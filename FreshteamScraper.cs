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
    internal class FreshteamScraper
    {
        private const int SourceId = 92;
        private const int Workers = 6;
        private const int DetailWorkers = 4;

        private static readonly Regex JobCardPattern = new Regex("<a\\s+(?<attrs>[^>]*href=[\"']/jobs/(?<job_id>[^\"'/]+)/(?<slug>[^\"']+)[\"'][^>]*)>(?<body>[\\s\\S]*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
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

        public async Task<List<ScrapedJob>> FetchJobsAsync(string inputCsv = null, int? limitSites = null, int maxJobsPerSite = 0)
        {
            inputCsv ??= System.IO.Path.Combine(Environment.CurrentDirectory, "outputs", "freshteam_jobs", "freshteam_link_counts_latest.csv");

            var rows = AtsCsv.ReadRows(inputCsv)
                .Where(r => !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "list_url")) ||
                            !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "domain")))
                .OrderByDescending(JobCount)
                .ToList();

            if (limitSites.HasValue && limitSites.Value > 0) rows = rows.Take(limitSites.Value).ToList();

            Console.WriteLine($"[Freshteam] Loaded {rows.Count} URL rows from {inputCsv}");

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
            var domain = FirstNonEmpty(AtsCsv.Get(row, "domain"), DomainFromUrl(AtsCsv.Get(row, "list_url")));
            var listUrl = FirstNonEmpty(AtsCsv.Get(row, "list_url"), $"https://{domain}/jobs");
            if (string.IsNullOrWhiteSpace(domain)) return new List<ScrapedJob>();

            try
            {
                var html = await _http.GetStringAsync(listUrl);
                var jobTypes = JobTypeMap(html);
                var summaries = ParseList(domain, html, jobTypes);
                if (maxJobsPerSite > 0) summaries = summaries.Take(maxJobsPerSite).ToList();

                var jobs = await FetchDetailsAsync(summaries);
                Console.WriteLine($"[Freshteam] {domain} listed={summaries.Count} jobs={jobs.Count}");
                return jobs;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Freshteam] {domain} error: {ex.Message}");
                return new List<ScrapedJob>();
            }
        }

        private static List<FreshteamJobSummary> ParseList(string domain, string html, Dictionary<string, string> jobTypes)
        {
            var summaries = new List<FreshteamJobSummary>();
            foreach (Match match in JobCardPattern.Matches(html ?? ""))
            {
                var attrs = match.Groups["attrs"].Value;
                var body = match.Groups["body"].Value;
                var id = WebUtility.HtmlDecode(match.Groups["job_id"].Value).Trim();
                var path = Regex.Match(attrs, "href=[\"'](?<href>[^\"']+)[\"']", RegexOptions.IgnoreCase).Groups["href"].Value;
                var location = Attr(attrs, "data-portal-location");
                var typeId = Attr(attrs, "data-portal-job-type");
                var remote = string.Equals(Attr(attrs, "data-portal-remote-location"), "true", StringComparison.OrdinalIgnoreCase);
                var title = ExtractClassText(body, "job-title");
                var description = ExtractClassText(body, "job-desc");

                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title)) continue;

                summaries.Add(new FreshteamJobSummary
                {
                    Id = id,
                    Title = title,
                    Company = CompanyFromDomain(domain),
                    Location = location,
                    Description = description,
                    JobUrl = new Uri(new Uri($"https://{domain}"), WebUtility.HtmlDecode(path)).ToString(),
                    IsRemote = remote && !HasExplicitNonUsCountry(location),
                    JobType = jobTypes.TryGetValue(typeId, out var mappedType) ? mappedType : "",
                    Category = ""
                });
            }

            return summaries
                .Where(s => UsLocationFilter.IsUs(s.Location) || s.IsRemote)
                .ToList();
        }

        private static async Task<List<ScrapedJob>> FetchDetailsAsync(List<FreshteamJobSummary> summaries)
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

        private static async Task<ScrapedJob> FetchDetailAsync(FreshteamJobSummary summary)
        {
            try
            {
                var html = await _http.GetStringAsync(summary.JobUrl);
                var description = FirstNonEmpty(
                    StripTags(JsonStringField(html, "description")),
                    ExtractClassText(html, "job-desc"),
                    summary.Description);
                var datePosted = ParseDate(JsonStringField(html, "datePosted"));
                var jobType = FirstNonEmpty(JsonStringField(html, "employmentType"), summary.JobType);
                var company = FirstNonEmpty(JsonStringField(html, "hiringOrganization"), ExtractPostedByCompany(html), summary.Company);
                var title = CleanTitle(FirstNonEmpty(JsonStringField(html, "title"), ExtractMeta(html, "og:title"), summary.Title));
                var category = FirstNonEmpty(ExtractDescriptionLabel(description, "Department"), summary.Category, InferCategory(title, description));

                return MapJob(summary, title, company, description, jobType, category, datePosted);
            }
            catch
            {
                return MapJob(summary, summary.Title, summary.Company, summary.Description, summary.JobType, summary.Category, null);
            }
        }

        private static ScrapedJob MapJob(FreshteamJobSummary summary, string title, string company, string description, string jobType, string category, DateTime? datePosted)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;
            if (!UsLocationFilter.IsUs(summary.Location) && !summary.IsRemote) return null;

            return new ScrapedJob
            {
                SourceId = SourceId,
                ExternalId = $"{SourceId}:{summary.Id}",
                Title = title,
                Company = company,
                Location = string.IsNullOrWhiteSpace(summary.Location) && summary.IsRemote ? "Remote" : summary.Location,
                Description = description,
                JobUrl = summary.JobUrl,
                IsRemote = summary.IsRemote,
                DatePosted = datePosted,
                JobType = jobType,
                Category = category
            };
        }

        private static Dictionary<string, string> JobTypeMap(string html)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var match = Regex.Match(html ?? "", "<select[^>]+id=[\"']work_type_id[\"'][^>]*>(?<options>[\\s\\S]*?)</select>", RegexOptions.IgnoreCase);
            if (!match.Success) return map;

            foreach (Match option in Regex.Matches(match.Groups["options"].Value, "<option[^>]+value=[\"'](?<value>[^\"']+)[\"'][^>]*>(?<text>[\\s\\S]*?)</option>", RegexOptions.IgnoreCase))
            {
                map[WebUtility.HtmlDecode(option.Groups["value"].Value).Trim()] = StripTags(option.Groups["text"].Value);
            }

            return map;
        }

        private static string Attr(string attrs, string name)
        {
            var match = Regex.Match(attrs ?? "", $"{Regex.Escape(name)}=(?:[\"'](?<value>[^\"']*)[\"']|(?<value>[^\\s>]+))", RegexOptions.IgnoreCase);
            return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value).Trim() : "";
        }

        private static string ExtractClassText(string html, string className)
        {
            var match = Regex.Match(html ?? "", $"<[^>]+class=[\"'][^\"']*{Regex.Escape(className)}[^\"']*[\"'][^>]*>(?<value>[\\s\\S]*?)</[^>]+>", RegexOptions.IgnoreCase);
            return match.Success ? StripTags(match.Groups["value"].Value) : "";
        }

        private static string JsonStringField(string html, string key)
        {
            var match = Regex.Match(html ?? "", $"[\"']{Regex.Escape(key)}[\"']\\s*:\\s*[\"'](?<value>(?:\\\\.|(?![\"']).)*)[\"']", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success) return "";

            var raw = WebUtility.HtmlDecode(match.Groups["value"].Value);
            try
            {
                return JsonSerializer.Deserialize<string>($"\"{raw.Replace("\"", "\\\"")}\"") ?? "";
            }
            catch
            {
                return raw.Replace("\\n", " ").Replace("\\/", "/").Replace("\\\"", "\"").Trim();
            }
        }

        private static string ExtractMeta(string html, string name)
        {
            var patterns = new[]
            {
                $"<meta[^>]+property=[\"']{Regex.Escape(name)}[\"'][^>]+content=\\s*[\"'](?<value>[^\"']*)[\"']",
                $"<meta[^>]+name=[\"']{Regex.Escape(name)}[\"'][^>]+content=\\s*[\"'](?<value>[^\"']*)[\"']"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(html ?? "", pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (match.Success) return CleanText(match.Groups["value"].Value);
            }

            return "";
        }

        private static string ExtractPostedByCompany(string html)
        {
            var description = ExtractMeta(html, "og:description");
            var match = Regex.Match(description, @"Posted by\s*:\s*(?<company>[^|]+)", RegexOptions.IgnoreCase);
            return match.Success ? CleanText(match.Groups["company"].Value) : "";
        }

        private static string ExtractDescriptionLabel(string description, string label)
        {
            if (string.IsNullOrWhiteSpace(description)) return "";
            var match = Regex.Match(description, $@"\b{Regex.Escape(label)}:\s*(?<value>[^.\r\n]+?)(?=\s+[A-Z][A-Za-z ]{{2,}}:|$)", RegexOptions.IgnoreCase);
            return match.Success ? CleanText(match.Groups["value"].Value) : "";
        }

        private static string CleanTitle(string value)
        {
            value = CleanText(value);
            value = Regex.Replace(value, @"^Hiring for\s+", "", RegexOptions.IgnoreCase).Trim();
            return value;
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
            if (Regex.IsMatch(text, @"\b(software|developer|engineer|programmer|full stack|frontend|backend|java|python|\.net|c#)\b", RegexOptions.IgnoreCase)) return "Software Engineering";

            return "";
        }

        private static bool HasExplicitNonUsCountry(string value)
        {
            var text = (value ?? "").ToLowerInvariant();
            return new[] { "canada", "india", "united kingdom", "germany", "france", "australia", "singapore" }.Any(text.Contains);
        }

        private static DateTime? ParseDate(string value)
        {
            if (DateTime.TryParse(value, out var parsed)) return parsed;

            value = Regex.Replace(value ?? "", @"\s+UTC\s*$", "Z", RegexOptions.IgnoreCase);
            return DateTime.TryParse(value, out parsed) ? parsed : null;
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

        private sealed class FreshteamJobSummary
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Company { get; set; }
            public string Location { get; set; }
            public string Description { get; set; }
            public string JobUrl { get; set; }
            public bool IsRemote { get; set; }
            public string JobType { get; set; }
            public string Category { get; set; }
        }
    }
}
