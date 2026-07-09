using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LinkupFeed
{
    internal class WorkdayScraper
    {
        private const int SourceId = 99;
        private const int Workers = 6;
        private const int PageSize = 20;

        private static readonly HttpClient _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
            DefaultRequestHeaders =
            {
                { "Accept", "application/json, text/html;q=0.9, */*;q=0.8" },
                { "Accept-Language", "en-US,en;q=0.9" },
                { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" }
            }
        };

        public async Task<List<ScrapedJob>> FetchJobsAsync(string inputCsv = null, int? limitSites = null, int maxPages = 0)
        {
            inputCsv ??= PathFor("outputs", "workday_discovery", "workday_discovery_valid_latest.csv");
            var rows = AtsCsv.ReadRows(inputCsv)
                .Where(r => string.Equals(AtsCsv.Get(r, "validated"), "true", StringComparison.OrdinalIgnoreCase))
                .Where(r => !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "api_url")))
                .ToList();

            if (limitSites.HasValue && limitSites.Value > 0) rows = rows.Take(limitSites.Value).ToList();

            Console.WriteLine($"[Workday] Loaded {rows.Count} URL rows from {inputCsv}");

            var allJobs = new List<ScrapedJob>();
            var gate = new SemaphoreSlim(Workers);
            var tasks = rows.Select(async row =>
            {
                await gate.WaitAsync();
                try { return await FetchSiteAsync(row, maxPages); }
                finally { gate.Release(); }
            }).ToList();

            foreach (var task in tasks) allJobs.AddRange(await task);
            return Dedupe(allJobs);
        }

        private static async Task<List<ScrapedJob>> FetchSiteAsync(Dictionary<string, string> row, int maxPages)
        {
            var apiUrl = AtsCsv.Get(row, "api_url");
            var domain = AtsCsv.Get(row, "domain");
            var site = AtsCsv.Get(row, "site");
            var referer = AtsCsv.Get(row, "careers_url");
            if (string.IsNullOrWhiteSpace(referer)) referer = $"https://{domain}/en-US/{site}";

            var jobs = new List<ScrapedJob>();
            int offset = 0;
            int total = int.MaxValue;
            int pages = 0;

            try
            {
                while (offset < total)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                    request.Headers.Referrer = new Uri(referer);
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(new { appliedFacets = new { }, limit = PageSize, offset, searchText = "" }),
                        Encoding.UTF8,
                        "application/json");

                    using var response = await _http.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[Workday] {domain} HTTP {(int)response.StatusCode}");
                        break;
                    }

                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    var root = doc.RootElement;
                    total = GetInt(root, "total") ?? 0;

                    if (!root.TryGetProperty("jobPostings", out var postings) ||
                        postings.ValueKind != JsonValueKind.Array ||
                        postings.GetArrayLength() == 0)
                    {
                        break;
                    }

                    foreach (var posting in postings.EnumerateArray())
                    {
                        var job = MapJob(row, posting);
                        if (job != null)
                        {
                            if (ItJobFilter.IsIt(job.Title, job.Category))
                            {
                                job.Description = await FetchDescriptionAsync(row, GetText(posting, "externalPath") ?? "");
                            }

                            jobs.Add(job);
                        }
                    }

                    pages++;
                    offset += PageSize;
                    if (maxPages > 0 && pages >= maxPages) break;
                    await Task.Delay(50);
                }

                var described = jobs.Count(j => !string.IsNullOrWhiteSpace(j.Description));
                Console.WriteLine($"[Workday] {domain} -> {jobs.Count} US/remote jobs ({described} with descriptions)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Workday] {domain} error: {ex.Message}");
            }

            return jobs;
        }

        private static ScrapedJob MapJob(Dictionary<string, string> row, JsonElement job)
        {
            var title = GetText(job, "title");
            if (string.IsNullOrWhiteSpace(title)) return null;

            var location = GetText(job, "locationsText") ?? GetText(job, "location") ?? "";
            var category = CategoryFromJob(job);
            var remote = IsRemote(location, job);
            if (!UsLocationFilter.IsUs(location) && !remote) return null;

            var externalPath = GetText(job, "externalPath") ?? "";
            return new ScrapedJob
            {
                SourceId = SourceId,
                ExternalId = WorkdayReferenceId(job, externalPath, category),
                Title = title,
                Company = CompanyName(row),
                Location = string.IsNullOrWhiteSpace(location) && remote ? "Remote" : location,
                Description = "",
                JobUrl = JobUrl(row, externalPath),
                IsRemote = remote,
                DatePosted = ParseDate(GetText(job, "postedOn")),
                JobType = GetText(job, "timeType") ?? GetText(job, "employmentType"),
                Category = category
            };
        }

        private static async Task<string> FetchDescriptionAsync(Dictionary<string, string> row, string externalPath)
        {
            if (string.IsNullOrWhiteSpace(externalPath)) return "";

            try
            {
                var detailUrl = DetailApiUrl(row, externalPath);
                using var request = new HttpRequestMessage(HttpMethod.Get, detailUrl);
                request.Headers.Referrer = new Uri(JobUrl(row, externalPath));

                using var response = await _http.SendAsync(request);
                if (!response.IsSuccessStatusCode) return "";

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = doc.RootElement;

                if (root.TryGetProperty("jobPostingInfo", out var info))
                {
                    var description = GetText(info, "jobDescription");
                    if (!string.IsNullOrWhiteSpace(description)) return CleanDescription(description);
                }

                var rootDescription = GetText(root, "jobDescription");
                return string.IsNullOrWhiteSpace(rootDescription) ? "" : CleanDescription(rootDescription);
            }
            catch
            {
                return "";
            }
        }

        private static string DetailApiUrl(Dictionary<string, string> row, string externalPath)
        {
            var domain = AtsCsv.Get(row, "domain");
            var tenant = AtsCsv.Get(row, "tenant");
            var site = AtsCsv.Get(row, "site");
            var path = (externalPath ?? "").Trim();

            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                path = new Uri(path).AbsolutePath;
            }

            path = path.TrimStart('/');
            if (path.StartsWith("job/", StringComparison.OrdinalIgnoreCase)) path = path.Substring(4);

            return $"https://{domain}/wday/cxs/{tenant}/{site}/job/{path}";
        }

        private static string CompanyName(Dictionary<string, string> row)
        {
            var tenant = AtsCsv.Get(row, "tenant");
            if (string.IsNullOrWhiteSpace(tenant)) tenant = AtsCsv.Get(row, "domain").Split('.').FirstOrDefault() ?? "";
            return Regex.Replace(tenant, "[_-]+", " ").Trim().ToUpperFirst();
        }

        private static string JobUrl(Dictionary<string, string> row, string externalPath)
        {
            var domain = AtsCsv.Get(row, "domain");
            var site = AtsCsv.Get(row, "site");
            var path = externalPath ?? "";
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return path;
            if (path.StartsWith("/")) return $"https://{domain}/en-US/{site}{path}";
            return $"https://{domain}/en-US/{site}/job/{path}";
        }

        private static string CategoryFromJob(JsonElement job)
        {
            foreach (var key in new[] { "jobFamily", "jobFamilyGroup", "jobProfile", "workerSubType", "employmentType" })
            {
                var value = GetText(job, key);
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }

            if (job.TryGetProperty("bulletFields", out var bullets) && bullets.ValueKind == JsonValueKind.Array)
            {
                var text = string.Join(" | ", bullets.EnumerateArray().Select(v => v.ToString()).Where(v => !string.IsNullOrWhiteSpace(v)));
                return text.Length > 250 ? text.Substring(0, 250) : text;
            }

            return "";
        }

        private static bool IsRemote(string location, JsonElement job)
        {
            var text = string.Join(" ", new[]
            {
                location,
                GetText(job, "workplaceType"),
                GetText(job, "remoteType"),
                GetText(job, "jobType")
            }).ToLowerInvariant();

            return text.Contains("remote") || text.Contains("work from home") || text.Contains("virtual");
        }

        private static string WorkdayReferenceId(JsonElement job, string externalPath, string category)
        {
            foreach (var candidate in new[] { GetText(job, "jobReqId"), category, ReferenceIdFromPath(externalPath), ReferenceIdFromText(category) })
            {
                if (LooksLikeReferenceId(candidate)) return candidate.Trim();
            }

            return externalPath;
        }

        private static bool LooksLikeReferenceId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var text = value.Trim();
            return text.Length <= 80 && !text.Contains(" ") && !text.Contains("/") && Regex.IsMatch(text, @"\d");
        }

        private static string ReferenceIdFromPath(string externalPath)
        {
            var leaf = (externalPath ?? "").Trim().TrimEnd('/').Split('/').LastOrDefault() ?? "";
            var match = Regex.Match(leaf, @"_([A-Za-z]{1,12}[_-]?\d[\w-]*|\d[\w-]*)$");
            return match.Success ? match.Groups[1].Value : "";
        }

        private static string ReferenceIdFromText(string value)
        {
            var match = Regex.Match(value ?? "", @"\b([A-Za-z]{1,12}[_-]?\d[\w-]*|\d{3,}[\w-]*)\b");
            return match.Success ? match.Groups[1].Value : "";
        }

        private static DateTime? ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var text = value.Trim();
            if (text.Equals("Posted Today", StringComparison.OrdinalIgnoreCase) || text.Equals("Today", StringComparison.OrdinalIgnoreCase)) return DateTime.Today;
            if (text.Equals("Posted Yesterday", StringComparison.OrdinalIgnoreCase) || text.Equals("Yesterday", StringComparison.OrdinalIgnoreCase)) return DateTime.Today.AddDays(-1);
            var dayMatch = Regex.Match(text, @"Posted\s+(\d+)\s+Days?\s+Ago", RegexOptions.IgnoreCase);
            if (dayMatch.Success && int.TryParse(dayMatch.Groups[1].Value, out var days)) return DateTime.Today.AddDays(-days);
            return DateTime.TryParse(text, out var parsed) ? parsed : null;
        }

        private static string GetText(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value)) return null;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }

        private static int? GetInt(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value)) return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
            return int.TryParse(value.ToString(), out number) ? number : null;
        }

        private static string PathFor(params string[] parts)
        {
            return System.IO.Path.Combine(new[] { Environment.CurrentDirectory }.Concat(parts).ToArray());
        }

        private static string CleanDescription(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var text = WebUtility.HtmlDecode(Regex.Replace(value, "<[^>]+>", " "));
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private static List<ScrapedJob> Dedupe(IEnumerable<ScrapedJob> jobs)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<ScrapedJob>();
            foreach (var job in jobs)
            {
                var key = !string.IsNullOrWhiteSpace(job.JobUrl)
                    ? job.JobUrl.Trim()
                    : !string.IsNullOrWhiteSpace(job.ExternalId)
                        ? job.ExternalId.Trim()
                        : $"{job.Title}|{job.Company}|{job.Location}";
                if (seen.Add(key)) results.Add(job);
            }
            return results;
        }
    }
}
