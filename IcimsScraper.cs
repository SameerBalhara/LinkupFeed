using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LinkupFeed
{
    internal class IcimsScraper
    {
        private const int SourceId = 98;
        private const int Workers = 6;
        private const int DetailWorkers = 4;
        private static readonly Regex JobLinkPattern = new Regex("href=[\"'](?<href>[^\"']*/jobs/(?<job_id>\\d+)(?:/[^\"']*)?/job[^\"']*)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex HtmlStripPattern = new Regex(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex MultiSpacePattern = new Regex(@"\s+", RegexOptions.Compiled);

        private static readonly HttpClient _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        })
        {
            Timeout = TimeSpan.FromSeconds(20),
            DefaultRequestHeaders =
            {
                { "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8" },
                { "Accept-Language", "en-US,en;q=0.9" },
                { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" }
            }
        };

        public async Task<List<ScrapedJob>> FetchJobsAsync(string inputCsv = null, int? limitSites = null, int maxPages = 25, int maxJobsPerSite = 0)
        {
            inputCsv ??= System.IO.Path.Combine(Environment.CurrentDirectory, "outputs", "icims_jobs", "icims_link_counts_latest.csv");
            var rows = AtsCsv.ReadRows(inputCsv)
                .Where(r => !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "search_url")))
                .OrderByDescending(LinkCount)
                .ToList();

            if (limitSites.HasValue && limitSites.Value > 0) rows = rows.Take(limitSites.Value).ToList();

            Console.WriteLine($"[iCIMS] Loaded {rows.Count} URL rows from {inputCsv}");

            var allJobs = new List<ScrapedJob>();
            var gate = new SemaphoreSlim(Workers);
            var tasks = rows.Select(async row =>
            {
                await gate.WaitAsync();
                try { return await FetchSiteAsync(row, maxPages, maxJobsPerSite); }
                finally { gate.Release(); }
            }).ToList();

            foreach (var task in tasks) allJobs.AddRange(await task);
            return Dedupe(allJobs);
        }

        private static int LinkCount(Dictionary<string, string> row)
        {
            return int.TryParse(AtsCsv.Get(row, "job_link_count"), out var count) ||
                   int.TryParse(AtsCsv.Get(row, "job_links_found"), out count)
                ? count
                : 0;
        }

        private static async Task<List<ScrapedJob>> FetchSiteAsync(Dictionary<string, string> row, int maxPages, int maxJobsPerSite)
        {
            var domain = AtsCsv.Get(row, "domain");
            var searchUrl = AtsCsv.Get(row, "search_url");
            var baseUrl = string.IsNullOrWhiteSpace(AtsCsv.Get(row, "base_url"))
                ? $"https://{domain}"
                : AtsCsv.Get(row, "base_url");
            var links = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                for (int page = 0; page < maxPages; page++)
                {
                    var html = await _http.GetStringAsync(PagedSearchUrl(searchUrl, page));
                    var newLinks = ExtractJobLinks(baseUrl, html).Where(link => seen.Add(link)).ToList();
                    links.AddRange(newLinks);

                    if (newLinks.Count == 0) break;
                    if (maxJobsPerSite > 0 && links.Count >= maxJobsPerSite)
                    {
                        links = links.Take(maxJobsPerSite).ToList();
                        break;
                    }

                    await Task.Delay(50);
                }

                var detailLinks = links.Where(link => ItJobFilter.IsIt(TitleFromJobUrl(link), "")).ToList();
                var jobs = await FetchDetailsAsync(row, detailLinks);
                Console.WriteLine($"[iCIMS] {domain} links={links.Count} details={detailLinks.Count} jobs={jobs.Count}");
                return jobs;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[iCIMS] {domain} error: {ex.Message}");
                return new List<ScrapedJob>();
            }
        }

        private static async Task<List<ScrapedJob>> FetchDetailsAsync(Dictionary<string, string> site, List<string> links)
        {
            var jobs = new List<ScrapedJob>();
            var gate = new SemaphoreSlim(DetailWorkers);
            var tasks = links.Select(async link =>
            {
                await gate.WaitAsync();
                try { return await ExtractDetailJobAsync(site, link); }
                finally { gate.Release(); }
            }).ToList();

            foreach (var task in tasks)
            {
                var job = await task;
                if (job != null) jobs.Add(job);
            }

            return jobs;
        }

        private static async Task<ScrapedJob> ExtractDetailJobAsync(Dictionary<string, string> site, string jobUrl)
        {
            try
            {
                using var response = await _http.GetAsync(jobUrl);
                var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? jobUrl;
                if (!response.IsSuccessStatusCode) return null;

                var html = await response.Content.ReadAsStringAsync();
                var data = JsonLdJobPosting(html);
                var title = GetJsonString(data, "title");
                if (string.IsNullOrWhiteSpace(title)) title = ExtractMeta(html, "og:title");
                if (string.IsNullOrWhiteSpace(title)) title = ExtractH1(html);
                if (string.IsNullOrWhiteSpace(title)) title = TitleFromJobUrl(finalUrl).ToUpperFirst();

                var company = GetHiringOrganization(data);
                if (string.IsNullOrWhiteSpace(company)) company = CompanyFromDomain(AtsCsv.Get(site, "domain"));

                var location = LocationFromJsonLd(data);
                if (string.IsNullOrWhiteSpace(location)) location = ExtractLocationFromHtml(html);

                var description = StripTags(GetJsonString(data, "description"));
                if (string.IsNullOrWhiteSpace(description)) description = ExtractMeta(html, "description");

                var category = GetJsonString(data, "occupationalCategory") ?? GetJsonString(data, "industry") ?? "";
                var jobType = EmploymentType(data);
                var remote = IsRemote(location, title, description);

                if (string.IsNullOrWhiteSpace(title)) return null;
                if (!UsLocationFilter.IsUs(location) && !remote) return null;
                if (!ItJobFilter.IsIt(title, category)) return null;

                return new ScrapedJob
                {
                    SourceId = SourceId,
                    ExternalId = $"icims:{AtsCsv.Get(site, "domain").ToLowerInvariant()}:{JobIdFromUrl(finalUrl)}",
                    Title = title,
                    Company = company,
                    Location = string.IsNullOrWhiteSpace(location) && remote ? "Remote" : location,
                    Description = description,
                    JobUrl = finalUrl,
                    IsRemote = remote,
                    DatePosted = ParseDate(GetJsonString(data, "datePosted")),
                    JobType = jobType,
                    Category = category
                };
            }
            catch
            {
                return null;
            }
        }

        private static string PagedSearchUrl(string searchUrl, int page)
        {
            var builder = new UriBuilder(searchUrl);
            var query = ParseQuery(builder.Query);
            if (!query.ContainsKey("ss")) query["ss"] = "1";
            query["in_iframe"] = "1";
            query["pr"] = page.ToString();
            builder.Query = string.Join("&", query.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
            return builder.Uri.ToString();
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in (query ?? "").TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pieces = part.Split('=', 2);
                result[Uri.UnescapeDataString(pieces[0])] = pieces.Length > 1 ? Uri.UnescapeDataString(pieces[1]) : "";
            }
            return result;
        }

        private static List<string> ExtractJobLinks(string baseUrl, string html)
        {
            var links = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in JobLinkPattern.Matches(html ?? ""))
            {
                var url = new Uri(new Uri(baseUrl), WebUtility.HtmlDecode(match.Groups["href"].Value)).ToString().Split('#')[0];
                if (seen.Add(url)) links.Add(url);
            }
            return links;
        }

        private static string TitleFromJobUrl(string url)
        {
            var match = Regex.Match(new Uri(url).AbsolutePath, @"/jobs/\d+/([^/]+)/job", RegexOptions.IgnoreCase);
            return match.Success ? Regex.Replace(WebUtility.UrlDecode(match.Groups[1].Value), "[-_]+", " ").Trim() : "";
        }

        private static string JobIdFromUrl(string url)
        {
            var match = Regex.Match(url ?? "", @"/jobs/(\d+)(?:/|$)");
            return match.Success ? match.Groups[1].Value : url;
        }

        private static Dictionary<string, JsonElement> JsonLdJobPosting(string html)
        {
            foreach (Match match in Regex.Matches(html ?? "", "<script[^>]+type=[\"']application/ld\\+json[\"'][^>]*>(?<json>.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                try
                {
                    using var doc = JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups["json"].Value).Trim());
                    foreach (var item in EnumerateJsonLdItems(doc.RootElement))
                    {
                        if (IsJobPosting(item))
                        {
                            return item.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
                        }
                    }
                }
                catch { }
            }
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<JsonElement> EnumerateJsonLdItems(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray()) yield return item;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                yield return root;
            }
        }

        private static bool IsJobPosting(JsonElement item)
        {
            if (!item.TryGetProperty("@type", out var type)) return false;
            if (type.ValueKind == JsonValueKind.String) return string.Equals(type.GetString(), "JobPosting", StringComparison.OrdinalIgnoreCase);
            if (type.ValueKind == JsonValueKind.Array) return type.EnumerateArray().Any(v => string.Equals(v.GetString(), "JobPosting", StringComparison.OrdinalIgnoreCase));
            return false;
        }

        private static string LocationFromJsonLd(Dictionary<string, JsonElement> data)
        {
            if (!data.TryGetValue("jobLocation", out var locations) && !data.TryGetValue("applicantLocationRequirements", out locations)) return "";
            var items = locations.ValueKind == JsonValueKind.Array ? locations.EnumerateArray().ToList() : new List<JsonElement> { locations };
            var parts = new List<string>();
            foreach (var location in items)
            {
                var address = location;
                if (location.ValueKind == JsonValueKind.Object && location.TryGetProperty("address", out var nested)) address = nested;
                var text = string.Join(", ", new[]
                {
                    GetPropertyString(address, "addressLocality"),
                    GetPropertyString(address, "addressRegion"),
                    GetPropertyString(address, "addressCountry")
                }.Where(x => !string.IsNullOrWhiteSpace(x)));
                if (!string.IsNullOrWhiteSpace(text)) parts.Add(text);
            }
            return string.Join(" | ", parts);
        }

        private static string GetHiringOrganization(Dictionary<string, JsonElement> data)
        {
            return data.TryGetValue("hiringOrganization", out var org) && org.ValueKind == JsonValueKind.Object
                ? GetPropertyString(org, "name")
                : "";
        }

        private static string EmploymentType(Dictionary<string, JsonElement> data)
        {
            if (!data.TryGetValue("employmentType", out var value)) return "";
            if (value.ValueKind == JsonValueKind.Array) return string.Join(", ", value.EnumerateArray().Select(v => v.ToString()));
            return value.ToString();
        }

        private static string ExtractMeta(string html, string name)
        {
            var patterns = new[]
            {
                $"<meta[^>]+property=[\"']{Regex.Escape(name)}[\"'][^>]+content=[\"'](?<value>[^\"']*)[\"']",
                $"<meta[^>]+name=[\"']{Regex.Escape(name)}[\"'][^>]+content=[\"'](?<value>[^\"']*)[\"']",
                $"<meta[^>]+content=[\"'](?<value>[^\"']*)[\"'][^>]+(?:property|name)=[\"']{Regex.Escape(name)}[\"']"
            };
            foreach (var pattern in patterns)
            {
                var match = Regex.Match(html ?? "", pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (match.Success) return WebUtility.HtmlDecode(match.Groups["value"].Value).Trim();
            }
            return "";
        }

        private static string ExtractH1(string html)
        {
            var match = Regex.Match(html ?? "", "<h1[^>]*>(?<title>.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? StripTags(match.Groups["title"].Value) : "";
        }

        private static string ExtractLocationFromHtml(string html)
        {
            var match = Regex.Match(html ?? "", "(?:Location|Job Location)</[^>]+>\\s*<[^>]+>(?<loc>.*?)</", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? StripTags(match.Groups["loc"].Value) : "";
        }

        private static string CompanyFromDomain(string domain)
        {
            var first = (domain ?? "").Split('.').FirstOrDefault() ?? "";
            first = Regex.Replace(first, "^(careers|jobs|application|applicationinvite|advcareers|apjcareers|americas|emea|apac)[-_]?", "", RegexOptions.IgnoreCase);
            first = Regex.Replace(first, "[-_]+", " ").Trim();
            return string.IsNullOrWhiteSpace(first) ? domain : first.ToUpperFirst();
        }

        private static string StripTags(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            value = Regex.Replace(value, @"<(br|p|div|li|h\d)\b[^>]*>", " ", RegexOptions.IgnoreCase);
            value = HtmlStripPattern.Replace(value, " ");
            return MultiSpacePattern.Replace(WebUtility.HtmlDecode(value), " ").Trim();
        }

        private static bool IsRemote(params string[] values)
        {
            var text = string.Join(" ", values.Where(v => !string.IsNullOrWhiteSpace(v))).ToLowerInvariant();
            return text.Contains("remote") || text.Contains("work from home") || text.Contains("virtual");
        }

        private static DateTime? ParseDate(string value)
        {
            return DateTime.TryParse(value, out var parsed) ? parsed : null;
        }

        private static string GetJsonString(Dictionary<string, JsonElement> data, string key)
        {
            if (!data.TryGetValue(key, out var value)) return "";
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }

        private static string GetPropertyString(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value)) return "";
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }

        private static List<ScrapedJob> Dedupe(IEnumerable<ScrapedJob> jobs)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<ScrapedJob>();
            foreach (var job in jobs)
            {
                var key = !string.IsNullOrWhiteSpace(job.JobUrl)
                    ? job.JobUrl.Trim()
                    : !string.IsNullOrWhiteSpace(job.ExternalId)
                        ? job.ExternalId.Trim()
                        : $"{job.Title}|{job.Company}|{job.Location}";
                if (seen.Add(key)) results.Add(job);
            }
            return results;
        }
    }
}
