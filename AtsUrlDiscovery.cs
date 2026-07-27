using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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

            int sourceRow = 1;
            foreach (var domain in domains)
            {
                sourceRow++;
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

