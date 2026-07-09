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
            var results = new List<ScrapedJob>();
            var companies = Companies.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(onlyCompany))
            {
                companies = companies.Where(c =>
                    string.Equals(c.Identifier, onlyCompany, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Company, onlyCompany, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var (identifier, fallbackCompanyName) in companies)
            {
                try
                {
                    Console.WriteLine($"[SmartRecruiters] {identifier} -> starting");
                    var postings = await FetchPostingsAsync(identifier);

                    int added = 0;
                    foreach (var posting in postings)
                    {
                        try
                        {
                            var job = await MapPostingAsync(identifier, fallbackCompanyName, posting);
                            if (job == null) continue;

                            results.Add(job);
                            added++;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[SmartRecruiters] {identifier} posting error: {ex.Message}");
                        }

                        await Task.Delay(100);
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
            const int maxAttempts = 2;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    return await _http.GetStringAsync(url);
                }
                catch (TaskCanceledException) when (attempt < maxAttempts)
                {
                    Console.WriteLine($"[SmartRecruiters] {identifier} -> timeout, retrying once");
                    await Task.Delay(2000);
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
