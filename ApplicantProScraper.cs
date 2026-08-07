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
    internal class ApplicantProScraper
    {
        private const int SourceId = 97;
        private const int Workers = 5;
        private const int DetailWorkers = 3;

        private static readonly Regex DomainIdPattern = new Regex(@"""domain_id""\s*:\s*""(?<id>\d+)""|domainId\s*:\s*(?<id2>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
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
                { "Accept", "application/json, text/html;q=0.9, */*;q=0.8" },
                { "Accept-Language", "en-US,en;q=0.9" },
                { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" }
            }
        };

        public async Task<List<ScrapedJob>> FetchJobsAsync(string inputCsv = null, int? limitSites = null, int maxJobsPerSite = 0)
        {
            inputCsv ??= System.IO.Path.Combine(Environment.CurrentDirectory, "outputs", "applicantpro_jobs", "applicantpro_api_urls_latest.csv");

            var rows = AtsCsv.ReadRows(inputCsv)
                .Where(r => !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "domain")) ||
                            !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "list_url")))
                .OrderByDescending(JobCount)
                .ToList();

            if (limitSites.HasValue && limitSites.Value > 0) rows = rows.Take(limitSites.Value).ToList();

            Console.WriteLine($"[ApplicantPro] Loaded {rows.Count} URL rows from {inputCsv}");

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
            var domain = FirstNonEmpty(AtsCsv.Get(row, "domain"), DomainFromUrl(AtsCsv.Get(row, "list_url")));
            var siteId = FirstNonEmpty(AtsCsv.Get(row, "site_id"), AtsCsv.Get(row, "domain_id"));
            if (string.IsNullOrWhiteSpace(domain)) return new List<ScrapedJob>();

            try
            {
                if (string.IsNullOrWhiteSpace(siteId))
                {
                    siteId = await DiscoverSiteIdAsync(domain);
                }

                if (string.IsNullOrWhiteSpace(siteId))
                {
                    Console.WriteLine($"[ApplicantPro] {domain} missing site id");
                    return new List<ScrapedJob>();
                }

                var listUrl = ApplicantProListUrl(domain, siteId);
                using var response = await GetWithRefererAsync(listUrl, $"https://{domain}/jobs/");
                var text = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return new List<ScrapedJob>();

                using var doc = JsonDocument.Parse(text);
                if (!doc.RootElement.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("jobs", out var jobs) ||
                    jobs.ValueKind != JsonValueKind.Array)
                {
                    return new List<ScrapedJob>();
                }

                var summaries = jobs.EnumerateArray().Select(j => ParseSummary(domain, siteId, j)).Where(j => j != null).ToList();
                if (maxJobsPerSite > 0) summaries = summaries.Take(maxJobsPerSite).ToList();

                var details = await FetchDetailsAsync(summaries);
                Console.WriteLine($"[ApplicantPro] {domain} candidates={summaries.Count} jobs={details.Count}");
                return details;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ApplicantPro] {domain} error: {ex.Message}");
                return new List<ScrapedJob>();
            }
        }

        private static ApplicantProJobSummary ParseSummary(string domain, string siteId, JsonElement item)
        {
            var id = GetJsonString(item, "id");
            var title = CleanText(GetJsonString(item, "title"));
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title)) return null;

            var location = FirstNonEmpty(
                CleanText(GetJsonString(item, "jobLocation")),
                JoinLocation(GetJsonString(item, "city"), GetJsonString(item, "abbreviation"), GetJsonString(item, "iso3")));

            return new ApplicantProJobSummary
            {
                Id = id,
                SiteId = siteId,
                Domain = domain,
                Title = title,
                Company = CompanyFromDomain(domain),
                Location = location,
                JobUrl = FirstNonEmpty(GetJsonString(item, "jobUrl"), $"https://{domain}/jobs/{id}"),
                DatePosted = ParseDate(GetJsonString(item, "startDateRef")),
                JobType = CleanText(GetJsonString(item, "employmentType")),
                Category = FirstNonEmpty(GetJsonString(item, "jobCategory"), GetJsonString(item, "customCategory"), GetJsonString(item, "classification"), GetJsonString(item, "orgTitle")),
                IsRemote = IsRemote(GetJsonString(item, "workplaceType"), location, title)
            };
        }

        private static async Task<List<ScrapedJob>> FetchDetailsAsync(List<ApplicantProJobSummary> summaries)
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

        private static async Task<ScrapedJob> FetchDetailAsync(ApplicantProJobSummary summary)
        {
            try
            {
                var detailUrl = $"https://{summary.Domain}/core/jobs/{summary.SiteId}/{summary.Id}/job-details";
                using var response = await GetWithRefererAsync(detailUrl, summary.JobUrl);
                var text = await response.Content.ReadAsStringAsync();

                JsonElement data = default;
                var hasData = false;
                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(text);
                    hasData = doc.RootElement.TryGetProperty("data", out data);
                    if (hasData) data = data.Clone();
                }

                var title = hasData ? FirstNonEmpty(GetJsonString(data, "title"), summary.Title) : summary.Title;
                var location = hasData
                    ? FirstNonEmpty(GetJsonString(data, "jobLocation"), JoinLocation(GetJsonString(data, "city"), GetJsonString(data, "abbreviation"), GetJsonString(data, "iso3")), summary.Location)
                    : summary.Location;
                var description = hasData
                    ? StripTags(FirstNonEmpty(GetJsonString(data, "description"), GetJsonString(data, "advertisingDescriptionHtml"), GetJsonString(data, "advertisingDescription")))
                    : "";
                var jobType = hasData ? FirstNonEmpty(GetJsonString(data, "employmentType"), summary.JobType) : summary.JobType;
                var category = hasData
                    ? FirstNonEmpty(GetJsonString(data, "jobCategory"), GetJsonString(data, "customCategory"), GetJsonString(data, "classification"), GetJsonString(data, "orgTitle"), summary.Category, InferCategory(title, description))
                    : FirstNonEmpty(summary.Category, InferCategory(summary.Title, ""));
                var datePosted = hasData ? ParseDate(GetJsonString(data, "startDateRef")) ?? summary.DatePosted : summary.DatePosted;
                var remote = summary.IsRemote || (hasData && IsRemote(GetJsonString(data, "workplaceType"), location, title, description));

                if (string.IsNullOrWhiteSpace(title)) return null;
                if (!UsLocationFilter.IsUs(location) && !remote) return null;

                return new ScrapedJob
                {
                    SourceId = SourceId,
                    ExternalId = TenantScopedReferenceId(summary.Domain, summary.Id),
                    Title = CleanText(title),
                    Company = summary.Company,
                    Location = string.IsNullOrWhiteSpace(location) && remote ? "Remote" : CleanText(location),
                    Description = description,
                    JobUrl = summary.JobUrl,
                    IsRemote = remote,
                    DatePosted = datePosted,
                    JobType = CleanText(jobType),
                    Category = CleanText(category)
                };
            }
            catch
            {
                return null;
            }
        }


        private static string TenantScopedReferenceId(string domain, string id)
        {
            var tenant = NormalizeReferenceTenant(domain);
            var raw = string.IsNullOrWhiteSpace(id) ? "" : id.Trim();
            var fingerprint = $"applicantpro|{tenant}|{raw}".ToLowerInvariant();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint));
            return $"{SourceId}:h{Convert.ToHexString(hash, 0, 8).ToLowerInvariant()}";
        }

        private static string NormalizeReferenceTenant(string domain)
        {
            var value = (domain ?? "").Trim().ToLowerInvariant();
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri)) value = uri.Host;
            value = value.Split('/')[0].Trim();
            return value.StartsWith("www.") ? value.Substring(4) : value;
        }        internal static async Task<string> DiscoverSiteIdAsync(string domain)
        {
            try
            {
                var html = await _http.GetStringAsync($"https://{domain}/jobs/");
                var match = DomainIdPattern.Match(html);
                if (match.Success)
                {
                    return FirstNonEmpty(match.Groups["id"].Value, match.Groups["id2"].Value);
                }
            }
            catch { }

            return "";
        }

        internal static string ApplicantProListUrl(string domain, string siteId)
        {
            var getParams = Uri.EscapeDataString("{\"isInternal\":0,\"showLocation\":1,\"showEmploymentType\":1,\"showWorkplaceType\":1,\"chatToApplyButton\":\"0\"}");
            return $"https://{domain}/core/jobs/{siteId}?getParams={getParams}";
        }

        private static async Task<HttpResponseMessage> GetWithRefererAsync(string url, string referer)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Referer", referer);
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/html;q=0.9, */*;q=0.8");
            return await _http.SendAsync(request);
        }

        private static string GetJsonString(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value)) return "";
            if (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined) return "";
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
        }

        private static string JoinLocation(params string[] parts)
        {
            return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(CleanText));
        }

        private static bool IsRemote(params string[] values)
        {
            var text = string.Join(" ", values.Where(v => !string.IsNullOrWhiteSpace(v))).ToLowerInvariant();
            return text.Contains("remote") ||
                   text.Contains("work from home") ||
                   text.Contains("work from home flexibility") ||
                   text.Contains("telecommute") ||
                   text.Contains("virtual");
        }

        private static DateTime? ParseDate(string value)
        {
            return DateTime.TryParse(value, out var parsed) ? parsed : null;
        }

        private static string InferCategory(string title, string description)
        {
            return JobCategoryMapper.Normalize("", title, description);
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

        private sealed class ApplicantProJobSummary
        {
            public string Id { get; set; }
            public string SiteId { get; set; }
            public string Domain { get; set; }
            public string Title { get; set; }
            public string Company { get; set; }
            public string Location { get; set; }
            public string JobUrl { get; set; }
            public DateTime? DatePosted { get; set; }
            public string JobType { get; set; }
            public string Category { get; set; }
            public bool IsRemote { get; set; }
        }
    }
}
