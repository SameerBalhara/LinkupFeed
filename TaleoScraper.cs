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
    // Taleo is Oracle's ATS. Each customer is hosted at https://{tenant}.taleo.net/careersection/...
    // The career site exposes an undocumented JSON search endpoint:
    //   POST https://{tenant}.taleo.net/careersection/rest/jobboard/searchjobs?lang=en&portal={portalId}
    // Returns a paginated list whose row "values" follow the order declared by "fields".
    internal class TaleoScraper
    {
        private const int SOURCE_ID = 55;
        private const int PageSize = 25;
        private const int MaxPagesPerTenant = 40; // safety cap (= 1000 jobs / tenant)

        // (tenant, portalId, displayCompany). portalId is the careersection number — most
        // tenants use "1" but some publish multiple portals. Adjust as needed.
        // DNS-verified live Taleo tenants. Portal IDs default to "1"; some sites publish
        // multiple portals (e.g. "2" for hourly/retail) — extend as needed.
        private static readonly (string Tenant, string Portal, string Company)[] Tenants =
        {
            ("starbucks",      "1", "Starbucks"),
            ("att",            "1", "AT&T"),
            ("baesystems",     "1", "BAE Systems"),
            ("intel",          "1", "Intel"),
            ("cognizant",      "1", "Cognizant"),
            ("genpact",        "1", "Genpact"),
            ("ajg",            "1", "Arthur J. Gallagher"),
            ("hilton",         "1", "Hilton"),
            ("hyatt",          "1", "Hyatt"),
            ("valero",         "1", "Valero"),
            ("energytransfer", "1", "Energy Transfer"),
            ("corteva",        "1", "Corteva"),
            ("praxair",        "1", "Praxair"),

            // ── Added batch: more Taleo tenants (bad tenants DNS-fail & are skipped) ─
            ("ibm",            "2", "IBM"),
            ("fedex",          "1", "FedEx"),
            ("ups",            "1", "UPS"),
            ("kroger",         "1", "Kroger"),
            ("tjx",            "1", "TJX Companies"),
            ("macys",          "1", "Macy's"),
            ("wellsfargo",     "1", "Wells Fargo"),
            ("usbank",         "1", "U.S. Bank"),
            ("pnc",            "1", "PNC"),
            ("nestle",         "1", "Nestlé"),
            ("loreal",         "1", "L'Oréal"),
            ("kellogg",        "1", "Kellogg's"),
            ("publix",         "1", "Publix"),
            ("disney",         "1", "Disney")
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
            c.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
            return c;
        }

        public async Task<List<ScrapedJob>> FetchJobsAsync()
        {
            var results = new List<ScrapedJob>();

            foreach (var (tenant, portal, company) in Tenants)
            {
                try
                {
                    Console.WriteLine($"[Taleo] {tenant} → starting");
                    var tenantJobs = await FetchTenantAsync(tenant, portal, company);
                    Console.WriteLine($"[Taleo] {tenant} → {tenantJobs.Count} US jobs");
                    results.AddRange(tenantJobs);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Taleo] {tenant} error: {ex.Message}");
                }

                await Task.Delay(1200);
            }

            return results;
        }

        private async Task<List<ScrapedJob>> FetchTenantAsync(string tenant, string portal, string company)
        {
            var jobs = new List<ScrapedJob>();
            var baseSite = $"https://{tenant}.taleo.net";
            var searchUrl = $"{baseSite}/careersection/rest/jobboard/searchjobs?lang=en&portal={portal}";

            int first = 1;
            for (int page = 0; page < MaxPagesPerTenant; page++)
            {
                int last = first + PageSize - 1;

                var payload = new
                {
                    multilineEnabled = false,
                    sortingSelection = new { sortBySelectionParam = "3", ascendingSortingOrder = "false" },
                    fieldData = new { fields = new { }, valid = true },
                    filterSelectionParam = new { searchFilterSelections = Array.Empty<object>() },
                    advancedSearchFiltersSelectionParam = new { searchFilterSelections = Array.Empty<object>() },
                    pageNo = page + 1
                };

                var body = JsonSerializer.Serialize(payload);
                using var req = new HttpRequestMessage(HttpMethod.Post, searchUrl)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                req.Headers.Referrer = new Uri($"{baseSite}/careersection/{portal}/jobsearch.ftl?lang=en");
                req.Headers.TryAddWithoutValidation("Origin", baseSite);
                req.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

                using var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Taleo] {tenant} HTTP {(int)resp.StatusCode} on page {page + 1}");
                    break;
                }

                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("requisitionList", out var listEl) ||
                    listEl.ValueKind != JsonValueKind.Array)
                    break;

                var fields = ExtractFieldOrder(root);
                int pageCount = 0;

                foreach (var row in listEl.EnumerateArray())
                {
                    pageCount++;
                    var values = ExtractRowValues(row, fields);

                    var title    = values.GetValueOrDefault("title");
                    var location = values.GetValueOrDefault("location")
                                ?? values.GetValueOrDefault("primarylocation")
                                ?? "";
                    var contestNo = values.GetValueOrDefault("contestno")
                                  ?? row.GetStringOrNull("contestNo")
                                  ?? row.GetStringOrNull("jobId");
                    var jobId = row.TryGetProperty("jobId", out var jidEl)
                                ? jidEl.ToString()
                                : contestNo;

                    if (!UsLocationFilter.IsUs(location)) continue;

                    var url = !string.IsNullOrEmpty(jobId)
                        ? $"{baseSite}/careersection/{portal}/jobdetail.ftl?job={contestNo ?? jobId}&lang=en"
                        : $"{baseSite}/careersection/{portal}/jobsearch.ftl?lang=en";

                    DateTime? posted = null;
                    var postedRaw = values.GetValueOrDefault("postingdate")
                                 ?? values.GetValueOrDefault("postedDate");
                    if (DateTime.TryParse(postedRaw, out var dt)) posted = dt;

                    jobs.Add(new ScrapedJob
                    {
                        SourceId    = SOURCE_ID,
                        ExternalId  = contestNo ?? jobId,
                        Title       = title,
                        Company     = company,
                        Location    = location,
                        Description = StripHtml(values.GetValueOrDefault("description")
                                              ?? values.GetValueOrDefault("jobdescription")),
                        JobUrl      = url,
                        IsRemote    = location.IndexOf("remote", StringComparison.OrdinalIgnoreCase) >= 0,
                        DatePosted  = posted
                    });
                }

                if (pageCount < PageSize) break; // last page
                first += PageSize;
                await Task.Delay(400);
            }

            return jobs;
        }

        // Taleo's payload looks like:
        // {
        //   "fields": ["title","location","contestno","postingdate", ...],
        //   "requisitionList": [ { "column": ["Engineer","Houston, TX",...], "jobId":123 }, ... ]
        // }
        private static List<string> ExtractFieldOrder(JsonElement root)
        {
            var fields = new List<string>();
            if (root.TryGetProperty("fields", out var fEl) && fEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in fEl.EnumerateArray())
                    fields.Add(f.GetString()?.ToLowerInvariant() ?? "");
            }
            return fields;
        }

        private static Dictionary<string, string> ExtractRowValues(JsonElement row, List<string> fields)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!row.TryGetProperty("column", out var colEl) || colEl.ValueKind != JsonValueKind.Array)
                return dict;

            int i = 0;
            foreach (var v in colEl.EnumerateArray())
            {
                if (i >= fields.Count) break;
                var key = fields[i++];
                if (string.IsNullOrEmpty(key)) continue;
                dict[key] = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
            }
            return dict;
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
