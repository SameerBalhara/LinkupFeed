using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LinkupFeed
{
    // SAP SuccessFactors (SF) is one of the most widely deployed ATSs. Most modern
    // SF career sites are built with Career Site Builder (CSB) and expose a public
    // JSON jobs endpoint that backs the search page:
    //
    //   GET https://{host}/careers/api/jobs?page={n}&pageSize=25&keywords=&location=
    //
    // Older SF deployments still serve the legacy "career?career_ns=job_listing" URL,
    // which can be augmented with &rss=1 to get an RSS feed. We try the CSB JSON
    // endpoint first; if it fails, we fall back to the RSS feed.
    internal class SuccessFactorsScraper
    {
        private const int SOURCE_ID = 56;
        private const int PageSize = 25;
        private const int MaxPagesPerTenant = 40; // safety cap (≈ 1000 jobs / tenant)

        // (host, companyId, displayCompany)
        //   host        — the public career-site host (e.g. "jobs.sap.com").
        //   companyId   — SF "company" parameter used by the legacy/RSS endpoint.
        //                 Leave empty if the tenant only uses the CSB JSON API.
        //   company     — human-readable employer name written to the feed.
        private static readonly (string Host, string CompanyId, string Company)[] Tenants =
        {
            ("jobs.sap.com",                "SAP",       "SAP"),
            ("careers.ge.com",              "GE",        "GE"),
            ("careers.deloitte.com",        "Deloitte",  "Deloitte"),
            ("careers.pwc.com",             "PwC",       "PwC"),
            ("jobs.ey.com",                 "EY",        "EY"),
            ("careers.unilever.com",        "Unilever",  "Unilever"),
            ("jobs.colgate.com",            "Colgate",   "Colgate-Palmolive"),
            ("jobs.kraftheinzcompany.com",  "KHC",       "Kraft Heinz"),
            ("careers.mckesson.com",        "McKesson",  "McKesson"),
            ("careers.cigna.com",           "Cigna",     "Cigna"),
            ("careers.humana.com",          "Humana",    "Humana"),
            ("careers.lilly.com",           "Lilly",     "Eli Lilly"),
            ("careers.bms.com",             "BMS",       "Bristol Myers Squibb"),
            ("careers.merck.com",           "Merck",     "Merck"),
            ("careers.pfizer.com",          "Pfizer",    "Pfizer"),

            // ── Added batch: more SF career sites (non-CSB hosts fall back to RSS;
            //    unreachable hosts error out and are skipped) ──────────────────
            ("jobs.basf.com",               "BASF",      "BASF"),
            ("careers.bayer.com",           "Bayer",     "Bayer"),
            ("jobs.bosch.com",              "Bosch",     "Bosch"),
            ("careers.jnj.com",             "JNJ",       "Johnson & Johnson"),
            ("jobs.abbott",                 "Abbott",    "Abbott"),
            ("careers.bd.com",              "BD",        "Becton Dickinson"),
            ("careers.novartis.com",        "Novartis",  "Novartis"),
            ("jobs.takeda.com",             "Takeda",    "Takeda"),
            ("careers.amgen.com",           "Amgen",     "Amgen"),
            ("jobs.boehringer-ingelheim.com","BI",       "Boehringer Ingelheim"),
            ("careers.3m.com",              "3M",        "3M"),
            ("jobs.siemens-healthineers.com","SHS",      "Siemens Healthineers"),
        };

        private static readonly Regex HtmlStripPattern =
            new Regex(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex MultiSpacePattern =
            new Regex(@"\s{2,}", RegexOptions.Compiled);

        private static readonly HttpClient _http = BuildClient();

        private static HttpClient BuildClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (compatible; ITJobCafeBot/1.0; +https://itjobcafe.com)");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/xml, */*");
            return c;
        }

        public async Task<List<ScrapedJob>> FetchJobsAsync()
        {
            var results = new List<ScrapedJob>();

            foreach (var (host, companyId, company) in Tenants)
            {
                try
                {
                    Console.WriteLine($"[SuccessFactors] {host} → starting");
                    var jobs = await FetchTenantAsync(host, companyId, company);
                    Console.WriteLine($"[SuccessFactors] {host} → {jobs.Count} US jobs");
                    results.AddRange(jobs);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SuccessFactors] {host} error: {ex.Message}");
                }

                await Task.Delay(1200);
            }

            return results;
        }

        private async Task<List<ScrapedJob>> FetchTenantAsync(string host, string companyId, string company)
        {
            var csbJobs = await TryFetchCsbJsonAsync(host, company);
            if (csbJobs.Count > 0) return csbJobs;

            // Fallback to the legacy RSS feed when CSB JSON isn't exposed.
            if (!string.IsNullOrEmpty(companyId))
                return await TryFetchLegacyRssAsync(host, companyId, company);

            return csbJobs;
        }

        // ── CSB JSON endpoint ────────────────────────────────────────────────────
        private async Task<List<ScrapedJob>> TryFetchCsbJsonAsync(string host, string company)
        {
            var jobs = new List<ScrapedJob>();
            var baseSite = $"https://{host}";

            for (int page = 1; page <= MaxPagesPerTenant; page++)
            {
                var url = $"{baseSite}/careers/api/jobs?page={page}&pageSize={PageSize}";

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Referrer = new Uri($"{baseSite}/careers/SearchJobs/");
                req.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

                HttpResponseMessage resp;
                try { resp = await _http.SendAsync(req); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SuccessFactors] {host} CSB transport error: {ex.Message}");
                    break;
                }

                using (resp)
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[SuccessFactors] {host} CSB HTTP {(int)resp.StatusCode} on page {page}");
                        break;
                    }

                    var json = await resp.Content.ReadAsStringAsync();
                    if (string.IsNullOrWhiteSpace(json) || json.TrimStart().StartsWith("<"))
                    {
                        // Endpoint returned HTML — not a CSB JSON site.
                        break;
                    }

                    int pageCount = 0;
                    try
                    {
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        // CSB responses vary; try common shapes.
                        JsonElement listEl = default;
                        if (root.ValueKind == JsonValueKind.Array) listEl = root;
                        else if (root.TryGetProperty("jobs", out var j1)) listEl = j1;
                        else if (root.TryGetProperty("results", out var j2)) listEl = j2;
                        else if (root.TryGetProperty("data", out var j3) &&
                                 j3.TryGetProperty("jobs", out var j4)) listEl = j4;

                        if (listEl.ValueKind != JsonValueKind.Array) break;

                        foreach (var j in listEl.EnumerateArray())
                        {
                            pageCount++;
                            var title    = j.GetStringOrNull("title", "jobTitle")
                                        ?? j.GetStringOrNull("name");
                            var location = j.GetStringOrNull("location", "jobLocation")
                                        ?? j.GetStringOrNull("city")
                                        ?? "";
                            var extId    = j.GetStringOrNull("jobId", "id")
                                        ?? j.GetStringOrNull("reqId")
                                        ?? j.GetStringOrNull("requisitionId");
                            var jobUrl   = j.GetStringOrNull("url", "jobUrl")
                                        ?? j.GetStringOrNull("applyUrl");
                            var desc     = j.GetStringOrNull("description", "jobDescription");
                            var posted   = j.GetStringOrNull("postedDate", "postingDate")
                                        ?? j.GetStringOrNull("datePosted");

                            if (!UsLocationFilter.IsUs(location)) continue;

                            if (string.IsNullOrEmpty(jobUrl) && !string.IsNullOrEmpty(extId))
                                jobUrl = $"{baseSite}/careers/job/{extId}";

                            jobs.Add(new ScrapedJob
                            {
                                SourceId    = SOURCE_ID,
                                ExternalId  = extId,
                                Title       = title,
                                Company     = company,
                                Location    = location,
                                Description = StripHtml(desc),
                                JobUrl      = jobUrl,
                                IsRemote    = location.IndexOf("remote", StringComparison.OrdinalIgnoreCase) >= 0,
                                DatePosted  = DateTime.TryParse(posted, out var dt) ? dt : (DateTime?)null
                            });
                        }
                    }
                    catch (JsonException)
                    {
                        // Not JSON we understand — bail to caller for RSS fallback.
                        break;
                    }

                    if (pageCount < PageSize) break;
                }

                await Task.Delay(400);
            }

            return jobs;
        }

        // ── Legacy RSS endpoint ──────────────────────────────────────────────────
        private async Task<List<ScrapedJob>> TryFetchLegacyRssAsync(string host, string companyId, string company)
        {
            var jobs = new List<ScrapedJob>();
            var url = $"https://{host}/career?company={Uri.EscapeDataString(companyId)}" +
                      $"&career_ns=job_listing&navBarLevel=JOB_SEARCH&rcm_site_locale=en_US&rss=1";

            string xml;
            try { xml = await _http.GetStringAsync(url); }
            catch (Exception ex)
            {
                Console.WriteLine($"[SuccessFactors] {host} RSS error: {ex.Message}");
                return jobs;
            }

            if (string.IsNullOrWhiteSpace(xml)) return jobs;

            try
            {
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(xml);

                var items = doc.GetElementsByTagName("item");
                foreach (System.Xml.XmlNode item in items)
                {
                    var title    = item["title"]?.InnerText;
                    var link     = item["link"]?.InnerText;
                    var desc     = item["description"]?.InnerText;
                    var pubDate  = item["pubDate"]?.InnerText;
                    var guid     = item["guid"]?.InnerText;

                    // The description usually contains "<b>Location:</b> City, ST".
                    var location = ExtractLocationFromDescription(desc) ?? "";

                    if (!UsLocationFilter.IsUs(location)) continue;

                    var extId = ExtractReqIdFromUrl(link) ?? guid;

                    jobs.Add(new ScrapedJob
                    {
                        SourceId    = SOURCE_ID,
                        ExternalId  = extId,
                        Title       = title,
                        Company     = company,
                        Location    = location,
                        Description = StripHtml(desc),
                        JobUrl      = link,
                        IsRemote    = location.IndexOf("remote", StringComparison.OrdinalIgnoreCase) >= 0,
                        DatePosted  = DateTime.TryParse(pubDate, out var dt) ? dt : (DateTime?)null
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SuccessFactors] {host} RSS parse error: {ex.Message}");
            }

            return jobs;
        }

        private static readonly Regex LocationLabelPattern =
            new Regex(@"Location[^A-Za-z0-9]*([A-Za-z0-9 ,.\-/]+)",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string ExtractLocationFromDescription(string desc)
        {
            if (string.IsNullOrWhiteSpace(desc)) return null;
            var stripped = StripHtml(desc);
            var m = LocationLabelPattern.Match(stripped);
            return m.Success ? m.Groups[1].Value.Trim().TrimEnd('.', ',') : null;
        }

        private static readonly Regex ReqIdPattern =
            new Regex(@"career_job_req_id=(\d+)", RegexOptions.Compiled);

        private static string ExtractReqIdFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var m = ReqIdPattern.Match(url);
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            var text = HtmlStripPattern.Replace(html, " ");
            text = System.Net.WebUtility.HtmlDecode(text);
            return MultiSpacePattern.Replace(text, " ").Trim();
        }
    }
}
