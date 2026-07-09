using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LinkupFeed
{
    // Dayforce exposes anonymous public job feeds per client:
    //   GET https://www.dayforcehcm.com/api/{client}/V1/JobFeeds
    internal class DayforceScraper
    {
        private const int SOURCE_ID = 60;

        private static readonly (string Client, string Company)[] Companies =
        {
            ("legence", "Legence"),
            ("isone", "ISO New England"),
            ("wplg", "WPLG"),
            ("sealaska", "Sealaska"),
            ("aquent", "Aquent"),
            ("gannett", "Gannett"),
            ("peaktech", "Peak Technologies"),
            ("marinerfinance", "Mariner Finance"),
            ("clearcaptions", "ClearCaptions"),
            ("devry", "DeVry"),
            ("aui", "Associated Universities Inc.")
        };

        private static readonly HttpClient _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip |
                                     DecompressionMethods.Deflate |
                                     DecompressionMethods.Brotli
        })
        {
            Timeout = TimeSpan.FromSeconds(60),
            DefaultRequestHeaders =
            {
                { "Accept", "application/json" },
                { "User-Agent", "ITJobCafe-Scraper/1.0" }
            }
        };

        private static readonly Regex HtmlStripPattern =
            new Regex(@"<[^>]+>", RegexOptions.Compiled);

        private static readonly Regex MultiSpacePattern =
            new Regex(@"\s{2,}", RegexOptions.Compiled);

        public async Task<List<ScrapedJob>> FetchJobsAsync(string onlyClient = null)
        {
            var results = new List<ScrapedJob>();
            var companies = Companies.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(onlyClient))
            {
                companies = companies.Where(c =>
                    string.Equals(c.Client, onlyClient, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Company, onlyClient, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var (client, fallbackCompanyName) in companies)
            {
                try
                {
                    Console.WriteLine($"[Dayforce] {client} -> starting");
                    var companyJobs = new List<ScrapedJob>();

                    var url = $"https://www.dayforcehcm.com/api/{Uri.EscapeDataString(client)}/V1/JobFeeds?includeActivePostingOnly=true";
                    var json = await FetchJsonAsync(url, client);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.ValueKind != JsonValueKind.Array)
                    {
                        Console.WriteLine($"[Dayforce] {client} -> unexpected payload shape");
                        continue;
                    }

                    foreach (var item in root.EnumerateArray())
                    {
                        var location = FormatLocation(item);
                        var isRemote = IsRemote(item, location);

                        if (!UsLocationFilter.IsUs(location) && !isRemote)
                        {
                            continue;
                        }

                        var description = BuildDescription(item);
                        var company = GetText(item, "CompanyName")
                                   ?? GetText(item, "ParentCompanyName")
                                   ?? fallbackCompanyName;

                        companyJobs.Add(new ScrapedJob
                        {
                            SourceId = SOURCE_ID,
                            ExternalId = GetText(item, "ReferenceNumber")
                                      ?? GetText(item, "ParentRequisitionCode")
                                      ?? GetText(item, "JobId"),
                            Title = GetText(item, "Title"),
                            Company = company,
                            Location = location,
                            Description = description,
                            JobUrl = GetText(item, "JobDetailsUrl") ?? GetText(item, "ApplyUrl"),
                            IsRemote = isRemote,
                            DatePosted = ParseDate(GetText(item, "DatePosted") ?? GetText(item, "LastUpdated")),
                            JobType = GetText(item, "EmploymentIndicator"),
                            Category = GetText(item, "JobFunction")
                        });
                    }

                    var merged = MergeDuplicatePostings(companyJobs);
                    results.AddRange(merged);

                    int added = merged.Count;
                    Console.WriteLine($"[Dayforce] {client} -> {added} US/remote jobs");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Dayforce] {client} error: {ex.Message}");
                }

                await Task.Delay(800);
            }

            return results;
        }

        private static List<ScrapedJob> MergeDuplicatePostings(List<ScrapedJob> jobs)
        {
            return jobs
                .GroupBy(j => !string.IsNullOrWhiteSpace(j.JobUrl)
                    ? j.JobUrl.Trim().ToLowerInvariant()
                    : !string.IsNullOrWhiteSpace(j.ExternalId)
                        ? j.ExternalId.Trim().ToLowerInvariant()
                        : Guid.NewGuid().ToString())
                .Select(group =>
                {
                    var best = group
                        .OrderByDescending(j => (j.Location ?? "").Length)
                        .ThenByDescending(j => (j.Description ?? "").Length)
                        .First();

                    var locations = group
                        .Select(j => j.Location)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (locations.Any(IsSpecificLocation))
                    {
                        locations = locations
                            .Where(IsSpecificLocation)
                            .ToList();
                    }

                    best.Location = string.Join("; ", locations);
                    best.IsRemote = group.Any(j => j.IsRemote);
                    best.Description = group
                        .Select(j => j.Description)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .OrderByDescending(x => x.Length)
                        .FirstOrDefault() ?? best.Description;

                    return best;
                })
                .ToList();
        }

        private static bool IsSpecificLocation(string location)
        {
            if (string.IsNullOrWhiteSpace(location)) return false;

            var normalized = location.Trim().ToLowerInvariant();
            return normalized != "united states" &&
                   normalized != "usa" &&
                   normalized != "us";
        }

        private static async Task<string> FetchJsonAsync(string url, string client)
        {
            const int maxAttempts = 2;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    return await _http.GetStringAsync(url);
                }
                catch (TaskCanceledException) when (attempt < maxAttempts)
                {
                    Console.WriteLine($"[Dayforce] {client} -> timeout, retrying once");
                    await Task.Delay(2000);
                }
            }

            return await _http.GetStringAsync(url);
        }

        private static string FormatLocation(JsonElement item)
        {
            return string.Join(", ",
                new[]
                {
                    GetText(item, "City"),
                    GetText(item, "State"),
                    FormatCountry(GetText(item, "Country"))
                }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static string FormatCountry(string country)
        {
            if (string.IsNullOrWhiteSpace(country)) return "";

            return country.Equals("US", StringComparison.OrdinalIgnoreCase) ||
                   country.Equals("USA", StringComparison.OrdinalIgnoreCase)
                ? "United States"
                : country;
        }

        private static bool IsRemote(JsonElement item, string location)
        {
            var telecommute = GetText(item, "TelecommutePercentage");
            if (decimal.TryParse(telecommute, out var pct) && pct > 0)
            {
                return true;
            }

            return location.IndexOf("remote", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (GetText(item, "Title") ?? "").IndexOf("remote", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (GetText(item, "Description") ?? "").IndexOf("remote", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildDescription(JsonElement item)
        {
            var parts = new[]
            {
                StripHtml(GetText(item, "Description")),
                StripHtml(GetText(item, "Education"))
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase);

            return string.Join("\n\n", parts);
        }

        private static string GetText(JsonElement el, string prop)
        {
            if (!el.TryGetProperty(prop, out var value)) return null;

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }

        private static DateTime? ParseDate(string raw)
        {
            return DateTime.TryParse(raw, out var dt) ? dt : (DateTime?)null;
        }

        private static string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;

            var text = HtmlStripPattern.Replace(html, " ");
            text = WebUtility.HtmlDecode(text);
            return MultiSpacePattern.Replace(text, " ").Trim();
        }
    }
}
