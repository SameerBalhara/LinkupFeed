using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace LinkupFeed
{
    internal static class AtsUrlDiscovery
    {
        private static readonly Regex WorkdayDomainPattern = new Regex(@"^(?<tenant>[^.]+)\.(?<server>wd\d+)\.myworkdayjobs\.com$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex IcimsDomainPattern = new Regex(@"(^|\.)icims\.com$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex JobLinkPattern = new Regex("href=[\"'](?<href>[^\"']*/jobs/(?<job_id>\\d+)(?:/[^\"']*)?/job[^\"']*)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex JazzHrDomainPattern = new Regex(@"(^|\.)applytojob\.com$|(^|\.)jazzhr\.com$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex JazzHrJobLinkPattern = new Regex("href=[\"'](?<href>[^\"']*/apply/(?<job_id>[A-Za-z0-9]+)(?:/[^\"']*)?)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BambooHrDomainPattern = new Regex(@"(^|\.)bamboohr\.com$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BreezyHrDomainPattern = new Regex(@"(^|\.)breezy\.hr$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex OracleCloudDomainPattern = new Regex(@"(^|\.)oraclecloud\.com$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PinpointHqDomainPattern = new Regex(@"(^|\.)pinpointhq\.com$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PersonioDomainPattern = new Regex(@"(^|\.)jobs\.personio\.(com|de)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FreshteamDomainPattern = new Regex(@"(^|\.)freshteam\.com$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex JobsoidDomainPattern = new Regex(@"(^|\.)jobsoid\.com$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ApplicantProDomainPattern = new Regex(@"(^|\.)applicantpro\.com$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CatsOneDomainPattern = new Regex(@"(^|\.)catsone\.com$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ZohoRecruitDomainPattern = new Regex(@"(^|\.)zohorecruit\.(com|in)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FreshteamJobLinkPattern = new Regex("href=[\"'](?<href>/jobs/(?<job_id>[^\"'/]+)/[^\"']*)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex OracleSiteNumberPattern = new Regex("data-sitenumber=[\"'](?<site>[^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] WorkdaySiteCandidates =
        {
            "External", "external", "Careers", "careers", "Career", "career", "Jobs", "jobs",
            "Search", "search", "Workday", "workday", "External_Career_Site",
            "ExternalCareerSite", "External_Careers", "ExternalCareers", "EXTERNAL_CAREERS",
            "Careers_External", "CareersExternal", "Career_Site", "career_site",
            "External_Candidate_Home", "Candidate_Home", "Global", "global", "CandidatePortal"
        };

        private static readonly HttpClient _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        })
        {
            Timeout = TimeSpan.FromSeconds(15),
            DefaultRequestHeaders =
            {
                { "Accept", "application/json, text/html;q=0.9, */*;q=0.8" },
                { "Accept-Language", "en-US,en;q=0.9" },
                { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" }
            }
        };

        public static async Task RefreshAsync(string inputCsv, string outputRoot, int? limitDomains, string onlySource = null)
        {
            inputCsv = string.IsNullOrWhiteSpace(inputCsv) ? Path.Combine("input", "JobSites.csv") : inputCsv;
            outputRoot = string.IsNullOrWhiteSpace(outputRoot) ? "outputs" : outputRoot;
            var runWorkday = ShouldRefreshSource(onlySource, "Workday");
            var runIcims = ShouldRefreshSource(onlySource, "iCIMS", "icims");
            var runJazzHr = ShouldRefreshSource(onlySource, "JazzHR", "jazz", "applytojob", "apply-to-job");
            var runBambooHr = ShouldRefreshSource(onlySource, "BambooHR", "bamboo");
            var runBreezyHr = ShouldRefreshSource(onlySource, "BreezyHR", "breezy");
            var runOracleCloud = ShouldRefreshSource(onlySource, "OracleCloud", "oracle", "oraclecloud");
            var runPinpointHq = ShouldRefreshSource(onlySource, "PinpointHQ", "pinpoint", "pinpointhq");
            var runPersonio = ShouldRefreshSource(onlySource, "Personio");
            var runFreshteam = ShouldRefreshSource(onlySource, "Freshteam", "fresh-team");
            var runJobsoid = ShouldRefreshSource(onlySource, "Jobsoid", "job-soid");
            var runApplicantPro = ShouldRefreshSource(onlySource, "ApplicantPro", "applicant-pro");
            var runCatsOne = ShouldRefreshSource(onlySource, "CATSOne", "catsone", "cats-one");
            var runZohoRecruit = ShouldRefreshSource(onlySource, "ZohoRecruit", "zoho", "zohorecruit", "zoho-recruit");

            var rows = AtsCsv.ReadRows(inputCsv);
            var domains = rows
                .Select(ReadDomain)
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Select(NormalizeDomain)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (limitDomains.HasValue && limitDomains.Value > 0) domains = domains.Take(limitDomains.Value).ToList();

            Console.WriteLine($"[URL Refresh] Loaded {domains.Count} domains from {inputCsv}");

            var workdayRows = new List<Dictionary<string, string>>();
            var icimsRows = new List<Dictionary<string, string>>();
            var jazzHrRows = new List<Dictionary<string, string>>();
            var bambooHrRows = new List<Dictionary<string, string>>();
            var breezyHrRows = new List<Dictionary<string, string>>();
            var oracleCloudRows = new List<Dictionary<string, string>>();
            var pinpointHqRows = new List<Dictionary<string, string>>();
            var personioRows = new List<Dictionary<string, string>>();
            var freshteamRows = new List<Dictionary<string, string>>();
            var jobsoidRows = new List<Dictionary<string, string>>();
            var applicantProRows = new List<Dictionary<string, string>>();
            var catsOneRows = new List<Dictionary<string, string>>();
            var zohoRecruitRows = new List<Dictionary<string, string>>();

            if (runOracleCloud)
            {
                var oracleInputs = domains
                    .Select((domain, index) => new { Domain = domain, SourceRow = index + 2 })
                    .Where(x => OracleCloudDomainPattern.IsMatch(x.Domain))
                    .ToList();

                Console.WriteLine($"[URL Refresh] Oracle Cloud candidate domains: {oracleInputs.Count}");
                using var oracleGate = new SemaphoreSlim(16);
                var oracleTasks = oracleInputs.Select(async input =>
                {
                    await oracleGate.WaitAsync();
                    try { return await DiscoverOracleCloudAsync(input.SourceRow, input.Domain); }
                    finally { oracleGate.Release(); }
                }).ToList();

                foreach (var task in oracleTasks)
                {
                    var result = await task;
                    if (result != null) oracleCloudRows.Add(result);
                }
            }

            if (runPinpointHq)
            {
                var pinpointInputs = domains
                    .Select((domain, index) => new { Domain = domain, SourceRow = index + 2 })
                    .Where(x => PinpointHqDomainPattern.IsMatch(x.Domain))
                    .ToList();

                Console.WriteLine($"[URL Refresh] PinpointHQ candidate domains: {pinpointInputs.Count}");
                using var pinpointGate = new SemaphoreSlim(16);
                var pinpointTasks = pinpointInputs.Select(async input =>
                {
                    await pinpointGate.WaitAsync();
                    try { return await DiscoverPinpointHqAsync(input.SourceRow, input.Domain); }
                    finally { pinpointGate.Release(); }
                }).ToList();

                foreach (var task in pinpointTasks)
                {
                    var result = await task;
                    if (result != null) pinpointHqRows.Add(result);
                }
            }

            if (runPersonio)
            {
                var personioInputs = domains
                    .Select((domain, index) => new { Domain = domain, SourceRow = index + 2 })
                    .Where(x => PersonioDomainPattern.IsMatch(x.Domain))
                    .ToList();

                Console.WriteLine($"[URL Refresh] Personio candidate domains: {personioInputs.Count}");
                using var personioGate = new SemaphoreSlim(12);
                var personioTasks = personioInputs.Select(async input =>
                {
                    await personioGate.WaitAsync();
                    try { return await DiscoverPersonioAsync(input.SourceRow, input.Domain); }
                    finally { personioGate.Release(); }
                }).ToList();

                foreach (var task in personioTasks)
                {
                    var result = await task;
                    if (result != null) personioRows.Add(result);
                }
            }

            if (runFreshteam)
            {
                var freshteamInputs = domains
                    .Select((domain, index) => new { Domain = domain, SourceRow = index + 2 })
                    .Where(x => FreshteamDomainPattern.IsMatch(x.Domain))
                    .ToList();

                Console.WriteLine($"[URL Refresh] Freshteam candidate domains: {freshteamInputs.Count}");
                using var freshteamGate = new SemaphoreSlim(12);
                var freshteamTasks = freshteamInputs.Select(async input =>
                {
                    await freshteamGate.WaitAsync();
                    try { return await DiscoverFreshteamAsync(input.SourceRow, input.Domain); }
                    finally { freshteamGate.Release(); }
                }).ToList();

                foreach (var task in freshteamTasks)
                {
                    var result = await task;
                    if (result != null) freshteamRows.Add(result);
                }
            }

            if (runJobsoid)
            {
                var jobsoidInputs = domains
                    .Select((domain, index) => new { Domain = domain, SourceRow = index + 2 })
                    .Where(x => JobsoidDomainPattern.IsMatch(x.Domain))
                    .ToList();

                Console.WriteLine($"[URL Refresh] Jobsoid candidate domains: {jobsoidInputs.Count}");
                using var jobsoidGate = new SemaphoreSlim(12);
                var jobsoidTasks = jobsoidInputs.Select(async input =>
                {
                    await jobsoidGate.WaitAsync();
                    try { return await DiscoverJobsoidAsync(input.SourceRow, input.Domain); }
                    finally { jobsoidGate.Release(); }
                }).ToList();

                foreach (var task in jobsoidTasks)
                {
                    var result = await task;
                    if (result != null) jobsoidRows.Add(result);
                }
            }

            if (runApplicantPro)
            {
                var applicantProInputs = domains
                    .Select((domain, index) => new { Domain = domain, SourceRow = index + 2 })
                    .Where(x => ApplicantProDomainPattern.IsMatch(x.Domain))
                    .ToList();

                Console.WriteLine($"[URL Refresh] ApplicantPro candidate domains: {applicantProInputs.Count}");
                using var applicantProGate = new SemaphoreSlim(10);
                var applicantProTasks = applicantProInputs.Select(async input =>
                {
                    await applicantProGate.WaitAsync();
                    try { return await DiscoverApplicantProAsync(input.SourceRow, input.Domain); }
                    finally { applicantProGate.Release(); }
                }).ToList();

                foreach (var task in applicantProTasks)
                {
                    var result = await task;
                    if (result != null) applicantProRows.Add(result);
                }
            }

            if (runCatsOne)
            {
                var catsOneInputs = domains
                    .Select((domain, index) => new { Domain = domain, SourceRow = index + 2 })
                    .Where(x => CatsOneDomainPattern.IsMatch(x.Domain))
                    .ToList();

                Console.WriteLine($"[URL Refresh] CATS One candidate domains: {catsOneInputs.Count}");
                using var catsOneGate = new SemaphoreSlim(10);
                var catsOneTasks = catsOneInputs.Select(async input =>
                {
                    await catsOneGate.WaitAsync();
                    try { return await DiscoverCatsOneAsync(input.SourceRow, input.Domain); }
                    finally { catsOneGate.Release(); }
                }).ToList();

                foreach (var task in catsOneTasks)
                {
                    var result = await task;
                    if (result != null) catsOneRows.Add(result);
                }
            }

            if (runZohoRecruit)
            {
                var zohoRecruitInputs = domains
                    .Select((domain, index) => new { Domain = domain, SourceRow = index + 2 })
                    .Where(x => ZohoRecruitDomainPattern.IsMatch(x.Domain))
                    .ToList();

                Console.WriteLine($"[URL Refresh] Zoho Recruit candidate domains: {zohoRecruitInputs.Count}");
                using var zohoRecruitGate = new SemaphoreSlim(8);
                var zohoRecruitTasks = zohoRecruitInputs.Select(async input =>
                {
                    await zohoRecruitGate.WaitAsync();
                    try { return await DiscoverZohoRecruitAsync(input.SourceRow, input.Domain); }
                    finally { zohoRecruitGate.Release(); }
                }).ToList();

                foreach (var task in zohoRecruitTasks)
                {
                    var result = await task;
                    if (result != null) zohoRecruitRows.Add(result);
                }
            }

            int sourceRow = 1;
            foreach (var domain in domains)
            {
                sourceRow++;
                if (runOracleCloud && OracleCloudDomainPattern.IsMatch(domain))
                {
                    continue;
                }

                if (runPinpointHq && PinpointHqDomainPattern.IsMatch(domain))
                {
                    continue;
                }

                if (runPersonio && PersonioDomainPattern.IsMatch(domain))
                {
                    continue;
                }

                if (runFreshteam && FreshteamDomainPattern.IsMatch(domain))
                {
                    continue;
                }

                if (runJobsoid && JobsoidDomainPattern.IsMatch(domain))
                {
                    continue;
                }

                if (runApplicantPro && ApplicantProDomainPattern.IsMatch(domain))
                {
                    continue;
                }

                if (runCatsOne && CatsOneDomainPattern.IsMatch(domain))
                {
                    continue;
                }

                if (runZohoRecruit && ZohoRecruitDomainPattern.IsMatch(domain))
                {
                    continue;
                }

                var workdayMatch = WorkdayDomainPattern.Match(domain);
                if (runWorkday && workdayMatch.Success)
                {
                    var result = await DiscoverWorkdayAsync(sourceRow, domain, workdayMatch.Groups["tenant"].Value, workdayMatch.Groups["server"].Value);
                    if (result != null) workdayRows.Add(result);
                    continue;
                }

                if (runIcims && IcimsDomainPattern.IsMatch(domain))
                {
                    var result = await DiscoverIcimsAsync(sourceRow, domain);
                    if (result != null) icimsRows.Add(result);
                    continue;
                }

                if (runJazzHr && JazzHrDomainPattern.IsMatch(domain))
                {
                    var result = await DiscoverJazzHrAsync(sourceRow, domain);
                    if (result != null) jazzHrRows.Add(result);
                    continue;
                }

                if (runBambooHr && BambooHrDomainPattern.IsMatch(domain))
                {
                    var result = await DiscoverBambooHrAsync(sourceRow, domain);
                    if (result != null) bambooHrRows.Add(result);
                    continue;
                }

                if (runBreezyHr && BreezyHrDomainPattern.IsMatch(domain))
                {
                    var result = await DiscoverBreezyHrAsync(sourceRow, domain);
                    if (result != null) breezyHrRows.Add(result);
                    continue;
                }

            }

            if (runWorkday)
            {
                var workdayDir = Path.Combine(outputRoot, "workday_discovery");
                Directory.CreateDirectory(workdayDir);
                WriteCsv(
                    Path.Combine(workdayDir, "workday_discovery_valid_latest.csv"),
                    workdayRows,
                    new[] { "source_row", "original_domain", "domain", "tenant", "wd_server", "site", "api_url", "careers_url", "status_code", "final_url", "validated", "total_jobs", "sample_title", "error", "candidate_count", "attempted_sites", "discovery_notes", "elapsed_seconds" });
                Console.WriteLine($"[URL Refresh] Workday valid URLs: {workdayRows.Count}");
            }

            if (runIcims)
            {
                var icimsDir = Path.Combine(outputRoot, "icims_jobs");
                Directory.CreateDirectory(icimsDir);
                WriteCsv(
                    Path.Combine(icimsDir, "icims_link_counts_latest.csv"),
                    icimsRows,
                    new[] { "domain", "search_url", "pages_fetched", "job_links_found", "sample_job_url", "error", "elapsed_seconds" });
                Console.WriteLine($"[URL Refresh] iCIMS valid URLs: {icimsRows.Count}");
            }

            if (runJazzHr)
            {
                var jazzHrDir = Path.Combine(outputRoot, "jazzhr_jobs");
                Directory.CreateDirectory(jazzHrDir);
                WriteCsv(
                    Path.Combine(jazzHrDir, "jazzhr_link_counts_latest.csv"),
                    jazzHrRows,
                    new[] { "source_row", "domain", "apply_url", "status_code", "job_links_found", "sample_job_url", "validated", "error", "elapsed_seconds" });
                Console.WriteLine($"[URL Refresh] JazzHR valid URLs: {jazzHrRows.Count}");
            }

            if (runBambooHr)
            {
                var bambooHrDir = Path.Combine(outputRoot, "bamboohr_jobs");
                Directory.CreateDirectory(bambooHrDir);
                WriteCsv(
                    Path.Combine(bambooHrDir, "bamboohr_link_counts_latest.csv"),
                    bambooHrRows,
                    new[] { "source_row", "domain", "list_url", "pages_fetched", "job_links_found", "sample_job_url", "error", "elapsed_seconds" });
                Console.WriteLine($"[URL Refresh] BambooHR tenants with jobs: {bambooHrRows.Count}");
            }

            if (runBreezyHr)
            {
                var breezyHrDir = Path.Combine(outputRoot, "breezyhr_jobs");
                Directory.CreateDirectory(breezyHrDir);
                WriteCsv(
                    Path.Combine(breezyHrDir, "breezyhr_link_counts_latest.csv"),
                    breezyHrRows,
                    new[] { "source_row", "domain", "json_url", "status_code", "job_links_found", "sample_job_url", "sample_title", "error", "elapsed_seconds" });
                Console.WriteLine($"[URL Refresh] BreezyHR tenants with jobs: {breezyHrRows.Count}");
            }

            if (runOracleCloud)
            {
                var oracleDir = Path.Combine(outputRoot, "oraclecloud_jobs");
                Directory.CreateDirectory(oracleDir);
                WriteCsv(
                    Path.Combine(oracleDir, "oraclecloud_requisition_urls_latest.csv"),
                    oracleCloudRows,
                    new[] { "source_row", "domain", "site", "api_url", "careers_url", "status_code", "validated", "total_jobs", "sample_job_id", "sample_title", "sample_company", "sample_job_url", "error", "elapsed_seconds" });
                Console.WriteLine($"[URL Refresh] Oracle Cloud tenants with jobs: {oracleCloudRows.Count}");
            }

            if (runPinpointHq)
            {
                var pinpointDir = Path.Combine(outputRoot, "pinpointhq_jobs");
                Directory.CreateDirectory(pinpointDir);
                WriteCsv(
                    Path.Combine(pinpointDir, "pinpointhq_link_counts_latest.csv"),
                    pinpointHqRows,
                    new[] { "source_row", "domain", "rss_url", "careers_url", "status_code", "job_links_found", "sample_job_url", "sample_title", "sample_posted_at", "error", "elapsed_seconds" });
                Console.WriteLine($"[URL Refresh] PinpointHQ tenants with jobs: {pinpointHqRows.Count}");
            }

            if (runPersonio)
            {
                var personioDir = Path.Combine(outputRoot, "personio_jobs");
                Directory.CreateDirectory(personioDir);
                WriteCsv(
                    Path.Combine(personioDir, "personio_xml_feeds_latest.csv"),
                    personioRows,
                    new[] { "source_row", "domain", "xml_url", "careers_url", "status_code", "job_links_found", "sample_job_url", "sample_title", "sample_posted_at", "error", "elapsed_seconds" });
                Console.WriteLine($"[URL Refresh] Personio tenants with jobs: {personioRows.Count}");
            }

            if (runFreshteam)
            {
                var freshteamDir = Path.Combine(outputRoot, "freshteam_jobs");
                Directory.CreateDirectory(freshteamDir);
                WriteCsv(
                    Path.Combine(freshteamDir, "freshteam_link_counts_latest.csv"),
                    freshteamRows,
                    new[] { "source_row", "domain", "list_url", "status_code", "job_links_found", "sample_job_url", "sample_title", "error", "elapsed_seconds" });
                Console.WriteLine($"[URL Refresh] Freshteam tenants with jobs: {freshteamRows.Count}");
            }

            if (runJobsoid)
            {
                var jobsoidDir = Path.Combine(outputRoot, "jobsoid_jobs");
                Directory.CreateDirectory(jobsoidDir);
                WriteCsv(
                    Path.Combine(jobsoidDir, "jobsoid_api_urls_latest.csv"),
                    jobsoidRows,
                    new[] { "source_row", "domain", "api_url", "careers_url", "status_code", "job_links_found", "sample_job_url", "sample_title", "sample_posted_at", "error", "elapsed_seconds" });
                Console.WriteLine($"[URL Refresh] Jobsoid tenants with jobs: {jobsoidRows.Count}");
            }

            if (runApplicantPro)
            {
                var applicantProDir = Path.Combine(outputRoot, "applicantpro_jobs");
                Directory.CreateDirectory(applicantProDir);
                WriteCsv(
                    Path.Combine(applicantProDir, "applicantpro_api_urls_latest.csv"),
                    applicantProRows,
                    new[] { "source_row", "domain", "site_id", "list_url", "careers_url", "status_code", "job_links_found", "sample_job_url", "sample_title", "error", "elapsed_seconds" });
                Console.WriteLine($"[URL Refresh] ApplicantPro tenants with jobs: {applicantProRows.Count}");
            }

            if (runCatsOne)
            {
                var catsOneDir = Path.Combine(outputRoot, "catsone_jobs");
                Directory.CreateDirectory(catsOneDir);
                WriteCsv(
                    Path.Combine(catsOneDir, "catsone_portals_latest.csv"),
                    catsOneRows,
                    new[] { "source_row", "domain", "portal_id", "careers_url", "status_code", "job_links_found", "sample_job_url", "sample_title", "error", "elapsed_seconds" });
                Console.WriteLine($"[URL Refresh] CATS One tenants with jobs: {catsOneRows.Count}");
            }

            if (runZohoRecruit)
            {
                var zohoRecruitDir = Path.Combine(outputRoot, "zohorecruit_jobs");
                Directory.CreateDirectory(zohoRecruitDir);
                WriteCsv(
                    Path.Combine(zohoRecruitDir, "zohorecruit_public_urls_latest.csv"),
                    zohoRecruitRows,
                    new[] { "source_row", "domain", "careers_url", "status_code", "job_links_found", "sample_job_url", "sample_title", "error", "elapsed_seconds" });
                Console.WriteLine($"[URL Refresh] Zoho Recruit tenants with jobs: {zohoRecruitRows.Count}");
            }
        }

        private static async Task<Dictionary<string, string>> DiscoverWorkdayAsync(int sourceRow, string domain, string tenant, string server)
        {
            var started = DateTime.UtcNow;
            var attempted = new List<string>();

            foreach (var site in WorkdaySiteCandidates)
            {
                attempted.Add(site);
                var apiUrl = $"https://{domain}/wday/cxs/{tenant}/{site}/jobs";
                var careersUrl = $"https://{domain}/en-US/{site}";

                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                    request.Headers.Referrer = new Uri(careersUrl);
                    request.Content = new StringContent(JsonSerializer.Serialize(new { appliedFacets = new { }, limit = 1, offset = 0, searchText = "" }), Encoding.UTF8, "application/json");
                    using var response = await _http.SendAsync(request);
                    var text = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode) continue;

                    using var doc = JsonDocument.Parse(text);
                    var root = doc.RootElement;
                    var total = root.TryGetProperty("total", out var totalEl) ? totalEl.ToString() : "";
                    var sampleTitle = "";
                    if (root.TryGetProperty("jobPostings", out var postings) && postings.ValueKind == JsonValueKind.Array && postings.GetArrayLength() > 0)
                    {
                        sampleTitle = postings[0].TryGetProperty("title", out var title) ? title.GetString() ?? "" : "";
                    }

                    return new Dictionary<string, string>
                    {
                        ["source_row"] = sourceRow.ToString(),
                        ["original_domain"] = domain,
                        ["domain"] = domain,
                        ["tenant"] = tenant,
                        ["wd_server"] = server,
                        ["site"] = site,
                        ["api_url"] = apiUrl,
                        ["careers_url"] = careersUrl,
                        ["status_code"] = ((int)response.StatusCode).ToString(),
                        ["final_url"] = apiUrl,
                        ["validated"] = "True",
                        ["total_jobs"] = total,
                        ["sample_title"] = sampleTitle,
                        ["error"] = "",
                        ["candidate_count"] = WorkdaySiteCandidates.Length.ToString(),
                        ["attempted_sites"] = string.Join(";", attempted),
                        ["discovery_notes"] = "C# refresh-ats-urls",
                        ["elapsed_seconds"] = (DateTime.UtcNow - started).TotalSeconds.ToString("0.00")
                    };
                }
                catch
                {
                    continue;
                }
            }

            return null;
        }

        private static async Task<Dictionary<string, string>> DiscoverIcimsAsync(int sourceRow, string domain)
        {
            var started = DateTime.UtcNow;
            foreach (var path in new[] { "/jobs/search?ss=1", "/jobs/search", "/jobs" })
            {
                var url = $"https://{domain}{path}";
                try
                {
                    var html = await _http.GetStringAsync(url);
                    var links = ExtractJobLinks($"https://{domain}", html);
                    if (links.Count == 0 && !html.Contains("icims", StringComparison.OrdinalIgnoreCase)) continue;

                    return new Dictionary<string, string>
                    {
                        ["domain"] = domain,
                        ["search_url"] = url,
                        ["pages_fetched"] = "1",
                        ["job_links_found"] = links.Count.ToString(),
                        ["sample_job_url"] = links.FirstOrDefault() ?? "",
                        ["error"] = "",
                        ["elapsed_seconds"] = (DateTime.UtcNow - started).TotalSeconds.ToString("0.00")
                    };
                }
                catch
                {
                    continue;
                }
            }

            return null;
        }

        private static async Task<Dictionary<string, string>> DiscoverJazzHrAsync(int sourceRow, string domain)
        {
            var started = DateTime.UtcNow;
            foreach (var path in new[] { "/apply", "/apply/", "/" })
            {
                var url = $"https://{domain}{path}";
                try
                {
                    using var response = await _http.GetAsync(url);
                    var html = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode) continue;

                    var links = ExtractJazzHrJobLinks($"https://{domain}", html);
                    var isJazzHr = html.Contains("JazzHR", StringComparison.OrdinalIgnoreCase) ||
                                   html.Contains("resumator", StringComparison.OrdinalIgnoreCase) ||
                                   html.Contains("applytojob", StringComparison.OrdinalIgnoreCase);

                    if (links.Count == 0 && !isJazzHr) continue;

                    return new Dictionary<string, string>
                    {
                        ["source_row"] = sourceRow.ToString(),
                        ["domain"] = domain,
                        ["apply_url"] = url,
                        ["status_code"] = ((int)response.StatusCode).ToString(),
                        ["job_links_found"] = links.Count.ToString(),
                        ["sample_job_url"] = links.FirstOrDefault() ?? "",
                        ["validated"] = (isJazzHr || links.Count > 0).ToString(),
                        ["error"] = "",
                        ["elapsed_seconds"] = (DateTime.UtcNow - started).TotalSeconds.ToString("0.00")
                    };
                }
                catch
                {
                    continue;
                }
            }

            return null;
        }

        private static async Task<Dictionary<string, string>> DiscoverBambooHrAsync(int sourceRow, string domain)
        {
            var started = DateTime.UtcNow;
            var listUrl = $"https://{domain}/careers/list";

            try
            {
                using var response = await _http.GetAsync(listUrl);
                var text = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                var totalJobs = 0;
                if (root.TryGetProperty("meta", out var meta) &&
                    meta.TryGetProperty("totalCount", out var totalEl) &&
                    totalEl.TryGetInt32(out var parsedTotal))
                {
                    totalJobs = parsedTotal;
                }
                else
                {
                    totalJobs = result.GetArrayLength();
                }

                if (totalJobs <= 0) return null;

                var sampleJobId = "";
                if (result.GetArrayLength() > 0)
                {
                    var sample = result[0];
                    sampleJobId = sample.TryGetProperty("id", out var idEl) ? idEl.ToString() : "";
                }

                return new Dictionary<string, string>
                {
                    ["source_row"] = sourceRow.ToString(),
                    ["domain"] = domain,
                    ["list_url"] = listUrl,
                    ["pages_fetched"] = "1",
                    ["job_links_found"] = totalJobs.ToString(),
                    ["sample_job_url"] = string.IsNullOrWhiteSpace(sampleJobId) ? "" : $"https://{domain}/careers/{sampleJobId}",
                    ["error"] = "",
                    ["elapsed_seconds"] = (DateTime.UtcNow - started).TotalSeconds.ToString("0.00")
                };
            }
            catch
            {
                return null;
            }
        }

        private static async Task<Dictionary<string, string>> DiscoverBreezyHrAsync(int sourceRow, string domain)
        {
            var started = DateTime.UtcNow;
            var jsonUrl = $"https://{domain}/json";

            try
            {
                using var response = await _http.GetAsync(jsonUrl);
                var text = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return null;

                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

                var totalJobs = doc.RootElement.GetArrayLength();
                if (totalJobs <= 0) return null;

                var sample = doc.RootElement[0];
                var sampleUrl = sample.TryGetProperty("url", out var urlEl) ? urlEl.GetString() ?? "" : "";
                var sampleTitle = sample.TryGetProperty("name", out var titleEl) ? titleEl.GetString() ?? "" : "";

                return new Dictionary<string, string>
                {
                    ["source_row"] = sourceRow.ToString(),
                    ["domain"] = domain,
                    ["json_url"] = jsonUrl,
                    ["status_code"] = ((int)response.StatusCode).ToString(),
                    ["job_links_found"] = totalJobs.ToString(),
                    ["sample_job_url"] = sampleUrl,
                    ["sample_title"] = sampleTitle,
                    ["error"] = "",
                    ["elapsed_seconds"] = (DateTime.UtcNow - started).TotalSeconds.ToString("0.00")
                };
            }
            catch
            {
                return null;
            }
        }

        private static async Task<Dictionary<string, string>> DiscoverOracleCloudAsync(int sourceRow, string domain)
        {
            var started = DateTime.UtcNow;
            var site = await DiscoverOracleSiteNumberAsync(domain) ?? "CX";

            var apiUrl = OracleCloudApiUrl(domain, site, 1, 0);
            var careersUrl = $"https://{domain}/hcmUI/CandidateExperience/en/sites/{site}";

            try
            {
                using var response = await GetWithTimeoutAsync(apiUrl, TimeSpan.FromSeconds(8));
                var text = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return null;

                using var doc = JsonDocument.Parse(text);
                if (!TryGetOracleSearch(doc.RootElement, out var search)) return null;

                var totalJobs = TryGetInt(search, "TotalJobsCount");
                if (totalJobs <= 0) return null;

                var sampleId = "";
                var sampleTitle = "";
                if (search.TryGetProperty("requisitionList", out var list) &&
                    list.ValueKind == JsonValueKind.Array &&
                    list.GetArrayLength() > 0)
                {
                    var sample = list[0];
                    sampleId = GetJsonString(sample, "Id");
                    sampleTitle = GetJsonString(sample, "Title");
                }

                var sampleCompany = "";
                if (search.TryGetProperty("organizationsFacet", out var orgs) &&
                    orgs.ValueKind == JsonValueKind.Array &&
                    orgs.GetArrayLength() > 0)
                {
                    sampleCompany = GetJsonString(orgs[0], "Name");
                }

                return new Dictionary<string, string>
                {
                    ["source_row"] = sourceRow.ToString(),
                    ["domain"] = domain,
                    ["site"] = site,
                    ["api_url"] = OracleCloudApiUrl(domain, site, 100, 0),
                    ["careers_url"] = careersUrl,
                    ["status_code"] = ((int)response.StatusCode).ToString(),
                    ["validated"] = "True",
                    ["total_jobs"] = totalJobs.ToString(),
                    ["sample_job_id"] = sampleId,
                    ["sample_title"] = sampleTitle,
                    ["sample_company"] = sampleCompany,
                    ["sample_job_url"] = string.IsNullOrWhiteSpace(sampleId) ? "" : OracleCloudJobUrl(domain, site, sampleId),
                    ["error"] = "",
                    ["elapsed_seconds"] = (DateTime.UtcNow - started).TotalSeconds.ToString("0.00")
                };
            }
            catch
            {
                return null;
            }
        }

        private static async Task<Dictionary<string, string>> DiscoverPinpointHqAsync(int sourceRow, string domain)
        {
            var started = DateTime.UtcNow;
            var rssUrl = $"https://{domain}/jobs.rss";
            var careersUrl = $"https://{domain}/";

            try
            {
                using var response = await GetWithTimeoutAsync(rssUrl, TimeSpan.FromSeconds(6));
                var text = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return null;

                var doc = XDocument.Parse(text);
                var items = doc.Descendants("item").ToList();
                if (items.Count == 0) return null;

                var sample = items.FirstOrDefault();
                var sampleUrl = WebUtility.HtmlDecode((string)sample?.Element("link") ?? "").Trim();
                var sampleTitle = WebUtility.HtmlDecode((string)sample?.Element("title") ?? "").Trim();
                var samplePostedAt = WebUtility.HtmlDecode((string)sample?.Element("pubDate") ?? "").Trim();

                return new Dictionary<string, string>
                {
                    ["source_row"] = sourceRow.ToString(),
                    ["domain"] = domain,
                    ["rss_url"] = rssUrl,
                    ["careers_url"] = careersUrl,
                    ["status_code"] = ((int)response.StatusCode).ToString(),
                    ["job_links_found"] = items.Count.ToString(),
                    ["sample_job_url"] = sampleUrl,
                    ["sample_title"] = sampleTitle,
                    ["sample_posted_at"] = samplePostedAt,
                    ["error"] = "",
                    ["elapsed_seconds"] = (DateTime.UtcNow - started).TotalSeconds.ToString("0.00")
                };
            }
            catch
            {
                return null;
            }
        }

        private static async Task<Dictionary<string, string>> DiscoverPersonioAsync(int sourceRow, string domain)
        {
            var started = DateTime.UtcNow;
            var xmlUrl = $"https://{domain}/xml";
            var careersUrl = $"https://{domain}/";

            try
            {
                using var response = await GetWithTimeoutAsync(xmlUrl, TimeSpan.FromSeconds(8));
                var text = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return null;

                var doc = XDocument.Parse(text);
                var positions = doc.Descendants("position").ToList();
                if (positions.Count == 0) return null;

                var sample = positions.FirstOrDefault();
                var sampleId = ElementText(sample, "id");
                var sampleTitle = ElementText(sample, "name");
                var samplePostedAt = ElementText(sample, "createdAt");

                return new Dictionary<string, string>
                {
                    ["source_row"] = sourceRow.ToString(),
                    ["domain"] = domain,
                    ["xml_url"] = xmlUrl,
                    ["careers_url"] = careersUrl,
                    ["status_code"] = ((int)response.StatusCode).ToString(),
                    ["job_links_found"] = positions.Count.ToString(),
                    ["sample_job_url"] = string.IsNullOrWhiteSpace(sampleId) ? careersUrl : $"https://{domain}/job/{sampleId}",
                    ["sample_title"] = sampleTitle,
                    ["sample_posted_at"] = samplePostedAt,
                    ["error"] = "",
                    ["elapsed_seconds"] = (DateTime.UtcNow - started).TotalSeconds.ToString("0.00")
                };
            }
            catch
            {
                return null;
            }
        }

        private static async Task<Dictionary<string, string>> DiscoverFreshteamAsync(int sourceRow, string domain)
        {
            var started = DateTime.UtcNow;
            var listUrl = $"https://{domain}/jobs";

            try
            {
                using var response = await GetWithTimeoutAsync(listUrl, TimeSpan.FromSeconds(8));
                var html = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return null;

                var links = ExtractFreshteamJobLinks($"https://{domain}", html);
                if (links.Count == 0) return null;

                return new Dictionary<string, string>
                {
                    ["source_row"] = sourceRow.ToString(),
                    ["domain"] = domain,
                    ["list_url"] = listUrl,
                    ["status_code"] = ((int)response.StatusCode).ToString(),
                    ["job_links_found"] = links.Count.ToString(),
                    ["sample_job_url"] = links.FirstOrDefault() ?? "",
                    ["sample_title"] = FreshteamTitleForUrl(html, links.FirstOrDefault()),
                    ["error"] = "",
                    ["elapsed_seconds"] = (DateTime.UtcNow - started).TotalSeconds.ToString("0.00")
                };
            }
            catch
            {
                return null;
            }
        }

        private static async Task<Dictionary<string, string>> DiscoverJobsoidAsync(int sourceRow, string domain)
        {
            var started = DateTime.UtcNow;
            var apiUrl = $"https://{domain}/api/v1/jobs";
            var careersUrl = $"https://{domain}/";

            try
            {
                using var response = await GetWithTimeoutAsync(apiUrl, TimeSpan.FromSeconds(8));
                var text = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return null;

                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

                var count = doc.RootElement.GetArrayLength();
                if (count == 0) return null;

                var sample = doc.RootElement[0];
                var sampleUrl = GetJsonString(sample, "hostedUrl");
                var sampleTitle = GetJsonString(sample, "title");
                var samplePostedAt = GetJsonString(sample, "postedDate");

                return new Dictionary<string, string>
                {
                    ["source_row"] = sourceRow.ToString(),
                    ["domain"] = domain,
                    ["api_url"] = apiUrl,
                    ["careers_url"] = careersUrl,
                    ["status_code"] = ((int)response.StatusCode).ToString(),
                    ["job_links_found"] = count.ToString(),
                    ["sample_job_url"] = sampleUrl,
                    ["sample_title"] = sampleTitle,
                    ["sample_posted_at"] = samplePostedAt,
                    ["error"] = "",
                    ["elapsed_seconds"] = (DateTime.UtcNow - started).TotalSeconds.ToString("0.00")
                };
            }
            catch
            {
                return null;
            }
        }

        private static async Task<Dictionary<string, string>> DiscoverApplicantProAsync(int sourceRow, string domain)
        {
            var started = DateTime.UtcNow;
            var careersUrl = $"https://{domain}/jobs/";

            try
            {
                var siteId = await ApplicantProScraper.DiscoverSiteIdAsync(domain);
                if (string.IsNullOrWhiteSpace(siteId)) return null;

                var listUrl = ApplicantProScraper.ApplicantProListUrl(domain, siteId);
                using var request = new HttpRequestMessage(HttpMethod.Get, listUrl);
                request.Headers.TryAddWithoutValidation("Referer", careersUrl);
                using var response = await _http.SendAsync(request);
                var text = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return null;

                using var doc = JsonDocument.Parse(text);
                if (!doc.RootElement.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("jobs", out var jobs) ||
                    jobs.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                var count = jobs.GetArrayLength();
                if (count == 0) return null;

                var sample = jobs[0];
                var sampleId = GetJsonString(sample, "id");
                var sampleTitle = GetJsonString(sample, "title");
                var sampleUrl = GetJsonString(sample, "jobUrl");
                if (string.IsNullOrWhiteSpace(sampleUrl) && !string.IsNullOrWhiteSpace(sampleId))
                {
                    sampleUrl = $"https://{domain}/jobs/{sampleId}";
                }

                return new Dictionary<string, string>
                {
                    ["source_row"] = sourceRow.ToString(),
                    ["domain"] = domain,
                    ["site_id"] = siteId,
                    ["list_url"] = listUrl,
                    ["careers_url"] = careersUrl,
                    ["status_code"] = ((int)response.StatusCode).ToString(),
                    ["job_links_found"] = count.ToString(),
                    ["sample_job_url"] = sampleUrl,
                    ["sample_title"] = sampleTitle,
                    ["error"] = "",
                    ["elapsed_seconds"] = (DateTime.UtcNow - started).TotalSeconds.ToString("0.00")
                };
            }
            catch
            {
                return null;
            }
        }

        private static async Task<Dictionary<string, string>> DiscoverCatsOneAsync(int sourceRow, string domain)
        {
            var started = DateTime.UtcNow;

            try
            {
                var result = await CatsOneScraper.DiscoverAsync(domain);
                if (result == null || result.JobCount <= 0) return null;

                return new Dictionary<string, string>
                {
                    ["source_row"] = sourceRow.ToString(),
                    ["domain"] = domain,
                    ["portal_id"] = result.PortalId,
                    ["careers_url"] = result.CareersUrl,
                    ["status_code"] = "200",
                    ["job_links_found"] = result.JobCount.ToString(),
                    ["sample_job_url"] = result.SampleJobUrl,
                    ["sample_title"] = result.SampleTitle,
                    ["error"] = "",
                    ["elapsed_seconds"] = (DateTime.UtcNow - started).TotalSeconds.ToString("0.00")
                };
            }
            catch
            {
                return null;
            }
        }

        private static async Task<Dictionary<string, string>> DiscoverZohoRecruitAsync(int sourceRow, string domain)
        {
            var started = DateTime.UtcNow;

            try
            {
                var result = await ZohoRecruitScraper.DiscoverAsync(domain);
                if (result == null || result.JobCount <= 0) return null;

                return new Dictionary<string, string>
                {
                    ["source_row"] = sourceRow.ToString(),
                    ["domain"] = domain,
                    ["careers_url"] = result.CareersUrl,
                    ["status_code"] = "200",
                    ["job_links_found"] = result.JobCount.ToString(),
                    ["sample_job_url"] = result.SampleJobUrl,
                    ["sample_title"] = result.SampleTitle,
                    ["error"] = "",
                    ["elapsed_seconds"] = (DateTime.UtcNow - started).TotalSeconds.ToString("0.00")
                };
            }
            catch
            {
                return null;
            }
        }

        private static async Task<string> DiscoverOracleSiteNumberAsync(string domain)
        {
            foreach (var site in new[] { "CX", "CX_1" })
            {
                try
                {
                    using var response = await GetWithTimeoutAsync($"https://{domain}/hcmUI/CandidateExperience/en/sites/{site}", TimeSpan.FromSeconds(5));
                    if (!response.IsSuccessStatusCode) continue;

                    var html = await response.Content.ReadAsStringAsync();
                    var match = OracleSiteNumberPattern.Match(html ?? "");
                    if (match.Success) return match.Groups["site"].Value;
                    if (html.Contains("CandidateExperience", StringComparison.OrdinalIgnoreCase)) return site;
                }
                catch
                {
                    continue;
                }
            }

            return null;
        }

        private static async Task<HttpResponseMessage> GetWithTimeoutAsync(string url, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            return await _http.GetAsync(url, cts.Token);
        }

        private static string OracleCloudApiUrl(string domain, string site, int limit, int offset)
        {
            return $"https://{domain}/hcmRestApi/resources/latest/recruitingCEJobRequisitions?onlyData=true&finder=findReqs;siteNumber={site},limit={limit},offset={offset},sortBy=POSTING_DATES_DESC&expand=requisitionList";
        }

        private static string OracleCloudJobUrl(string domain, string site, string id)
        {
            return $"https://{domain}/hcmUI/CandidateExperience/en/sites/{site}/job/{id}";
        }

        private static bool TryGetOracleSearch(JsonElement root, out JsonElement search)
        {
            search = default;
            if (!root.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array ||
                items.GetArrayLength() == 0)
            {
                return false;
            }

            search = items[0];
            return true;
        }

        private static int TryGetInt(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value)) return 0;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
            return int.TryParse(value.ToString(), out var parsed) ? parsed : 0;
        }

        private static string GetJsonString(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value)) return "";
            if (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined) return "";
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
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

        private static List<string> ExtractJazzHrJobLinks(string baseUrl, string html)
        {
            var links = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in JazzHrJobLinkPattern.Matches(html ?? ""))
            {
                var url = new Uri(new Uri(baseUrl), WebUtility.HtmlDecode(match.Groups["href"].Value)).ToString().Split('#')[0];
                if (seen.Add(url)) links.Add(url);
            }
            return links;
        }

        private static List<string> ExtractFreshteamJobLinks(string baseUrl, string html)
        {
            var links = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in FreshteamJobLinkPattern.Matches(html ?? ""))
            {
                var url = new Uri(new Uri(baseUrl), WebUtility.HtmlDecode(match.Groups["href"].Value)).ToString().Split('#')[0];
                if (seen.Add(url)) links.Add(url);
            }
            return links;
        }

        private static string FreshteamTitleForUrl(string html, string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
            var match = Regex.Match(html ?? "", $"href=[\"']{Regex.Escape(path)}[\"'][\\s\\S]*?<div[^>]+class=[\"'][^\"']*job-title[^\"']*[\"'][^>]*>(?<title>[\\s\\S]*?)</div>", RegexOptions.IgnoreCase);
            return match.Success ? WebUtility.HtmlDecode(Regex.Replace(match.Groups["title"].Value, "<[^>]+>", " ")).Trim() : "";
        }

        private static string ElementText(XElement element, string name)
        {
            return element?.Element(name)?.Value?.Trim() ?? "";
        }

        private static string ReadDomain(Dictionary<string, string> row)
        {
            foreach (var key in new[] { "Domain", "domain", "normalized_domain", "original_domain" })
            {
                var value = AtsCsv.Get(row, key);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }

            return row.Values.FirstOrDefault() ?? "";
        }

        private static string NormalizeDomain(string value)
        {
            value = (value ?? "").Trim().ToLowerInvariant().TrimEnd('/');
            value = Regex.Replace(value, "^https?://", "");
            value = value.Split('/')[0].Trim().Trim('.');
            return value.StartsWith("www.") ? value.Substring(4) : value;
        }

        private static bool ShouldRefreshSource(string onlySource, params string[] names)
        {
            if (string.IsNullOrWhiteSpace(onlySource)) return true;
            return names.Any(name => string.Equals(onlySource, name, StringComparison.OrdinalIgnoreCase));
        }

        private static void WriteCsv(string path, List<Dictionary<string, string>> rows, string[] fields)
        {
            using var writer = new StreamWriter(path, false, Encoding.UTF8);
            writer.WriteLine(string.Join(",", fields.Select(Escape)));
            foreach (var row in rows)
            {
                writer.WriteLine(string.Join(",", fields.Select(field => Escape(row.TryGetValue(field, out var value) ? value : ""))));
            }
        }

        private static string Escape(string value)
        {
            value ??= "";
            return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
                ? $"\"{value.Replace("\"", "\"\"")}\""
                : value;
        }
    }
}

