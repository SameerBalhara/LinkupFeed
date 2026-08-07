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
    internal class BreezyHrScraper
    {
        private const int SourceId = 98;
        private const int Workers = 2;
        private const int DetailWorkers = 2;
        private static readonly TimeSpan RequestSpacing = TimeSpan.FromMilliseconds(175);

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
                { "Accept", "application/json, text/html;q=0.9, */*;q=0.8" },
                { "Accept-Language", "en-US,en;q=0.9" },
                { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" }
            }
        };

        public async Task<List<ScrapedJob>> FetchJobsAsync(string inputCsv = null, int? limitSites = null, int maxJobsPerSite = 0)
        {
            inputCsv ??= System.IO.Path.Combine(Environment.CurrentDirectory, "outputs", "breezyhr_jobs", "breezyhr_link_counts_latest.csv");

            var rows = AtsCsv.ReadRows(inputCsv)
                .Where(r => !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "domain")) ||
                            !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "json_url")))
                .OrderByDescending(JobCount)
                .ToList();

            if (limitSites.HasValue && limitSites.Value > 0) rows = rows.Take(limitSites.Value).ToList();

            Console.WriteLine($"[BreezyHR] Loaded {rows.Count} URL rows from {inputCsv}");

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
            var domain = FirstNonEmpty(AtsCsv.Get(row, "domain"), DomainFromUrl(AtsCsv.Get(row, "json_url")));
            var jsonUrl = FirstNonEmpty(AtsCsv.Get(row, "json_url"), $"https://{domain}/json");
            if (string.IsNullOrWhiteSpace(domain)) return new List<ScrapedJob>();

            try
            {
                var listJson = await GetStringWithRetryAsync(jsonUrl);
                var listed = ParseList(domain, listJson);
                var summaries = listed
                    .Where(s => UsLocationFilter.IsUs(s.Location) || s.IsRemote)
                    .ToList();
                if (maxJobsPerSite > 0) summaries = summaries.Take(maxJobsPerSite).ToList();

                var jobs = await FetchDetailsAsync(summaries);
                Console.WriteLine($"[BreezyHR] {domain} listed={listed.Count} candidates={summaries.Count} details={summaries.Count} jobs={jobs.Count}");
                return jobs;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BreezyHR] {domain} error: {ex.Message}");
                return new List<ScrapedJob>();
            }
        }

        private static List<BreezyJobSummary> ParseList(string domain, string json)
        {
            var summaries = new List<BreezyJobSummary>();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return summaries;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var id = GetString(item, "id");
                var title = CleanText(GetString(item, "name"));
                var url = GetString(item, "url");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title)) continue;

                var location = LocationFromElement(item);
                var remote = IsRemote(
                    title,
                    location,
                    GetString(item, "remote_details"),
                    GetString(item, "location"),
                    GetString(item, "locations"));

                summaries.Add(new BreezyJobSummary
                {
                    Id = id,
                    Title = title,
                    Company = CompanyFromElement(item, domain),
                    Location = location,
                    IsRemote = remote,
                    JobUrl = FirstNonEmpty(url, $"https://{domain}/p/{GetString(item, "friendly_id")}", $"https://{domain}/p/{id}"),
                    DatePosted = ParseDate(GetString(item, "published_date")),
                    JobType = TypeNameFromElement(item),
                    Category = CleanText(GetString(item, "department"))
                });
            }

            return summaries;
        }

        private static async Task<List<ScrapedJob>> FetchDetailsAsync(List<BreezyJobSummary> summaries)
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

        private static async Task<ScrapedJob> FetchDetailAsync(BreezyJobSummary summary)
        {
            try
            {
                var html = await GetStringWithRetryAsync(summary.JobUrl);
                var finalUrl = summary.JobUrl;
                var data = JsonLdJobPosting(html);

                var title = FirstNonEmpty(GetJsonString(data, "title"), summary.Title);
                var description = StripTags(GetJsonString(data, "description"));
                if (string.IsNullOrWhiteSpace(description)) description = ExtractMeta(html, "description");

                var category = FirstNonEmpty(
                    summary.Category,
                    GetJsonString(data, "occupationalCategory"),
                    GetJsonString(data, "industry"),
                    InferCategory(title, description));
                var jobType = FirstNonEmpty(EmploymentType(data), summary.JobType);
                var datePosted = ParseDate(GetJsonString(data, "datePosted")) ?? summary.DatePosted;
                var location = FirstNonEmpty(LocationFromJsonLd(data), summary.Location);
                var remote = summary.IsRemote || IsRemote(GetJsonString(data, "jobLocationType"), location, title, description);

                return MapJob(summary, finalUrl, description, category, datePosted, jobType, location, title, remote);
            }
            catch
            {
                return MapJob(summary, summary.JobUrl, "", summary.Category, summary.DatePosted, summary.JobType, summary.Location);
            }
        }

        private static async Task<string> GetStringWithRetryAsync(string url)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (attempt == 0)
                {
                    await Task.Delay(RequestSpacing);
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
                }

                using var response = await _http.GetAsync(url);
                var text = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode) return text;

                if ((response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429) && attempt < 2)
                {
                    continue;
                }

                response.EnsureSuccessStatusCode();
            }

            return "";
        }

        private static ScrapedJob MapJob(
            BreezyJobSummary summary,
            string finalUrl,
            string description,
            string category,
            DateTime? datePosted,
            string jobType,
            string location,
            string title = null,
            bool? remote = null)
        {
            title = FirstNonEmpty(title, summary.Title);
            location = FirstNonEmpty(location, summary.Location);
            var isRemote = remote ?? summary.IsRemote;

            if (string.IsNullOrWhiteSpace(title)) return null;
            if (!UsLocationFilter.IsUs(location) && !isRemote) return null;

            return new ScrapedJob
            {
                SourceId = SourceId,
                ExternalId = TenantScopedReferenceId(DomainFromUrl(summary.JobUrl), summary.Id),
                Title = title,
                Company = summary.Company,
                Location = string.IsNullOrWhiteSpace(location) && isRemote ? "Remote" : location,
                Description = description,
                JobUrl = finalUrl,
                IsRemote = isRemote,
                DatePosted = datePosted ?? summary.DatePosted,
                JobType = FirstNonEmpty(jobType, summary.JobType),
                Category = FirstNonEmpty(category, summary.Category)
            };
        }


        private static string TenantScopedReferenceId(string domain, string id)
        {
            var tenant = NormalizeReferenceTenant(domain);
            var raw = string.IsNullOrWhiteSpace(id) ? "" : id.Trim();
            var fingerprint = $"breezyhr|{tenant}|{raw}".ToLowerInvariant();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint));
            return $"{SourceId}:h{Convert.ToHexString(hash, 0, 8).ToLowerInvariant()}";
        }

        private static string NormalizeReferenceTenant(string domain)
        {
            var value = (domain ?? "").Trim().ToLowerInvariant();
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri)) value = uri.Host;
            value = value.Split('/')[0].Trim();
            return value.StartsWith("www.") ? value.Substring(4) : value;
        }        private static Dictionary<string, JsonElement> JsonLdJobPosting(string html)
        {
            foreach (Match match in Regex.Matches(html ?? "", "<script[^>]+type=[\"']application/ld\\+json[\"'][^>]*>(?<json>.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                try
                {
                    using var doc = JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups["json"].Value).Trim());
                    foreach (var item in EnumerateJsonLdItems(doc.RootElement))
                    {
                        if (IsJobPosting(item))
                        {
                            return item.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
                        }
                    }
                }
                catch { }
            }

            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<JsonElement> EnumerateJsonLdItems(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray()) yield return item;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in graph.EnumerateArray()) yield return item;
                }
                else
                {
                    yield return root;
                }
            }
        }

        private static bool IsJobPosting(JsonElement item)
        {
            if (!item.TryGetProperty("@type", out var type)) return false;
            if (type.ValueKind == JsonValueKind.String) return string.Equals(type.GetString(), "JobPosting", StringComparison.OrdinalIgnoreCase);
            if (type.ValueKind == JsonValueKind.Array) return type.EnumerateArray().Any(v => string.Equals(v.GetString(), "JobPosting", StringComparison.OrdinalIgnoreCase));
            return false;
        }

        private static string LocationFromElement(JsonElement item)
        {
            if (item.TryGetProperty("locations", out var locations) && locations.ValueKind == JsonValueKind.Array)
            {
                var parts = locations.EnumerateArray()
                    .Select(LocationText)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(4)
                    .ToList();
                if (parts.Count > 0) return string.Join(" | ", parts);
            }

            if (item.TryGetProperty("location", out var location))
            {
                return LocationText(location);
            }

            return "";
        }

        private static string LocationText(JsonElement location)
        {
            if (location.ValueKind == JsonValueKind.String) return CleanText(location.GetString());
            if (location.ValueKind != JsonValueKind.Object) return "";

            var explicitName = CleanText(GetString(location, "name"));
            if (!string.IsNullOrWhiteSpace(explicitName)) return explicitName;

            var country = "";
            if (location.TryGetProperty("country", out var countryEl) && countryEl.ValueKind == JsonValueKind.Object)
            {
                country = FirstNonEmpty(GetString(countryEl, "name"), GetString(countryEl, "id"));
            }

            var state = "";
            if (location.TryGetProperty("state", out var stateEl) && stateEl.ValueKind == JsonValueKind.Object)
            {
                state = FirstNonEmpty(GetString(stateEl, "name"), GetString(stateEl, "id"));
            }

            return string.Join(", ", new[]
            {
                GetString(location, "city"),
                state,
                country
            }.Where(v => !string.IsNullOrWhiteSpace(v)));
        }

        private static string LocationFromJsonLd(Dictionary<string, JsonElement> data)
        {
            if (!data.TryGetValue("jobLocation", out var locations) &&
                !data.TryGetValue("applicantLocationRequirements", out locations))
            {
                return "";
            }

            var items = locations.ValueKind == JsonValueKind.Array
                ? locations.EnumerateArray().ToList()
                : new List<JsonElement> { locations };
            var parts = new List<string>();

            foreach (var item in items)
            {
                var address = item;
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("address", out var nested)) address = nested;

                var text = address.ValueKind == JsonValueKind.Object
                    ? string.Join(", ", new[]
                    {
                        GetPropertyString(address, "addressLocality"),
                        GetPropertyString(address, "addressRegion"),
                        GetPropertyString(address, "addressCountry"),
                        GetPropertyString(address, "name")
                    }.Where(v => !string.IsNullOrWhiteSpace(v)))
                    : address.ToString();

                if (!string.IsNullOrWhiteSpace(text)) parts.Add(text);
            }

            return string.Join(" | ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private static string TypeNameFromElement(JsonElement item)
        {
            if (!item.TryGetProperty("type", out var type)) return "";
            if (type.ValueKind == JsonValueKind.Object) return CleanText(FirstNonEmpty(GetString(type, "name"), GetString(type, "id")));
            return CleanText(type.ToString());
        }

        private static string CompanyFromElement(JsonElement item, string domain)
        {
            if (item.TryGetProperty("company", out var company) && company.ValueKind == JsonValueKind.Object)
            {
                var name = CleanText(GetString(company, "name"));
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }

            return CompanyFromDomain(domain);
        }

        private static string EmploymentType(Dictionary<string, JsonElement> data)
        {
            if (!data.TryGetValue("employmentType", out var value)) return "";
            if (value.ValueKind == JsonValueKind.Array) return string.Join(", ", value.EnumerateArray().Select(v => v.ToString()));
            return value.ToString();
        }

        private static string ExtractMeta(string html, string name)
        {
            var patterns = new[]
            {
                $"<meta[^>]+property=[\"']{Regex.Escape(name)}[\"'][^>]+content=[\"'](?<value>[^\"']*)[\"']",
                $"<meta[^>]+name=[\"']{Regex.Escape(name)}[\"'][^>]+content=[\"'](?<value>[^\"']*)[\"']",
                $"<meta[^>]+content=[\"'](?<value>[^\"']*)[\"'][^>]+(?:property|name)=[\"']{Regex.Escape(name)}[\"']"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(html ?? "", pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (match.Success) return CleanText(match.Groups["value"].Value);
            }

            return "";
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
            if (Regex.IsMatch(text, @"\b(scrum|project manager|program manager|product manager|business analyst)\b", RegexOptions.IgnoreCase)) return "Product / Project Management";
            if (Regex.IsMatch(text, @"\b(software|developer|engineer|programmer|full stack|frontend|backend|java|python|\.net|c#)\b", RegexOptions.IgnoreCase)) return "Software Engineering";

            return "";
        }

        private static bool IsRemote(params string[] values)
        {
            var text = string.Join(" ", values.Where(v => !string.IsNullOrWhiteSpace(v))).ToLowerInvariant();
            return text.Contains("remote") ||
                   text.Contains("telecommute") ||
                   text.Contains("work from home") ||
                   text.Contains("virtual");
        }

        private static string GetJsonString(Dictionary<string, JsonElement> data, string key)
        {
            if (!data.TryGetValue(key, out var value)) return "";
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
        }

        private static string GetPropertyString(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value)) return "";
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
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

        private sealed class BreezyJobSummary
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Company { get; set; }
            public string Location { get; set; }
            public bool IsRemote { get; set; }
            public string JobUrl { get; set; }
            public DateTime? DatePosted { get; set; }
            public string JobType { get; set; }
            public string Category { get; set; }
        }
    }
}
