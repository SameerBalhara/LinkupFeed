using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LinkupFeed
{
    internal class ZohoRecruitScraper
    {
        private const int SourceId = 99;
        private const int Workers = 5;
        private const int DetailWorkers = 4;

        private static readonly Regex JobLinkPattern = new Regex("href=[\"'](?<href>[^\"']*(?:/jobs/Careers|/recruit/PortalDetail)[^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PortalRowPattern = new Regex(@"<tr[^>]+class=[""'][^""']*jobDetailRow[^""']*[""'][^>]*>(?<row>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex PortalAnchorPattern = new Regex(@"<a[^>]+class=[""'][^""']*jobdetail[^""']*[""'][^>]+href=[""'](?<href>[^""']+)[""'][^>]*>(?<text>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex CellPattern = new Regex(@"<td[^>]*>(?<value>.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex JsonLdPattern = new Regex("<script[^>]+type=[\"']application/ld\\+json[\"'][^>]*>(?<json>.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex TitlePattern = new Regex(@"<(?:h1|h2|title)[^>]*>(?<value>.*?)</(?:h1|h2|title)>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex HtmlStripPattern = new Regex(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex MultiSpacePattern = new Regex(@"\s+", RegexOptions.Compiled);

        private static readonly HttpClient _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        })
        {
            Timeout = TimeSpan.FromSeconds(25),
            DefaultRequestHeaders =
            {
                { "Accept", "text/html, application/json;q=0.9, */*;q=0.8" },
                { "Accept-Language", "en-US,en;q=0.9" },
                { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" }
            }
        };

        public async Task<List<ScrapedJob>> FetchJobsAsync(string inputCsv = null, int? limitSites = null, int maxJobsPerSite = 0)
        {
            inputCsv ??= System.IO.Path.Combine(Environment.CurrentDirectory, "outputs", "zohorecruit_jobs", "zohorecruit_public_urls_latest.csv");

            var rows = AtsCsv.ReadRows(inputCsv)
                .Where(r => !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "careers_url")) ||
                            !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "domain")))
                .OrderByDescending(JobCount)
                .ToList();

            if (limitSites.HasValue && limitSites.Value > 0) rows = rows.Take(limitSites.Value).ToList();

            Console.WriteLine($"[ZohoRecruit] Loaded {rows.Count} URL rows from {inputCsv}");

            var allJobs = new List<ScrapedJob>();
            using var gate = new SemaphoreSlim(Workers);
            var tasks = rows.Select(async row =>
            {
                await gate.WaitAsync();
                try { return await FetchSiteAsync(row, maxJobsPerSite); }
                finally { gate.Release(); }
            }).ToList();

            foreach (var task in tasks) allJobs.AddRange(await task);
            return Dedupe(allJobs);
        }

        private static int JobCount(Dictionary<string, string> row)
        {
            return int.TryParse(AtsCsv.Get(row, "job_links_found"), out var count) ||
                   int.TryParse(AtsCsv.Get(row, "total_jobs"), out count)
                ? count
                : 0;
        }

        private static async Task<List<ScrapedJob>> FetchSiteAsync(Dictionary<string, string> row, int maxJobsPerSite)
        {
            var domain = FirstNonEmpty(AtsCsv.Get(row, "domain"), DomainFromUrl(AtsCsv.Get(row, "careers_url")));
            var careersUrl = FirstNonEmpty(AtsCsv.Get(row, "careers_url"), PortalUrl(domain));
            if (string.IsNullOrWhiteSpace(domain)) return new List<ScrapedJob>();

            try
            {
                using var response = await _http.GetAsync(careersUrl);
                var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? careersUrl;
                var html = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode || LooksLikeLoginWall(finalUrl, html)) return new List<ScrapedJob>();

                var portalSummaries = ParsePortalRows(finalUrl, html);
                var links = portalSummaries.Count > 0
                    ? portalSummaries.Select(j => j.JobUrl).ToList()
                    : ExtractJobLinks(finalUrl, html);
                if (links.Count == 0)
                {
                    var inlineJobs = ParseJsonLdJobs(domain, finalUrl, html);
                    Console.WriteLine($"[ZohoRecruit] {domain} inline jobs={inlineJobs.Count}");
                    return inlineJobs;
                }

                if (maxJobsPerSite > 0) links = links.Take(maxJobsPerSite).ToList();

                var jobs = await FetchDetailsAsync(domain, links, portalSummaries);
                Console.WriteLine($"[ZohoRecruit] {domain} candidates={links.Count} jobs={jobs.Count}");
                return jobs;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ZohoRecruit] {domain} error: {ex.Message}");
                return new List<ScrapedJob>();
            }
        }

        private static async Task<List<ScrapedJob>> FetchDetailsAsync(string domain, List<string> urls, List<ZohoRecruitPortalSummary> summaries)
        {
            var jobs = new List<ScrapedJob>();
            var summaryByUrl = summaries.ToDictionary(s => s.JobUrl, StringComparer.OrdinalIgnoreCase);
            using var gate = new SemaphoreSlim(DetailWorkers);
            var tasks = urls.Select(async url =>
            {
                await gate.WaitAsync();
                try
                {
                    summaryByUrl.TryGetValue(url, out var summary);
                    return await FetchDetailAsync(domain, url, summary);
                }
                finally { gate.Release(); }
            }).ToList();

            foreach (var task in tasks)
            {
                var job = await task;
                if (job != null) jobs.Add(job);
            }

            return jobs;
        }

        private static async Task<ScrapedJob> FetchDetailAsync(string domain, string url, ZohoRecruitPortalSummary summary)
        {
            try
            {
                using var response = await _http.GetAsync(url);
                var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
                var html = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode || LooksLikeLoginWall(finalUrl, html)) return null;

                var jsonLd = ParseJsonLdJobs(domain, finalUrl, html).FirstOrDefault();
                if (jsonLd != null) return jsonLd;

                var title = FirstNonEmpty(summary?.Title, CleanText(HtmlTitle(html)));
                title = Regex.Replace(title, @"\s*[-|]\s*(Careers|Jobs).*$", "", RegexOptions.IgnoreCase).Trim();
                var description = MainText(html);
                var location = FirstNonEmpty(summary?.Location, LocationFromText(description));
                var remote = IsRemote(title, location);

                if (string.IsNullOrWhiteSpace(title)) return null;
                if (!UsLocationFilter.IsUs(location) && !remote) return null;

                return new ScrapedJob
                {
                    SourceId = SourceId,
                    ExternalId = TenantScopedReferenceId(domain, JobIdFromUrl(finalUrl)),
                    Title = title,
                    Company = CompanyFromDomain(domain),
                    Location = string.IsNullOrWhiteSpace(location) && remote ? "Remote" : location,
                    Description = description,
                    JobUrl = finalUrl,
                    IsRemote = remote,
                    DatePosted = null,
                    JobType = JobTypeFromText(description),
                    Category = JobCategoryMapper.Normalize("", title, description)
                };
            }
            catch
            {
                return null;
            }
        }

        internal static async Task<ZohoRecruitDiscoveryResult> DiscoverAsync(string domain)
        {
            foreach (var careersUrl in new[] { PortalUrl(domain), $"https://{domain}/jobs/Careers" })
            {
                try
                {
                    using var response = await _http.GetAsync(careersUrl);
                    var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? careersUrl;
                    var html = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode || LooksLikeLoginWall(finalUrl, html)) continue;

                    var portalSummaries = ParsePortalRows(finalUrl, html);
                    var links = portalSummaries.Count > 0
                        ? portalSummaries.Select(j => j.JobUrl).ToList()
                        : ExtractJobLinks(finalUrl, html);
                    var inlineJobs = links.Count == 0 ? ParseJsonLdJobs(domain, finalUrl, html) : new List<ScrapedJob>();
                    var count = links.Count > 0 ? links.Count : inlineJobs.Count;
                    if (count == 0) continue;

                    return new ZohoRecruitDiscoveryResult
                    {
                        CareersUrl = finalUrl,
                        JobCount = count,
                        SampleJobUrl = links.FirstOrDefault() ?? inlineJobs.FirstOrDefault()?.JobUrl ?? finalUrl,
                        SampleTitle = portalSummaries.FirstOrDefault()?.Title ?? inlineJobs.FirstOrDefault()?.Title ?? ""
                    };
                }
                catch
                {
                    continue;
                }
            }

            return null;
        }

        private static List<string> ExtractJobLinks(string baseUrl, string html)
        {
            var links = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in JobLinkPattern.Matches(html ?? ""))
            {
                var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
                if (href.EndsWith("/jobs/Careers", StringComparison.OrdinalIgnoreCase)) continue;
                var url = new Uri(new Uri(baseUrl), href).ToString().Split('#')[0];
                if (seen.Add(url)) links.Add(url);
            }

            return links;
        }

        private static List<ZohoRecruitPortalSummary> ParsePortalRows(string baseUrl, string html)
        {
            var jobs = new List<ZohoRecruitPortalSummary>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match rowMatch in PortalRowPattern.Matches(html ?? ""))
            {
                var row = rowMatch.Groups["row"].Value;
                var anchor = PortalAnchorPattern.Match(row);
                var link = anchor.Success
                    ? new Uri(new Uri(baseUrl), WebUtility.HtmlDecode(anchor.Groups["href"].Value)).ToString().Split('#')[0]
                    : ExtractJobLinks(baseUrl, row).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(link) || !seen.Add(link)) continue;

                var cells = CellPattern.Matches(row)
                    .Cast<Match>()
                    .Select(m => StripTags(m.Groups["value"].Value))
                    .Where(v => !string.IsNullOrWhiteSpace(v) &&
                                !string.Equals(v, "APPLY NOW", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var title = BestTitleFromPortalRow(anchor.Success ? StripTags(anchor.Groups["text"].Value) : "", cells);
                var locationParts = cells
                    .Where(v => !string.Equals(v, title, StringComparison.OrdinalIgnoreCase))
                    .Where(v => !IsLikelyDate(v) && !IsLikelyJobCode(v) && !IsLikelyJobType(v))
                    .Take(2)
                    .ToList();
                var location = string.Join(", ", locationParts);

                jobs.Add(new ZohoRecruitPortalSummary
                {
                    JobUrl = link,
                    Title = title,
                    Location = location
                });
            }

            return jobs;
        }

        private static string BestTitleFromPortalRow(string anchorText, List<string> cells)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(anchorText)) candidates.Add(anchorText);
            candidates.AddRange(cells);

            candidates = candidates
                .Select(CleanText)
                .Where(v => !string.IsNullOrWhiteSpace(v) &&
                            !IsLikelyDate(v) &&
                            !IsLikelyJobCode(v) &&
                            !IsLikelyJobType(v) &&
                            Regex.IsMatch(v, "[A-Za-z]"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var titleLike = candidates.FirstOrDefault(v => Regex.IsMatch(v,
                @"\b(engineer|developer|analyst|architect|administrator|consultant|manager|specialist|technician|designer|lead|director|scientist|security|salesforce|cloud|software|data|systems?)\b",
                RegexOptions.IgnoreCase));

            return titleLike ?? candidates.FirstOrDefault() ?? anchorText ?? "";
        }

        private static bool IsLikelyDate(string value)
        {
            return DateTime.TryParse(value, out _);
        }

        private static bool IsLikelyJobCode(string value)
        {
            return Regex.IsMatch(value ?? "", @"^(ZR_)?\d+(_JOB)?$", RegexOptions.IgnoreCase);
        }

        private static bool IsLikelyJobType(string value)
        {
            return Regex.IsMatch(value ?? "", @"^(full[- ]?time|part[- ]?time|contract|temporary|internship|freelance)$", RegexOptions.IgnoreCase);
        }

        private static List<ScrapedJob> ParseJsonLdJobs(string domain, string url, string html)
        {
            var jobs = new List<ScrapedJob>();
            foreach (Match match in JsonLdPattern.Matches(html ?? ""))
            {
                try
                {
                    using var doc = JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups["json"].Value).Trim());
                    foreach (var item in EnumerateJsonLdItems(doc.RootElement))
                    {
                        if (!IsJobPosting(item)) continue;

                        var title = CleanText(GetJsonString(item, "title"));
                        var description = StripTags(GetJsonString(item, "description"));
                        var location = LocationFromJsonLd(item);
                        var remote = IsRemote(GetJsonString(item, "jobLocationType"), location, title, description);
                        if (string.IsNullOrWhiteSpace(title)) continue;
                        if (!UsLocationFilter.IsUs(location) && !remote) continue;

                        jobs.Add(new ScrapedJob
                        {
                            SourceId = SourceId,
                            ExternalId = TenantScopedReferenceId(domain, FirstNonEmpty(GetIdentifier(item), JobIdFromUrl(url), title)),
                            Title = title,
                            Company = FirstNonEmpty(HiringOrganization(item), CompanyFromDomain(domain)),
                            Location = string.IsNullOrWhiteSpace(location) && remote ? "Remote" : location,
                            Description = description,
                            JobUrl = FirstNonEmpty(GetJsonString(item, "url"), url),
                            IsRemote = remote,
                            DatePosted = ParseDate(GetJsonString(item, "datePosted")),
                            JobType = EmploymentType(item),
                            Category = JobCategoryMapper.Normalize(GetJsonString(item, "occupationalCategory"), title, description)
                        });
                    }
                }
                catch { }
            }

            return jobs;
        }


        private static string TenantScopedReferenceId(string domain, string id)
        {
            var tenant = NormalizeReferenceTenant(domain);
            var raw = string.IsNullOrWhiteSpace(id) ? "" : id.Trim();
            var fingerprint = $"zohorecruit|{tenant}|{raw}".ToLowerInvariant();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint));
            return $"{SourceId}:h{Convert.ToHexString(hash, 0, 8).ToLowerInvariant()}";
        }

        private static string NormalizeReferenceTenant(string domain)
        {
            var value = (domain ?? "").Trim().ToLowerInvariant();
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri)) value = uri.Host;
            value = value.Split('/')[0].Trim();
            return value.StartsWith("www.") ? value.Substring(4) : value;
        }        private static IEnumerable<JsonElement> EnumerateJsonLdItems(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray()) yield return item;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in graph.EnumerateArray()) yield return item;
                }
                else
                {
                    yield return root;
                }
            }
        }

        private static bool IsJobPosting(JsonElement item)
        {
            if (!item.TryGetProperty("@type", out var type)) return false;
            if (type.ValueKind == JsonValueKind.String) return string.Equals(type.GetString(), "JobPosting", StringComparison.OrdinalIgnoreCase);
            if (type.ValueKind == JsonValueKind.Array) return type.EnumerateArray().Any(v => string.Equals(v.GetString(), "JobPosting", StringComparison.OrdinalIgnoreCase));
            return false;
        }

        private static string LocationFromJsonLd(JsonElement item)
        {
            if (!item.TryGetProperty("jobLocation", out var locations) &&
                !item.TryGetProperty("applicantLocationRequirements", out locations))
            {
                return "";
            }

            var items = locations.ValueKind == JsonValueKind.Array ? locations.EnumerateArray().ToList() : new List<JsonElement> { locations };
            var parts = new List<string>();
            foreach (var loc in items)
            {
                if (loc.ValueKind == JsonValueKind.Object)
                {
                    var address = loc.TryGetProperty("address", out var nested) ? nested : loc;
                    parts.Add(string.Join(", ", new[]
                    {
                        GetJsonString(address, "addressLocality"),
                        GetJsonString(address, "addressRegion"),
                        GetJsonString(address, "addressCountry"),
                        GetJsonString(loc, "name")
                    }.Where(v => !string.IsNullOrWhiteSpace(v))));
                }
                else if (loc.ValueKind == JsonValueKind.String)
                {
                    parts.Add(loc.GetString());
                }
            }

            return string.Join(" | ", parts.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private static string HiringOrganization(JsonElement item)
        {
            return item.TryGetProperty("hiringOrganization", out var org) && org.ValueKind == JsonValueKind.Object
                ? GetJsonString(org, "name")
                : "";
        }

        private static string EmploymentType(JsonElement item)
        {
            if (!item.TryGetProperty("employmentType", out var value)) return "";
            if (value.ValueKind == JsonValueKind.Array) return string.Join(", ", value.EnumerateArray().Select(v => v.ToString()));
            return value.ToString();
        }

        private static string GetIdentifier(JsonElement item)
        {
            if (!item.TryGetProperty("identifier", out var identifier)) return "";
            if (identifier.ValueKind == JsonValueKind.Object) return GetJsonString(identifier, "value");
            return identifier.ToString();
        }

        private static string GetJsonString(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value)) return "";
            if (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined) return "";
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
        }

        private static bool LooksLikeLoginWall(string url, string html)
        {
            return (url ?? "").Contains("login", StringComparison.OrdinalIgnoreCase) ||
                   (url ?? "").Contains("IAMSecurityError", StringComparison.OrdinalIgnoreCase) ||
                   (url ?? "").Contains("/clientportal", StringComparison.OrdinalIgnoreCase) ||
                   (html ?? "").Contains("signin", StringComparison.OrdinalIgnoreCase);
        }

        private static string PortalUrl(string domain)
        {
            return $"https://{domain}/recruit/Portal.na?digest=";
        }

        private static string HtmlTitle(string html)
        {
            var match = TitlePattern.Match(html ?? "");
            return match.Success ? StripTags(match.Groups["value"].Value) : "";
        }

        private static string MainText(string html)
        {
            var text = StripTags(html);
            return text.Length > 5000 ? text.Substring(0, 5000) : text;
        }

        private static string LocationFromText(string text)
        {
            var match = Regex.Match(text ?? "", @"\b([A-Z][A-Za-z .'-]+,\s*(AL|AK|AZ|AR|CA|CO|CT|DE|FL|GA|HI|ID|IL|IN|IA|KS|KY|LA|ME|MD|MA|MI|MN|MS|MO|MT|NE|NV|NH|NJ|NM|NY|NC|ND|OH|OK|OR|PA|RI|SC|SD|TN|TX|UT|VT|VA|WA|WV|WI|WY|DC))\b");
            return match.Success ? CleanText(match.Groups[1].Value) : "";
        }

        private static string JobIdFromUrl(string url)
        {
            var match = Regex.Match(url ?? "", @"(?:[?&]jobid=|[?&]JobOpeningId=)(?<id>[A-Za-z0-9_-]{4,})", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups["id"].Value;

            match = Regex.Match(url ?? "", @"/(?<id>[A-Za-z0-9_-]{4,})(?:[/?#-]|$)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["id"].Value : url;
        }

        private static string JobTypeFromText(string text)
        {
            if (Regex.IsMatch(text ?? "", @"\bcontract\b", RegexOptions.IgnoreCase)) return "Contract";
            if (Regex.IsMatch(text ?? "", @"\bpart[- ]time\b", RegexOptions.IgnoreCase)) return "Part Time";
            if (Regex.IsMatch(text ?? "", @"\bfull[- ]time\b", RegexOptions.IgnoreCase)) return "Full Time";
            return "";
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

        private static string StripTags(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            value = Regex.Replace(value, @"<(br|p|div|li|h\d)\b[^>]*>", " ", RegexOptions.IgnoreCase);
            value = HtmlStripPattern.Replace(value, " ");
            return CleanText(value);
        }

        private static string CleanText(string value)
        {
            return MultiSpacePattern.Replace(WebUtility.HtmlDecode(value ?? ""), " ").Trim();
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }

            return "";
        }

        private static string CompanyFromDomain(string domain)
        {
            var first = (domain ?? "").Split('.').FirstOrDefault() ?? "";
            first = Regex.Replace(first, "[-_]+", " ").Trim();
            return string.IsNullOrWhiteSpace(first) ? domain : first.ToUpperFirst();
        }

        private static string DomainFromUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : "";
        }

        private static List<ScrapedJob> Dedupe(IEnumerable<ScrapedJob> jobs)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<ScrapedJob>();
            foreach (var job in jobs)
            {
                var key = !string.IsNullOrWhiteSpace(job.ExternalId) ? job.ExternalId.Trim() : job.JobUrl?.Trim();
                if (!string.IsNullOrWhiteSpace(key) && seen.Add(key)) results.Add(job);
            }

            return results;
        }

        internal sealed class ZohoRecruitDiscoveryResult
        {
            public string CareersUrl { get; set; }
            public int JobCount { get; set; }
            public string SampleJobUrl { get; set; }
            public string SampleTitle { get; set; }
        }

        private sealed class ZohoRecruitPortalSummary
        {
            public string JobUrl { get; set; }
            public string Title { get; set; }
            public string Location { get; set; }
        }
    }
}
