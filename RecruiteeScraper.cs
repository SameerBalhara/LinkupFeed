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
    // Recruitee exposes a public JSON feed per careers subdomain:
    //   GET https://{company}.recruitee.com/api/offers
    internal class RecruiteeScraper
    {
        private const int SOURCE_ID = 61;

        private static readonly (string Subdomain, string Company)[] Companies =
        {
            ("transperfect", "TransPerfect"),
            ("bunq", "bunq"),
            ("simvia", "Simvia")
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

        public async Task<List<ScrapedJob>> FetchJobsAsync(string onlySubdomain = null)
        {
            var results = new List<ScrapedJob>();
            var companies = Companies.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(onlySubdomain))
            {
                companies = companies.Where(c =>
                    string.Equals(c.Subdomain, onlySubdomain, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Company, onlySubdomain, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var (subdomain, fallbackCompanyName) in companies)
            {
                try
                {
                    Console.WriteLine($"[Recruitee] {subdomain} -> starting");

                    var url = $"https://{subdomain}.recruitee.com/api/offers";
                    var json = await FetchJsonAsync(url, subdomain);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("offers", out var offers) ||
                        offers.ValueKind != JsonValueKind.Array)
                    {
                        Console.WriteLine($"[Recruitee] {subdomain} -> unexpected payload shape");
                        continue;
                    }

                    int added = 0;
                    foreach (var offer in offers.EnumerateArray())
                    {
                        var location = FormatLocation(offer);
                        var isRemote = IsRemote(offer, location);

                        if (!UsLocationFilter.IsUs(location) && !isRemote)
                        {
                            continue;
                        }

                        results.Add(new ScrapedJob
                        {
                            SourceId = SOURCE_ID,
                            ExternalId = GetText(offer, "id") ?? GetText(offer, "guid") ?? GetText(offer, "slug"),
                            Title = GetTitle(offer),
                            Company = GetText(offer, "company_name") ?? fallbackCompanyName,
                            Location = location,
                            Description = BuildDescription(offer),
                            JobUrl = GetText(offer, "careers_url") ?? GetText(offer, "careers_apply_url"),
                            IsRemote = isRemote,
                            DatePosted = ParseDate(GetText(offer, "published_at") ?? GetText(offer, "created_at")),
                            JobType = GetText(offer, "employment_type_code"),
                            Category = GetText(offer, "department") ?? GetText(offer, "category_code")
                        });
                        added++;
                    }

                    Console.WriteLine($"[Recruitee] {subdomain} -> {added} US/remote jobs");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Recruitee] {subdomain} error: {ex.Message}");
                }

                await Task.Delay(800);
            }

            return results;
        }

        private static async Task<string> FetchJsonAsync(string url, string subdomain)
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
                    Console.WriteLine($"[Recruitee] {subdomain} -> timeout, retrying once");
                    await Task.Delay(2000);
                }
            }

            return await _http.GetStringAsync(url);
        }

        private static string GetTitle(JsonElement offer)
        {
            return GetText(offer, "title") ??
                   GetTranslationText(offer, "title") ??
                   GetText(offer, "sharing_title");
        }

        private static string FormatLocation(JsonElement offer)
        {
            var locations = GetLocationList(offer).ToList();
            if (locations.Count > 0)
            {
                return string.Join("; ", locations.Distinct(StringComparer.OrdinalIgnoreCase));
            }

            return GetText(offer, "location") ??
                   string.Join(", ",
                       new[]
                       {
                           GetText(offer, "city"),
                           GetText(offer, "state_name") ?? GetText(offer, "state_code"),
                           GetText(offer, "country")
                       }
                       .Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static IEnumerable<string> GetLocationList(JsonElement offer)
        {
            if (!offer.TryGetProperty("locations", out var locations) ||
                locations.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var loc in locations.EnumerateArray())
            {
                if (loc.ValueKind != JsonValueKind.Object) continue;

                var text = GetText(loc, "name");
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = string.Join(", ",
                        new[]
                        {
                            GetText(loc, "city"),
                            GetText(loc, "state") ?? GetText(loc, "state_code"),
                            GetText(loc, "country")
                        }
                        .Where(x => !string.IsNullOrWhiteSpace(x)));
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return text;
                }
            }
        }

        private static bool IsRemote(JsonElement offer, string location)
        {
            return GetBool(offer, "remote") == true ||
                   location.IndexOf("remote", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (GetText(offer, "title") ?? "").IndexOf("remote", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildDescription(JsonElement offer)
        {
            var description = GetTranslationText(offer, "description") ?? GetText(offer, "description");
            var requirements = GetTranslationText(offer, "requirements") ?? GetText(offer, "requirements");

            var parts = new[]
            {
                StripHtml(description),
                StripHtml(requirements)
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase);

            return string.Join("\n\n", parts);
        }

        private static string GetTranslationText(JsonElement offer, string prop)
        {
            if (!offer.TryGetProperty("translations", out var translations) ||
                translations.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (translations.TryGetProperty("en", out var english) &&
                english.ValueKind == JsonValueKind.Object)
            {
                var text = GetText(english, prop);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }

            foreach (var translation in translations.EnumerateObject())
            {
                if (translation.Value.ValueKind != JsonValueKind.Object) continue;

                var text = GetText(translation.Value, prop);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }

            return null;
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

        private static bool? GetBool(JsonElement el, string prop)
        {
            if (!el.TryGetProperty(prop, out var value)) return null;

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
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
