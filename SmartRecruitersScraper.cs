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
    // SmartRecruiters exposes a public JSON posting API per company:
    //   GET https://api.smartrecruiters.com/v1/companies/{company}/postings
    //   GET https://api.smartrecruiters.com/v1/companies/{company}/postings/{postingId}
    internal class SmartRecruitersScraper
    {
        private const int SOURCE_ID = 59;
        private const int PageSize = 100;

        private static readonly (string Identifier, string Company)[] Companies =
        {
            ("smartrecruiters", "SmartRecruiters"),
            ("Visa", "Visa")
        };

        private static readonly HttpClient _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip |
                                     DecompressionMethods.Deflate |
                                     DecompressionMethods.Brotli
        })
        {
            Timeout = TimeSpan.FromSeconds(45),
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

        public async Task<List<ScrapedJob>> FetchJobsAsync(string onlyCompany = null)
        {
            var companies = Companies.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(onlyCompany))
            {
                companies = companies.Where(c =>
                    string.Equals(c.Identifier, onlyCompany, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Company, onlyCompany, StringComparison.OrdinalIgnoreCase));
            }

            return await FetchJobsForCompaniesAsync(companies);
        }

        public async Task<List<ScrapedJob>> FetchJobsFromCsvAsync(string inputCsv, int? limitSites = null)
        {
            var rows = AtsCsv.ReadRows(inputCsv)
                .Select(row =>
                {
                    var identifier = FirstNonEmpty(
                        AtsCsv.Get(row, "identifier"),
                        AtsCsv.Get(row, "tenant"),
                        AtsCsv.Get(row, "company"));
                    var company = FirstNonEmpty(AtsCsv.Get(row, "company"), identifier);
                    return (Identifier: identifier, Company: company);
                })
                .Where(c => !string.IsNullOrWhiteSpace(c.Identifier))
                .GroupBy(c => c.Identifier, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (limitSites.HasValue)
            {
                rows = rows.Take(limitSites.Value).ToList();
            }

            Console.WriteLine($"[SmartRecruiters] Loaded {rows.Count} company rows from {inputCsv}");
            return await FetchJobsForCompaniesAsync(rows);
        }

        private async Task<List<ScrapedJob>> FetchJobsForCompaniesAsync(IEnumerable<(string Identifier, string Company)> companies)
        {
            var results = new List<ScrapedJob>();

            foreach (var (identifier, fallbackCompanyName) in companies)
            {
                try
                {
                    Console.WriteLine($"[SmartRecruiters] {identifier} -> starting");
                    var postings = await FetchPostingsAsync(identifier);

                    int added = 0;
                    foreach (var batch in postings.Chunk(8))
                    {
                        var mappedJobs = await Task.WhenAll(batch.Select(async posting =>
                        {
                            try
                            {
                                return await MapPostingAsync(identifier, fallbackCompanyName, posting);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[SmartRecruiters] {identifier} posting error: {ex.Message}");
                                return null;
                            }
                        }));

                        foreach (var job in mappedJobs.Where(job => job != null))
                        {
                            results.Add(job);
                            added++;
                        }

                        await Task.Delay(150);
                    }

                    Console.WriteLine($"[SmartRecruiters] {identifier} -> {added} US/remote jobs");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SmartRecruiters] {identifier} error: {ex.Message}");
                }

                await Task.Delay(800);
            }

            return results;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
        }

        private static async Task<List<JsonElement>> FetchPostingsAsync(string identifier)
        {
            var postings = new List<JsonElement>();
            int offset = 0;
            int totalFound = 0;

            do
            {
                var url = $"https://api.smartrecruiters.com/v1/companies/{Uri.EscapeDataString(identifier)}/postings?limit={PageSize}&offset={offset}";
                var json = await FetchJsonAsync(url, identifier);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                totalFound = GetInt(root, "totalFound") ?? 0;

                if (!root.TryGetProperty("content", out var content) ||
                    content.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine($"[SmartRecruiters] {identifier} -> unexpected payload shape");
                    break;
                }

                foreach (var posting in content.EnumerateArray())
                {
                    postings.Add(posting.Clone());
                }

                offset += PageSize;
            }
            while (offset < totalFound);

            return postings;
        }

        private static async Task<ScrapedJob> MapPostingAsync(string identifier, string fallbackCompanyName, JsonElement posting)
        {
            var listingLocation = FormatLocation(posting);
            var listingIsRemote = IsRemote(posting, listingLocation);
            if (!UsLocationFilter.IsUs(listingLocation) && !listingIsRemote)
            {
                return null;
            }

            var details = await FetchDetailsAsync(identifier, posting);
            var source = details ?? posting;

            var location = FormatLocation(source);
            var isRemote = IsRemote(source, location);

            if (!UsLocationFilter.IsUs(location) && !isRemote)
            {
                return null;
            }

            var company = GetNestedText(source, "company", "name") ?? fallbackCompanyName;
            var description = BuildDescription(source);

            return new ScrapedJob
            {
                SourceId = SOURCE_ID,
                ExternalId = GetText(source, "uuid") ?? GetText(source, "id"),
                Title = GetText(source, "name"),
                Company = company,
                Location = location,
                Description = description,
                JobUrl = GetText(source, "postingUrl") ?? GetText(source, "applyUrl") ?? GetText(source, "ref"),
                IsRemote = isRemote,
                DatePosted = ParseDate(GetText(source, "releasedDate")),
                JobType = GetNestedText(source, "typeOfEmployment", "label"),
                Category = BuildCategory(source)
            };
        }

        private static async Task<JsonElement?> FetchDetailsAsync(string identifier, JsonElement posting)
        {
            var detailUrl = GetText(posting, "ref");
            if (string.IsNullOrWhiteSpace(detailUrl))
            {
                var id = GetText(posting, "id") ?? GetText(posting, "uuid");
                if (string.IsNullOrWhiteSpace(id)) return null;

                detailUrl = $"https://api.smartrecruiters.com/v1/companies/{Uri.EscapeDataString(identifier)}/postings/{Uri.EscapeDataString(id)}";
            }

            var json = await FetchJsonAsync(detailUrl, identifier);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

        private static async Task<string> FetchJsonAsync(string url, string identifier)
        {
            const int maxAttempts = 4;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    return await _http.GetStringAsync(url);
                }
                catch (TaskCanceledException) when (attempt < maxAttempts)
                {
                    Console.WriteLine($"[SmartRecruiters] {identifier} -> timeout, retrying ({attempt}/{maxAttempts})");
                    await Task.Delay(1000 * attempt);
                }
                catch (HttpRequestException ex) when (
                    attempt < maxAttempts &&
                    (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                     ex.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
                     (int?)ex.StatusCode >= 500))
                {
                    Console.WriteLine($"[SmartRecruiters] {identifier} -> HTTP {(int?)ex.StatusCode}, retrying ({attempt}/{maxAttempts})");
                    await Task.Delay(1500 * attempt);
                }
            }

            return await _http.GetStringAsync(url);
        }

        private static string FormatLocation(JsonElement posting)
        {
            if (!posting.TryGetProperty("location", out var location) ||
                location.ValueKind != JsonValueKind.Object)
            {
                return "";
            }

            var fullLocation = GetText(location, "fullLocation");
            if (!string.IsNullOrWhiteSpace(fullLocation))
            {
                return fullLocation;
            }

            return string.Join(", ",
                new[]
                {
                    GetText(location, "city"),
                    GetText(location, "region"),
                    FormatCountry(GetText(location, "country"))
                }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static string FormatCountry(string country)
        {
            if (string.IsNullOrWhiteSpace(country)) return "";

            return country.Equals("us", StringComparison.OrdinalIgnoreCase)
                ? "United States"
                : country;
        }

        private static bool IsRemote(JsonElement posting, string location)
        {
            if (posting.TryGetProperty("location", out var loc) &&
                loc.ValueKind == JsonValueKind.Object &&
                loc.TryGetProperty("remote", out var remote) &&
                remote.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            return location.IndexOf("remote", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildCategory(JsonElement posting)
        {
            return string.Join(" / ",
                new[]
                {
                    GetNestedText(posting, "department", "label"),
                    GetNestedText(posting, "function", "label")
                }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private static string BuildDescription(JsonElement posting)
        {
            if (!posting.TryGetProperty("jobAd", out var jobAd) ||
                jobAd.ValueKind != JsonValueKind.Object ||
                !jobAd.TryGetProperty("sections", out var sections) ||
                sections.ValueKind != JsonValueKind.Object)
            {
                return "";
            }

            var parts = new List<string>();
            foreach (var section in sections.EnumerateObject())
            {
                var title = GetText(section.Value, "title");
                var text = StripHtml(GetText(section.Value, "text"));

                if (string.IsNullOrWhiteSpace(text)) continue;

                parts.Add(string.IsNullOrWhiteSpace(title)
                    ? text
                    : $"{title}\n{text}");
            }

            return string.Join("\n\n", parts);
        }

        private static string GetNestedText(JsonElement el, string objectProp, string textProp)
        {
            return el.TryGetProperty(objectProp, out var nested) &&
                   nested.ValueKind == JsonValueKind.Object
                ? GetText(nested, textProp)
                : null;
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

        private static int? GetInt(JsonElement el, string prop)
        {
            return el.TryGetProperty(prop, out var value) &&
                   value.ValueKind == JsonValueKind.Number &&
                   value.TryGetInt32(out var number)
                ? number
                : (int?)null;
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
