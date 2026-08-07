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
    internal class JobsoidScraper
    {
        private const int SourceId = 91;
        private const int Workers = 6;

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
                { "Accept", "application/json, text/plain;q=0.9, */*;q=0.8" },
                { "Accept-Language", "en-US,en;q=0.9" },
                { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" }
            }
        };

        public async Task<List<ScrapedJob>> FetchJobsAsync(string inputCsv = null, int? limitSites = null, int maxJobsPerSite = 0)
        {
            inputCsv ??= System.IO.Path.Combine(Environment.CurrentDirectory, "outputs", "jobsoid_jobs", "jobsoid_api_urls_latest.csv");

            var rows = AtsCsv.ReadRows(inputCsv)
                .Where(r => !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "api_url")) ||
                            !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "domain")))
                .OrderByDescending(JobCount)
                .ToList();

            if (limitSites.HasValue && limitSites.Value > 0) rows = rows.Take(limitSites.Value).ToList();

            Console.WriteLine($"[Jobsoid] Loaded {rows.Count} URL rows from {inputCsv}");

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
            var domain = FirstNonEmpty(AtsCsv.Get(row, "domain"), DomainFromUrl(AtsCsv.Get(row, "api_url")));
            var apiUrl = FirstNonEmpty(AtsCsv.Get(row, "api_url"), $"https://{domain}/api/v1/jobs");
            if (string.IsNullOrWhiteSpace(domain)) return new List<ScrapedJob>();

            try
            {
                var json = await _http.GetStringAsync(apiUrl);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return new List<ScrapedJob>();

                var items = doc.RootElement.EnumerateArray().ToList();
                if (maxJobsPerSite > 0) items = items.Take(maxJobsPerSite).ToList();

                var jobs = items
                    .Select(item => MapJob(domain, item))
                    .Where(job => job != null)
                    .ToList();

                Console.WriteLine($"[Jobsoid] {domain} listed={items.Count} jobs={jobs.Count}");
                return jobs;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Jobsoid] {domain} error: {ex.Message}");
                return new List<ScrapedJob>();
            }
        }

        private static ScrapedJob MapJob(string domain, JsonElement item)
        {
            var id = GetString(item, "id");
            var title = CleanText(GetString(item, "title"));
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title)) return null;

            var location = LocationFromElement(item);
            var country = LocationCountry(item);
            var description = StripTags(GetString(item, "description"));
            var jobType = CleanText(GetString(item, "type"));
            var remote = IsRemote(jobType, title, description, country);

            if (!UsLocationFilter.IsUs(location) && !remote) return null;

            return new ScrapedJob
            {
                SourceId = SourceId,
                ExternalId = $"{SourceId}:{id}",
                Title = title,
                Company = FirstNonEmpty(CleanText(GetString(item, "company")), CompanyFromDomain(domain)),
                Location = string.IsNullOrWhiteSpace(location) && remote ? "Remote" : location,
                Description = description,
                JobUrl = FirstNonEmpty(GetString(item, "hostedUrl"), $"https://{domain}/j/{id}"),
                IsRemote = remote,
                DatePosted = ParseDate(GetString(item, "postedDate")),
                JobType = jobType,
                Category = CategoryFromElement(item)
            };
        }

        private static string LocationFromElement(JsonElement item)
        {
            if (!item.TryGetProperty("location", out var location) || location.ValueKind != JsonValueKind.Object) return "";

            var title = CleanText(GetString(location, "title"));
            var city = CleanText(GetString(location, "city"));
            var state = CleanText(GetString(location, "state"));
            var country = CleanText(GetString(location, "country"));

            if (!string.IsNullOrWhiteSpace(title) && !title.Contains(" - "))
            {
                return title;
            }

            return string.Join(", ", new[] { city, state, country }.Where(v => !string.IsNullOrWhiteSpace(v)));
        }

        private static string LocationCountry(JsonElement item)
        {
            return item.TryGetProperty("location", out var location) && location.ValueKind == JsonValueKind.Object
                ? CleanText(GetString(location, "country"))
                : "";
        }

        private static string CategoryFromElement(JsonElement item)
        {
            if (item.TryGetProperty("function", out var function) && function.ValueKind == JsonValueKind.Object)
            {
                var title = CleanText(GetString(function, "title"));
                if (!string.IsNullOrWhiteSpace(title)) return title;
            }

            if (item.TryGetProperty("department", out var department) && department.ValueKind == JsonValueKind.Object)
            {
                var title = CleanText(GetString(department, "title"));
                if (!string.IsNullOrWhiteSpace(title)) return title;
            }

            return CleanText(GetString(item, "industry"));
        }

        private static bool IsRemote(string jobType, string title, string description, string country)
        {
            var text = $"{jobType} {title} {description}".ToLowerInvariant();
            if (!Regex.IsMatch(text, @"\b(remote|telecommute|work from home|virtual)\b", RegexOptions.IgnoreCase)) return false;
            if (string.IsNullOrWhiteSpace(country)) return true;

            return country.Equals("United States", StringComparison.OrdinalIgnoreCase) ||
                   country.Equals("USA", StringComparison.OrdinalIgnoreCase) ||
                   country.Equals("US", StringComparison.OrdinalIgnoreCase);
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
    }
}
