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
    internal class BambooHrScraper
    {
        private const int SourceId = 96;
        private const int Workers = 8;
        private const int DetailWorkers = 6;

        private static readonly Regex HtmlStripPattern = new Regex(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex MultiSpacePattern = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly string[] ItTenantSignals =
        {
            "tech", "technology", "software", "system", "data", "analytics", "cloud", "cyber",
            "security", "digital", "dev", "solutions", "consult", "federal", "gov", "lab",
            "engineering", "network", "automation", "platform", "infrastructure"
        };

        private static readonly string[] NonItTenantSignals =
        {
            "food", "restaurant", "health", "medical", "care", "clinic", "dental",
            "construction", "electric", "plumbing", "roof", "retail", "school", "church",
            "realestate", "property", "hospitality", "hotel", "fitness", "therapy",
            "law", "legal", "farm", "manufacturing", "warehouse"
        };

        private static readonly HttpClient _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        })
        {
            Timeout = TimeSpan.FromSeconds(20),
            DefaultRequestHeaders =
            {
                { "Accept", "application/json, text/html;q=0.9, */*;q=0.8" },
                { "Accept-Language", "en-US,en;q=0.9" },
                { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" }
            }
        };

        public async Task<List<ScrapedJob>> FetchJobsAsync(string inputCsv = null, int? limitSites = null, int maxJobsPerSite = 0)
        {
            inputCsv ??= System.IO.Path.Combine(Environment.CurrentDirectory, "outputs", "bamboohr_jobs", "bamboohr_link_counts_latest.csv");

            var rows = AtsCsv.ReadRows(inputCsv)
                .Where(r => !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "domain")) ||
                            !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "list_url")))
                .OrderByDescending(TenantPriorityScore)
                .ThenByDescending(JobCount)
                .ToList();

            if (limitSites.HasValue && limitSites.Value > 0) rows = rows.Take(limitSites.Value).ToList();

            Console.WriteLine($"[BambooHR] Loaded {rows.Count} URL rows from {inputCsv}");

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
                   int.TryParse(AtsCsv.Get(row, "job_link_count"), out count) ||
                   int.TryParse(AtsCsv.Get(row, "total_jobs"), out count) ||
                   int.TryParse(AtsCsv.Get(row, "job_count"), out count)
                ? count
                : 0;
        }

        private static int TenantPriorityScore(Dictionary<string, string> row)
        {
            var domain = FirstNonEmpty(AtsCsv.Get(row, "domain"), DomainFromUrl(AtsCsv.Get(row, "list_url")));
            var tenant = (domain ?? "").Split('.').FirstOrDefault() ?? "";
            var compact = Regex.Replace(tenant.ToLowerInvariant(), @"[^a-z0-9]+", "");
            var score = 0;

            foreach (var signal in ItTenantSignals)
            {
                if (compact.Contains(signal)) score += 3;
            }

            foreach (var signal in NonItTenantSignals)
            {
                if (compact.Contains(signal)) score -= 4;
            }

            if (Regex.IsMatch(compact, @"(a3|jcifederal|govcio|caci|peraton|leidos|saic|octo|mindpoint|redhorse|arkatechture)", RegexOptions.IgnoreCase)) score += 5;
            if (Regex.IsMatch(compact, @"(weitz|food|reverehealth|kinsley|tricity|aceelectric)", RegexOptions.IgnoreCase)) score -= 5;

            return score;
        }

        private static async Task<List<ScrapedJob>> FetchSiteAsync(Dictionary<string, string> row, int maxJobsPerSite)
        {
            var domain = FirstNonEmpty(AtsCsv.Get(row, "domain"), DomainFromUrl(AtsCsv.Get(row, "list_url")));
            var listUrl = FirstNonEmpty(AtsCsv.Get(row, "list_url"), $"https://{domain}/careers/list");
            if (string.IsNullOrWhiteSpace(domain)) return new List<ScrapedJob>();

            try
            {
                var listJson = await _http.GetStringAsync(listUrl);
                var summaries = ParseList(listJson);
                if (maxJobsPerSite > 0) summaries = summaries.Take(maxJobsPerSite).ToList();

                var detailCandidates = summaries;

                var jobs = await FetchDetailsAsync(domain, detailCandidates);
                Console.WriteLine($"[BambooHR] {domain} listed={summaries.Count} details={detailCandidates.Count} jobs={jobs.Count}");
                return jobs;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BambooHR] {domain} error: {ex.Message}");
                return new List<ScrapedJob>();
            }
        }

        private static List<BambooHrJobSummary> ParseList(string json)
        {
            var summaries = new List<BambooHrJobSummary>();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array) return summaries;

            foreach (var item in result.EnumerateArray())
            {
                var id = GetString(item, "id");
                var title = GetString(item, "jobOpeningName");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title)) continue;

                summaries.Add(new BambooHrJobSummary
                {
                    Id = id,
                    Title = CleanText(title),
                    Category = CleanText(GetString(item, "departmentLabel")),
                    JobType = CleanText(GetString(item, "employmentStatusLabel")),
                    Location = LocationFromElement(item),
                    IsRemote = IsRemote(GetString(item, "isRemote"), GetString(item, "locationType"), title, LocationFromElement(item))
                });
            }

            return summaries;
        }

        private static async Task<List<ScrapedJob>> FetchDetailsAsync(string domain, List<BambooHrJobSummary> summaries)
        {
            var jobs = new List<ScrapedJob>();
            using var gate = new SemaphoreSlim(DetailWorkers);
            var tasks = summaries.Select(async summary =>
            {
                await gate.WaitAsync();
                try { return await FetchDetailAsync(domain, summary); }
                finally { gate.Release(); }
            }).ToList();

            foreach (var task in tasks)
            {
                var job = await task;
                if (job != null) jobs.Add(job);
            }

            return jobs;
        }

        private static async Task<ScrapedJob> FetchDetailAsync(string domain, BambooHrJobSummary summary)
        {
            try
            {
                var detailUrl = $"https://{domain}/careers/{summary.Id}/detail";
                using var response = await _http.GetAsync(detailUrl);
                if (!response.IsSuccessStatusCode) return null;

                var text = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(text);
                if (!doc.RootElement.TryGetProperty("result", out var result) ||
                    !result.TryGetProperty("jobOpening", out var job) ||
                    job.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var title = FirstNonEmpty(CleanText(GetString(job, "jobOpeningName")), summary.Title);
                var description = StripTags(GetString(job, "description"));
                var inferredCategory = ItJobFilter.IsIt(title, summary.Category) ? InferCategory(title, description) : "";
                var category = FirstNonEmpty(CleanText(GetString(job, "departmentLabel")), summary.Category, inferredCategory);
                var location = FirstNonEmpty(LocationFromElement(job), summary.Location);
                var jobUrl = FirstNonEmpty(GetString(job, "jobOpeningShareUrl"), $"https://{domain}/careers/{summary.Id}");
                var remote = summary.IsRemote || IsRemote(GetString(job, "isRemote"), GetString(job, "locationType"), title, location, description);

                if (string.IsNullOrWhiteSpace(title)) return null;
                if (!UsLocationFilter.IsUs(location) && !remote) return null;

                return new ScrapedJob
                {
                    SourceId = SourceId,
                    ExternalId = $"bamboohr:{domain.ToLowerInvariant()}:{summary.Id}",
                    Title = title,
                    Company = CompanyFromDomain(domain),
                    Location = string.IsNullOrWhiteSpace(location) && remote ? "Remote" : location,
                    Description = description,
                    JobUrl = jobUrl,
                    IsRemote = remote,
                    DatePosted = ParseDate(GetString(job, "datePosted")),
                    JobType = FirstNonEmpty(CleanText(GetString(job, "employmentStatusLabel")), summary.JobType),
                    Category = category
                };
            }
            catch
            {
                return null;
            }
        }

        private static string LocationFromElement(JsonElement item)
        {
            if (!item.TryGetProperty("location", out var location) || location.ValueKind != JsonValueKind.Object) return "";

            return string.Join(", ", new[]
            {
                GetString(location, "city"),
                GetString(location, "state"),
                GetString(location, "addressCountry")
            }.Where(v => !string.IsNullOrWhiteSpace(v)));
        }

        private static bool IsRemote(params string[] values)
        {
            var text = string.Join(" ", values.Where(v => !string.IsNullOrWhiteSpace(v))).ToLowerInvariant();
            return text.Contains("remote") ||
                   text.Contains("work from home") ||
                   text.Contains("virtual") ||
                   text.Contains("locationtype 3") ||
                   text.Contains("locationtype=3");
        }

        private static string InferCategory(string title, string description)
        {
            var text = $"{title} {description}";
            if (Regex.IsMatch(text, @"\b(cyber|security|soc|siem|iam)\b", RegexOptions.IgnoreCase)) return "Cybersecurity";
            if (Regex.IsMatch(text, @"\b(data scientist|machine learning|ml engineer|artificial intelligence|ai engineer|analytics|business intelligence)\b", RegexOptions.IgnoreCase)) return "Data / Analytics";
            if (Regex.IsMatch(text, @"\b(devops|sre|cloud|aws|azure|kubernetes|terraform|infrastructure)\b", RegexOptions.IgnoreCase)) return "DevOps / Infrastructure";
            if (Regex.IsMatch(text, @"\b(network|systems administrator|system administrator|desktop support|help desk|it support)\b", RegexOptions.IgnoreCase)) return "IT Support / Systems";
            if (Regex.IsMatch(text, @"\b(software|developer|engineer|programmer|full stack|frontend|backend|java|python|\.net|c#)\b", RegexOptions.IgnoreCase)) return "Software Engineering";

            return "Information Technology";
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

        private static string GetString(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value)) return "";
            if (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined) return "";
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
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

        private static List<ScrapedJob> Dedupe(IEnumerable<ScrapedJob> jobs)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<ScrapedJob>();
            foreach (var job in jobs)
            {
                var key = FirstNonEmpty(job.ExternalId, job.JobUrl, $"{job.Company}|{job.Title}|{job.Location}");
                if (seen.Add(key)) results.Add(job);
            }

            return results;
        }

        private sealed class BambooHrJobSummary
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Category { get; set; }
            public string JobType { get; set; }
            public string Location { get; set; }
            public bool IsRemote { get; set; }
        }
    }
}
