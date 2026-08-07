using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LinkupFeed
{
    internal class CatsOneScraper
    {
        private const int SourceId = 98;
        private const int Workers = 6;
        private const int DetailWorkers = 4;

        private static readonly Regex PortalPathPattern = new Regex(@"/careers/(?<portal>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex JobLinkPattern = new Regex("href=[\"'](?<href>/careers/(?<portal>\\d+)(?:/jobs)?/[^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TitlePattern = new Regex(@"<(?:h1|h2|title)[^>]*>(?<value>.*?)</(?:h1|h2|title)>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex JsonTitlePattern = new Regex(@"""title""\s*:\s*""(?<value>(?:\\.|[^""\\])+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex JsonDescriptionPattern = new Regex(@"""description""\s*:\s*""(?<value>(?:\\.|[^""\\])+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
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
                { "Accept", "text/html, application/json;q=0.9, */*;q=0.8" },
                { "Accept-Language", "en-US,en;q=0.9" },
                { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" }
            }
        };

        public async Task<List<ScrapedJob>> FetchJobsAsync(string inputCsv = null, int? limitSites = null, int maxJobsPerSite = 0)
        {
            inputCsv ??= System.IO.Path.Combine(Environment.CurrentDirectory, "outputs", "catsone_jobs", "catsone_portals_latest.csv");

            var rows = AtsCsv.ReadRows(inputCsv)
                .Where(r => !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "careers_url")) ||
                            !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "domain")))
                .OrderByDescending(JobCount)
                .ToList();

            if (limitSites.HasValue && limitSites.Value > 0) rows = rows.Take(limitSites.Value).ToList();

            Console.WriteLine($"[CATSOne] Loaded {rows.Count} URL rows from {inputCsv}");

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
            var domain = FirstNonEmpty(AtsCsv.Get(row, "domain"), DomainFromUrl(AtsCsv.Get(row, "careers_url")));
            var careersUrl = FirstNonEmpty(AtsCsv.Get(row, "careers_url"), $"https://{domain}/careers");
            if (string.IsNullOrWhiteSpace(domain)) return new List<ScrapedJob>();

            try
            {
                using var response = await _http.GetAsync(careersUrl);
                var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? careersUrl;
                var html = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return new List<ScrapedJob>();

                var links = ExtractJobLinks(finalUrl, html);
                if (maxJobsPerSite > 0) links = links.Take(maxJobsPerSite).ToList();

                var jobs = await FetchDetailsAsync(domain, links);
                Console.WriteLine($"[CATSOne] {domain} candidates={links.Count} jobs={jobs.Count}");
                return jobs;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CATSOne] {domain} error: {ex.Message}");
                return new List<ScrapedJob>();
            }
        }

        private static async Task<List<ScrapedJob>> FetchDetailsAsync(string domain, List<string> urls)
        {
            var jobs = new List<ScrapedJob>();
            using var gate = new SemaphoreSlim(DetailWorkers);
            var tasks = urls.Select(async url =>
            {
                await gate.WaitAsync();
                try { return await FetchDetailAsync(domain, url); }
                finally { gate.Release(); }
            }).ToList();

            foreach (var task in tasks)
            {
                var job = await task;
                if (job != null) jobs.Add(job);
            }

            return jobs;
        }

        private static async Task<ScrapedJob> FetchDetailAsync(string domain, string url)
        {
            try
            {
                using var response = await _http.GetAsync(url);
                var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
                var html = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return null;

                var title = FirstNonEmpty(JsonString(html, JsonTitlePattern), HtmlTitle(html));
                title = Regex.Replace(title, @"\s*\|\s*.*$", "").Trim();
                var description = FirstNonEmpty(StripTags(JsonString(html, JsonDescriptionPattern)), MainText(html));
                var location = LocationFromHtml(html);
                var category = JobCategoryMapper.Normalize("", title, description);
                var remote = IsRemote(title, location, description);

                if (string.IsNullOrWhiteSpace(title)) return null;
                if (!UsLocationFilter.IsUs(location) && !remote) return null;

                return new ScrapedJob
                {
                    SourceId = SourceId,
                    ExternalId = TenantScopedReferenceId(domain, JobIdFromUrl(finalUrl)),
                    Title = title,
                    Company = CompanyFromDomain(domain),
                    Location = string.IsNullOrWhiteSpace(location) && remote ? "Remote" : location,
                    Description = description,
                    JobUrl = finalUrl,
                    IsRemote = remote,
                    DatePosted = null,
                    JobType = JobTypeFromText(description),
                    Category = category
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
            var fingerprint = $"catsone|{tenant}|{raw}".ToLowerInvariant();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint));
            return $"{SourceId}:h{Convert.ToHexString(hash, 0, 8).ToLowerInvariant()}";
        }

        private static string NormalizeReferenceTenant(string domain)
        {
            var value = (domain ?? "").Trim().ToLowerInvariant();
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri)) value = uri.Host;
            value = value.Split('/')[0].Trim();
            return value.StartsWith("www.") ? value.Substring(4) : value;
        }        internal static async Task<CatsOneDiscoveryResult> DiscoverAsync(string domain)
        {
            try
            {
                using var response = await _http.GetAsync($"https://{domain}/careers");
                var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? $"https://{domain}/careers";
                var html = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return null;

                var links = ExtractJobLinks(finalUrl, html);
                if (links.Count == 0 && !PortalPathPattern.IsMatch(finalUrl)) return null;

                return new CatsOneDiscoveryResult
                {
                    CareersUrl = finalUrl,
                    PortalId = PortalPathPattern.Match(finalUrl).Groups["portal"].Value,
                    JobCount = links.Count,
                    SampleJobUrl = links.FirstOrDefault() ?? finalUrl,
                    SampleTitle = links.Count > 0 ? "" : HtmlTitle(html)
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
                var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
                if (href.Equals(new Uri(baseUrl).AbsolutePath, StringComparison.OrdinalIgnoreCase)) continue;

                var url = new Uri(new Uri(baseUrl), href).ToString().Split('#')[0];
                if (seen.Add(url)) links.Add(url);
            }

            return links;
        }

        private static string JsonString(string html, Regex pattern)
        {
            var match = pattern.Match(html ?? "");
            if (!match.Success) return "";

            var value = match.Groups["value"].Value;
            value = Regex.Unescape(value);
            return CleanText(value);
        }

        private static string HtmlTitle(string html)
        {
            var match = TitlePattern.Match(html ?? "");
            return match.Success ? StripTags(match.Groups["value"].Value) : "";
        }

        private static string MainText(string html)
        {
            var text = StripTags(html);
            return text.Length > 5000 ? text.Substring(0, 5000) : text;
        }

        private static string LocationFromHtml(string html)
        {
            var match = Regex.Match(html ?? "", @"""location""\s*:\s*\{(?<body>.*?)\}", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                var body = match.Groups["body"].Value;
                var city = JsonField(body, "city");
                var state = JsonField(body, "state");
                var country = JsonField(body, "country_code");
                var loc = string.Join(", ", new[] { city, state, country }.Where(v => !string.IsNullOrWhiteSpace(v)));
                if (!string.IsNullOrWhiteSpace(loc)) return loc;
            }

            var text = StripTags(html);
            var stateMatch = Regex.Match(text, @"\b([A-Z][A-Za-z .'-]+,\s*(AL|AK|AZ|AR|CA|CO|CT|DE|FL|GA|HI|ID|IL|IN|IA|KS|KY|LA|ME|MD|MA|MI|MN|MS|MO|MT|NE|NV|NH|NJ|NM|NY|NC|ND|OH|OK|OR|PA|RI|SC|SD|TN|TX|UT|VT|VA|WA|WV|WI|WY|DC))\b");
            return stateMatch.Success ? CleanText(stateMatch.Groups[1].Value) : "";
        }

        private static string JsonField(string text, string key)
        {
            var match = Regex.Match(text ?? "", $@"""{Regex.Escape(key)}""\s*:\s*""(?<value>(?:\\.|[^""\\])*)""", RegexOptions.IgnoreCase);
            return match.Success ? CleanText(Regex.Unescape(match.Groups["value"].Value)) : "";
        }

        private static string CompanyFromHtml(string html, string domain)
        {
            var title = HtmlTitle(html);
            var match = Regex.Match(title ?? "", @"\|\s*(?<company>.+)$");
            if (match.Success) return CleanText(match.Groups["company"].Value);

            var og = Regex.Match(html ?? "", @"<meta[^>]+(?:property|name)=[""'](?:og:title|twitter:title)[""'][^>]+content=[""'](?<value>[^""']+)[""']", RegexOptions.IgnoreCase);
            if (og.Success)
            {
                var value = CleanText(og.Groups["value"].Value);
                value = Regex.Replace(value, @"^Careers\s*\|\s*", "", RegexOptions.IgnoreCase).Trim();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }

            return CompanyFromDomain(domain);
        }

        private static string JobIdFromUrl(string url)
        {
            var match = Regex.Match(url ?? "", @"/jobs/(?<id>\d+)(?:-|$|/)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["id"].Value : url;
        }

        private static string JobTypeFromText(string text)
        {
            if (Regex.IsMatch(text ?? "", @"\bcontract\b", RegexOptions.IgnoreCase)) return "Contract";
            if (Regex.IsMatch(text ?? "", @"\bpart[- ]time\b", RegexOptions.IgnoreCase)) return "Part Time";
            if (Regex.IsMatch(text ?? "", @"\bfull[- ]time\b", RegexOptions.IgnoreCase)) return "Full Time";
            return "";
        }

        private static bool IsRemote(params string[] values)
        {
            var text = string.Join(" ", values.Where(v => !string.IsNullOrWhiteSpace(v))).ToLowerInvariant();
            return text.Contains("remote") || text.Contains("work from home") || text.Contains("virtual");
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

        internal sealed class CatsOneDiscoveryResult
        {
            public string CareersUrl { get; set; }
            public string PortalId { get; set; }
            public int JobCount { get; set; }
            public string SampleJobUrl { get; set; }
            public string SampleTitle { get; set; }
        }
    }
}
