using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace LinkupFeed
{
    internal class PinpointHqScraper
    {
        private const int SourceId = 94;
        private const int Workers = 6;
        private const int DetailWorkers = 4;

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
                { "Accept", "application/rss+xml, application/xml;q=0.9, text/html;q=0.8, */*;q=0.7" },
                { "Accept-Language", "en-US,en;q=0.9" },
                { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" }
            }
        };

        public async Task<List<ScrapedJob>> FetchJobsAsync(string inputCsv = null, int? limitSites = null, int maxJobsPerSite = 0)
        {
            inputCsv ??= System.IO.Path.Combine(Environment.CurrentDirectory, "outputs", "pinpointhq_jobs", "pinpointhq_link_counts_latest.csv");

            var rows = AtsCsv.ReadRows(inputCsv)
                .Where(r => !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "rss_url")) ||
                            !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "domain")))
                .OrderByDescending(JobCount)
                .ToList();

            if (limitSites.HasValue && limitSites.Value > 0) rows = rows.Take(limitSites.Value).ToList();

            Console.WriteLine($"[PinpointHQ] Loaded {rows.Count} URL rows from {inputCsv}");

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
                   int.TryParse(AtsCsv.Get(row, "total_jobs"), out count) ||
                   int.TryParse(AtsCsv.Get(row, "job_count"), out count)
                ? count
                : 0;
        }

        private static async Task<List<ScrapedJob>> FetchSiteAsync(Dictionary<string, string> row, int maxJobsPerSite)
        {
            var domain = FirstNonEmpty(AtsCsv.Get(row, "domain"), DomainFromUrl(AtsCsv.Get(row, "rss_url")));
            var rssUrl = FirstNonEmpty(AtsCsv.Get(row, "rss_url"), $"https://{domain}/jobs.rss");
            if (string.IsNullOrWhiteSpace(domain)) return new List<ScrapedJob>();

            try
            {
                var rss = await _http.GetStringAsync(rssUrl);
                var summaries = ParseFeed(domain, rss);

                if (maxJobsPerSite > 0) summaries = summaries.Take(maxJobsPerSite).ToList();

                var jobs = await FetchDetailsAsync(summaries);
                Console.WriteLine($"[PinpointHQ] {domain} candidates={summaries.Count} jobs={jobs.Count}");
                return jobs;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PinpointHQ] {domain} error: {ex.Message}");
                return new List<ScrapedJob>();
            }
        }

        private static List<PinpointJobSummary> ParseFeed(string domain, string xml)
        {
            var summaries = new List<PinpointJobSummary>();
            var doc = XDocument.Parse(xml);

            foreach (var item in doc.Descendants("item"))
            {
                var title = CleanText((string)item.Element("title"));
                var link = CleanText((string)item.Element("link"));
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link)) continue;

                summaries.Add(new PinpointJobSummary
                {
                    Id = JobIdFromUrl(link),
                    Title = title,
                    Company = CompanyFromDomain(domain),
                    Location = LocationFromTitle(title),
                    IsRemote = IsRemote(title, link),
                    JobUrl = link,
                    DatePosted = ParseDate(CleanText((string)item.Element("pubDate")))
                });
            }

            return summaries;
        }

        private static async Task<List<ScrapedJob>> FetchDetailsAsync(List<PinpointJobSummary> summaries)
        {
            var jobs = new List<ScrapedJob>();
            using var gate = new SemaphoreSlim(DetailWorkers);
            var tasks = summaries.Select(async summary =>
            {
                await gate.WaitAsync();
                try { return await FetchDetailAsync(summary); }
                finally { gate.Release(); }
            }).ToList();

            foreach (var task in tasks)
            {
                var job = await task;
                if (job != null) jobs.Add(job);
            }

            return jobs;
        }

        private static async Task<ScrapedJob> FetchDetailAsync(PinpointJobSummary summary)
        {
            try
            {
                using var response = await _http.GetAsync(summary.JobUrl);
                var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? summary.JobUrl;
                if (!response.IsSuccessStatusCode) return null;

                var html = await response.Content.ReadAsStringAsync();
                var data = JsonLdJobPosting(html);

                var title = CleanText(FirstNonEmpty(GetJsonString(data, "title"), summary.Title));
                var description = StripTags(GetJsonString(data, "description"));
                if (string.IsNullOrWhiteSpace(description)) description = ExtractMeta(html, "description");

                var company = FirstNonEmpty(GetHiringOrganization(data), summary.Company);
                var location = FirstNonEmpty(LocationFromJsonLd(data), summary.Location);
                var category = FirstNonEmpty(
                    ExtractDescriptionLabel(description, "Department"),
                    GetJsonString(data, "occupationalCategory"),
                    GetJsonString(data, "industry"),
                    InferCategory(title, description));
                var jobType = FirstNonEmpty(EmploymentType(data), ExtractDescriptionLabel(description, "Employment Type"));
                var datePosted = ParseDate(GetJsonString(data, "datePosted")) ?? summary.DatePosted;
                var remote = summary.IsRemote || IsRemote(GetJsonString(data, "jobLocationType"), location, title, description);
                var referenceId = NormalizePostingUuid(FirstNonEmpty(IdentifierValue(data), PostingUuidFromHtml(html), summary.Id, finalUrl));

                if (string.IsNullOrWhiteSpace(title)) return null;
                if (string.IsNullOrWhiteSpace(referenceId)) return null;
                if (!UsLocationFilter.IsUs(location) && !remote) return null;

                return new ScrapedJob
                {
                    SourceId = SourceId,
                    ExternalId = $"{SourceId}:{referenceId}",
                    Title = title,
                    Company = company,
                    Location = string.IsNullOrWhiteSpace(location) && remote ? "Remote" : location,
                    Description = description,
                    JobUrl = finalUrl,
                    IsRemote = remote,
                    DatePosted = datePosted,
                    JobType = jobType,
                    Category = category
                };
            }
            catch
            {
                return null;
            }
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

        private static string LocationFromJsonLd(Dictionary<string, JsonElement> data)
        {
            if (!data.TryGetValue("jobLocation", out var locations) &&
                !data.TryGetValue("applicantLocationRequirements", out locations))
            {
                return "";
            }

            var items = locations.ValueKind == JsonValueKind.Array
                ? locations.EnumerateArray().ToList()
                : new List<JsonElement> { locations };
            var parts = new List<string>();

            foreach (var item in items)
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var explicitName = GetPropertyString(item, "name");
                    if (!string.IsNullOrWhiteSpace(explicitName))
                    {
                        parts.Add(explicitName);
                        continue;
                    }

                    var address = item.TryGetProperty("address", out var nested) ? nested : item;
                    var text = string.Join(", ", new[]
                    {
                        GetPropertyString(address, "addressLocality"),
                        GetPropertyString(address, "addressRegion"),
                        GetPropertyString(address, "addressCountry")
                    }.Where(v => !string.IsNullOrWhiteSpace(v)));

                    if (!string.IsNullOrWhiteSpace(text)) parts.Add(text);
                }
                else if (item.ValueKind == JsonValueKind.String)
                {
                    parts.Add(item.GetString());
                }
            }

            return string.Join(" | ", parts.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase));
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

        private static string NormalizePostingUuid(string value)
        {
            var match = Regex.Match(value ?? "", @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase);
            return match.Success ? match.Value.ToLowerInvariant() : "";
        }
        private static string IdentifierValue(Dictionary<string, JsonElement> data)
        {
            if (!data.TryGetValue("identifier", out var identifier) || identifier.ValueKind != JsonValueKind.Object) return "";
            return GetPropertyString(identifier, "value");
        }

        private static string PostingUuidFromHtml(string html)
        {
            var match = Regex.Match(html ?? "", @"/postings/(?<id>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["id"].Value.ToLowerInvariant() : "";
        }

        private static string ExtractDescriptionLabel(string description, string label)
        {
            if (string.IsNullOrWhiteSpace(description)) return "";
            var pattern = $@"\b{Regex.Escape(label)}:\s*(?<value>[^.:\r\n]+?)(?=\s+[A-Z][A-Za-z ]{{2,}}:|\s+Description\b|\s+Key Responsibilities\b|$)";
            var match = Regex.Match(description, pattern, RegexOptions.IgnoreCase);
            return match.Success ? CleanText(match.Groups["value"].Value) : "";
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
                if (match.Success) return CleanText(match.Groups["value"].Value);
            }

            return "";
        }

        private static string InferCategory(string title, string description)
        {
            var text = $"{title} {description}";
            if (Regex.IsMatch(text, @"\b(cyber|security|soc|siem|isso|issm|iam)\b", RegexOptions.IgnoreCase)) return "Cybersecurity";
            if (Regex.IsMatch(text, @"\b(data scientist|machine learning|ml engineer|artificial intelligence|ai engineer|nlp)\b", RegexOptions.IgnoreCase)) return "AI / Machine Learning";
            if (Regex.IsMatch(text, @"\b(data engineer|data analyst|analytics|business intelligence|power bi|tableau|etl|sql)\b", RegexOptions.IgnoreCase)) return "Data / Analytics";
            if (Regex.IsMatch(text, @"\b(devops|sre|site reliability|cloud|aws|azure|kubernetes|terraform|infrastructure)\b", RegexOptions.IgnoreCase)) return "DevOps / Infrastructure";
            if (Regex.IsMatch(text, @"\b(qa|quality assurance|test engineer|automation tester|sdet)\b", RegexOptions.IgnoreCase)) return "Quality Assurance";
            if (Regex.IsMatch(text, @"\b(network|systems administrator|system administrator|desktop support|help desk|it support|technician)\b", RegexOptions.IgnoreCase)) return "IT Support / Systems";
            if (Regex.IsMatch(text, @"\b(scrum|project manager|program manager|product manager|business analyst)\b", RegexOptions.IgnoreCase)) return "Product / Project Management";
            if (Regex.IsMatch(text, @"\b(software|developer|engineer|programmer|full stack|frontend|backend|java|python|\.net|c#)\b", RegexOptions.IgnoreCase)) return "Software Engineering";

            return "";
        }

        private static string LocationFromTitle(string title)
        {
            var match = Regex.Match(title ?? "", @"\((?<loc>[^)]*(?:remote|united states|usa|[A-Z]{2})[^)]*)\)$", RegexOptions.IgnoreCase);
            return match.Success ? CleanText(match.Groups["loc"].Value) : "";
        }

        private static string JobIdFromUrl(string url)
        {
            var match = Regex.Match(url ?? "", @"/jobs/(?<id>\d+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["id"].Value : url;
        }

        private static bool IsRemote(params string[] values)
        {
            var text = string.Join(" ", values.Where(v => !string.IsNullOrWhiteSpace(v))).ToLowerInvariant();
            return text.Contains("remote") ||
                   text.Contains("telecommute") ||
                   text.Contains("work from home") ||
                   text.Contains("virtual");
        }

        private static DateTime? ParseDate(string value)
        {
            return DateTime.TryParse(value, out var parsed) ? parsed : null;
        }

        private static string GetJsonString(Dictionary<string, JsonElement> data, string key)
        {
            if (!data.TryGetValue(key, out var value)) return "";
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
        }

        private static string GetPropertyString(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value)) return "";
            if (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined) return "";
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
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
                var key = !string.IsNullOrWhiteSpace(job.ExternalId)
                    ? job.ExternalId.Trim()
                    : !string.IsNullOrWhiteSpace(job.JobUrl)
                        ? job.JobUrl.Trim()
                        : $"{job.Company}|{job.Title}|{job.Location}";
                if (seen.Add(key)) results.Add(job);
            }

            return results;
        }

        private sealed class PinpointJobSummary
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Company { get; set; }
            public string Location { get; set; }
            public bool IsRemote { get; set; }
            public string JobUrl { get; set; }
            public DateTime? DatePosted { get; set; }
            public string Category { get; set; }
        }
    }
}
