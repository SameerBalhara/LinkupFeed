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
    internal class OracleCloudScraper
    {
        private const int SourceId = 95;
        private const int Workers = 4;
        private const int DetailWorkers = 6;
        private const int PageSize = 100;

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
            inputCsv ??= System.IO.Path.Combine(Environment.CurrentDirectory, "outputs", "oraclecloud_jobs", "oraclecloud_requisition_urls_latest.csv");

            var rows = AtsCsv.ReadRows(inputCsv)
                .Where(r => !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "domain")) &&
                            !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "site")))
                .OrderByDescending(JobCount)
                .ToList();

            if (limitSites.HasValue && limitSites.Value > 0) rows = rows.Take(limitSites.Value).ToList();

            Console.WriteLine($"[OracleCloud] Loaded {rows.Count} URL rows from {inputCsv}");

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
            return int.TryParse(AtsCsv.Get(row, "total_jobs"), out var count) ||
                   int.TryParse(AtsCsv.Get(row, "job_links_found"), out count) ||
                   int.TryParse(AtsCsv.Get(row, "job_count"), out count)
                ? count
                : 0;
        }

        private static async Task<List<ScrapedJob>> FetchSiteAsync(Dictionary<string, string> row, int maxJobsPerSite)
        {
            var domain = AtsCsv.Get(row, "domain");
            var site = AtsCsv.Get(row, "site");
            if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(site)) return new List<ScrapedJob>();

            try
            {
                var summaries = new List<OracleCloudJobSummary>();
                var total = 0;

                for (var offset = 0; ; offset += PageSize)
                {
                    var url = ListApiUrl(domain, site, PageSize, offset);
                    var json = await GetStringWithRetryAsync(url);
                    var page = ParsePage(domain, site, json, out total);
                    if (page.Count == 0) break;

                    summaries.AddRange(page.Where(s => UsLocationFilter.IsUs(s.Location) || s.IsRemote));

                    if (maxJobsPerSite > 0 && summaries.Count >= maxJobsPerSite)
                    {
                        summaries = summaries.Take(maxJobsPerSite).ToList();
                        break;
                    }

                    if (total > 0 && offset + PageSize >= total) break;
                }

                var jobs = await FetchDetailsAsync(summaries);
                Console.WriteLine($"[OracleCloud] {domain}/{site} listed={total} candidates={summaries.Count} jobs={jobs.Count}");
                return jobs;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OracleCloud] {domain}/{site} error: {ex.Message}");
                return new List<ScrapedJob>();
            }
        }

        private static List<OracleCloudJobSummary> ParsePage(string domain, string site, string json, out int total)
        {
            total = 0;
            var summaries = new List<OracleCloudJobSummary>();

            using var doc = JsonDocument.Parse(json);
            if (!TryGetSearch(doc.RootElement, out var search)) return summaries;

            total = TryGetInt(search, "TotalJobsCount");
            var company = FirstNonEmpty(FirstFacetName(search, "organizationsFacet"), CompanyFromDomain(domain));
            var categoryById = FacetMap(search, "categoriesFacet");

            if (!search.TryGetProperty("requisitionList", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return summaries;
            }

            foreach (var item in list.EnumerateArray())
            {
                var id = CleanText(GetString(item, "Id"));
                var title = CleanText(GetString(item, "Title"));
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title)) continue;

                var category = FirstNonEmpty(
                    CleanText(GetString(item, "Category")),
                    ValueById(categoryById, GetString(item, "JobFamilyId")),
                    CleanText(GetString(item, "JobFunction")),
                    CleanText(GetString(item, "JobFamily")));
                var workplace = CleanText(GetString(item, "WorkplaceType"));
                var location = FirstNonEmpty(CleanText(GetString(item, "PrimaryLocation")), workplace);
                var remote = IsRemote(workplace, GetString(item, "WorkplaceTypeCode"), title, location);

                summaries.Add(new OracleCloudJobSummary
                {
                    Domain = domain,
                    Site = site,
                    Id = id,
                    Title = title,
                    Company = company,
                    Location = string.IsNullOrWhiteSpace(location) && remote ? "Remote" : location,
                    IsRemote = remote,
                    JobUrl = PublicJobUrl(domain, site, id),
                    DatePosted = ParseDate(GetString(item, "PostedDate")),
                    JobType = FirstNonEmpty(GetString(item, "JobSchedule"), GetString(item, "JobType"), GetString(item, "WorkerType"), GetString(item, "ContractType")),
                    Category = category,
                    ShortDescription = StripTags(GetString(item, "ShortDescriptionStr"))
                });
            }

            return summaries;
        }

        private static async Task<List<ScrapedJob>> FetchDetailsAsync(List<OracleCloudJobSummary> summaries)
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

        private static async Task<ScrapedJob> FetchDetailAsync(OracleCloudJobSummary summary)
        {
            try
            {
                var json = await GetStringWithRetryAsync(DetailApiUrl(summary.Domain, summary.Site, summary.Id));
                using var doc = JsonDocument.Parse(json);
                if (!TryGetFirstItem(doc.RootElement, out var detail))
                {
                    return MapJob(summary, summary.ShortDescription);
                }

                var description = StripTags(string.Join(" ", new[]
                {
                    GetString(detail, "ExternalDescriptionStr"),
                    GetString(detail, "ExternalResponsibilitiesStr"),
                    GetString(detail, "ExternalQualificationsStr"),
                    GetString(detail, "CorporateDescriptionStr")
                }.Where(v => !string.IsNullOrWhiteSpace(v))));

                var location = FirstNonEmpty(
                    LocationFromDetail(detail),
                    summary.Location);
                var workplace = FirstNonEmpty(GetString(detail, "WorkplaceType"), GetString(detail, "WorkplaceTypeCode"));
                var remote = summary.IsRemote || IsRemote(workplace, location, description);
                var company = FirstNonEmpty(
                    CleanText(GetString(detail, "Organization")),
                    CleanText(GetString(detail, "LegalEmployer")),
                    CleanText(GetString(detail, "BusinessUnit")),
                    summary.Company);
                var category = FirstNonEmpty(
                    CleanText(GetString(detail, "Category")),
                    CleanText(GetString(detail, "JobFunction")),
                    summary.Category);
                var jobType = FirstNonEmpty(
                    CleanText(GetString(detail, "JobSchedule")),
                    CleanText(GetString(detail, "JobType")),
                    CleanText(GetString(detail, "WorkerType")),
                    CleanText(GetString(detail, "ContractType")),
                    summary.JobType);
                var posted = ParseDate(GetString(detail, "ExternalPostedStartDate")) ?? summary.DatePosted;

                return MapJob(summary, FirstNonEmpty(description, summary.ShortDescription), category, posted, jobType, location, company, remote);
            }
            catch
            {
                return MapJob(summary, summary.ShortDescription);
            }
        }

        private static ScrapedJob MapJob(
            OracleCloudJobSummary summary,
            string description,
            string category = null,
            DateTime? datePosted = null,
            string jobType = null,
            string location = null,
            string company = null,
            bool? isRemote = null)
        {
            var finalLocation = FirstNonEmpty(location, summary.Location);
            var remote = isRemote ?? summary.IsRemote;

            if (!UsLocationFilter.IsUs(finalLocation) && !remote) return null;

            return new ScrapedJob
            {
                SourceId = SourceId,
                ExternalId = OracleCloudReferenceId(summary),
                Title = summary.Title,
                Company = FirstNonEmpty(company, summary.Company),
                Location = string.IsNullOrWhiteSpace(finalLocation) && remote ? "Remote" : finalLocation,
                Description = description ?? "",
                JobUrl = summary.JobUrl,
                IsRemote = remote,
                DatePosted = datePosted ?? summary.DatePosted,
                JobType = FirstNonEmpty(jobType, summary.JobType),
                Category = FirstNonEmpty(category, summary.Category)
            };
        }

        private static string OracleCloudReferenceId(OracleCloudJobSummary summary)
        {
            var key = string.Join("|", new[]
            {
                summary.Domain,
                summary.Site,
                summary.Id
            }).ToLowerInvariant();

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            return $"{SourceId}:h{Convert.ToHexString(bytes, 0, 8).ToLowerInvariant()}";
        }

        private static async Task<string> GetStringWithRetryAsync(string url)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0) await Task.Delay(TimeSpan.FromSeconds(2 * attempt));

                using var response = await _http.GetAsync(url);
                var text = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode) return text;

                if ((response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429 || response.StatusCode == HttpStatusCode.RequestTimeout) && attempt < 2)
                {
                    continue;
                }

                response.EnsureSuccessStatusCode();
            }

            return "";
        }

        private static string ListApiUrl(string domain, string site, int limit, int offset)
        {
            return $"https://{domain}/hcmRestApi/resources/latest/recruitingCEJobRequisitions?onlyData=true&finder=findReqs;siteNumber={site},limit={limit},offset={offset},sortBy=POSTING_DATES_DESC&expand=requisitionList";
        }

        private static string DetailApiUrl(string domain, string site, string id)
        {
            return $"https://{domain}/hcmRestApi/resources/latest/recruitingCEJobRequisitionDetails?onlyData=true&finder=ById;Id={Uri.EscapeDataString(id)},siteNumber={Uri.EscapeDataString(site)}&expand=all";
        }

        private static string PublicJobUrl(string domain, string site, string id)
        {
            return $"https://{domain}/hcmUI/CandidateExperience/en/sites/{site}/job/{id}";
        }

        private static bool TryGetSearch(JsonElement root, out JsonElement search)
        {
            search = default;
            return TryGetFirstItem(root, out search);
        }

        private static bool TryGetFirstItem(JsonElement root, out JsonElement item)
        {
            item = default;
            if (!root.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array ||
                items.GetArrayLength() == 0)
            {
                return false;
            }

            item = items[0];
            return true;
        }

        private static Dictionary<string, string> FacetMap(JsonElement search, string property)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!search.TryGetProperty(property, out var facets) || facets.ValueKind != JsonValueKind.Array) return map;

            foreach (var facet in facets.EnumerateArray())
            {
                var id = GetString(facet, "Id");
                var name = CleanText(GetString(facet, "Name"));
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name)) map[id] = name;
            }

            return map;
        }

        private static string FirstFacetName(JsonElement search, string property)
        {
            if (!search.TryGetProperty(property, out var facets) ||
                facets.ValueKind != JsonValueKind.Array ||
                facets.GetArrayLength() == 0)
            {
                return "";
            }

            return CleanText(GetString(facets[0], "Name"));
        }

        private static string ValueById(Dictionary<string, string> map, string id)
        {
            return !string.IsNullOrWhiteSpace(id) && map.TryGetValue(id, out var value) ? value : "";
        }

        private static string LocationFromDetail(JsonElement detail)
        {
            var parts = new List<string>();
            var primary = CleanText(GetString(detail, "PrimaryLocation"));
            if (!string.IsNullOrWhiteSpace(primary)) parts.Add(primary);

            foreach (var childName in new[] { "secondaryLocations", "otherWorkLocations", "workLocation" })
            {
                if (!detail.TryGetProperty(childName, out var child) || child.ValueKind != JsonValueKind.Array) continue;
                foreach (var item in child.EnumerateArray())
                {
                    var text = FirstNonEmpty(
                        CleanText(GetString(item, "Name")),
                        CleanText(GetString(item, "LocationName")),
                        CleanText(GetString(item, "PrimaryLocation")),
                        CleanText(GetString(item, "WorkLocation")));
                    if (!string.IsNullOrWhiteSpace(text)) parts.Add(text);
                }
            }

            return string.Join(" | ", parts.Distinct(StringComparer.OrdinalIgnoreCase).Take(6));
        }

        private static bool IsRemote(params string[] values)
        {
            var text = string.Join(" ", values.Where(v => !string.IsNullOrWhiteSpace(v))).ToLowerInvariant();
            return text.Contains("remote") ||
                   text.Contains("telecommute") ||
                   text.Contains("work from home") ||
                   text.Contains("virtual") ||
                   text.Contains("ora_remote");
        }

        private static int TryGetInt(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value)) return 0;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
            return int.TryParse(value.ToString(), out var parsed) ? parsed : 0;
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

        private static List<ScrapedJob> Dedupe(IEnumerable<ScrapedJob> jobs)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<ScrapedJob>();
            foreach (var job in jobs)
            {
                if (job == null) continue;
                var key = !string.IsNullOrWhiteSpace(job.ExternalId)
                    ? job.ExternalId.Trim()
                    : !string.IsNullOrWhiteSpace(job.JobUrl)
                        ? job.JobUrl.Trim()
                        : $"{job.Company}|{job.Title}|{job.Location}";
                if (seen.Add(key)) results.Add(job);
            }

            return results;
        }

        private sealed class OracleCloudJobSummary
        {
            public string Domain { get; set; }
            public string Site { get; set; }
            public string Id { get; set; }
            public string Title { get; set; }
            public string Company { get; set; }
            public string Location { get; set; }
            public bool IsRemote { get; set; }
            public string JobUrl { get; set; }
            public DateTime? DatePosted { get; set; }
            public string JobType { get; set; }
            public string Category { get; set; }
            public string ShortDescription { get; set; }
        }
    }
}
