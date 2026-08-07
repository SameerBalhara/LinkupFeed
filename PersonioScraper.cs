using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace LinkupFeed
{
    internal class PersonioScraper
    {
        private const int SourceId = 93;
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
                { "Accept", "application/xml, text/xml;q=0.9, */*;q=0.8" },
                { "Accept-Language", "en-US,en;q=0.9" },
                { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" }
            }
        };

        public async Task<List<ScrapedJob>> FetchJobsAsync(string inputCsv = null, int? limitSites = null, int maxJobsPerSite = 0)
        {
            inputCsv ??= System.IO.Path.Combine(Environment.CurrentDirectory, "outputs", "personio_jobs", "personio_xml_feeds_latest.csv");

            var rows = AtsCsv.ReadRows(inputCsv)
                .Where(r => !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "xml_url")) ||
                            !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "domain")))
                .OrderByDescending(JobCount)
                .ToList();

            if (limitSites.HasValue && limitSites.Value > 0) rows = rows.Take(limitSites.Value).ToList();

            Console.WriteLine($"[Personio] Loaded {rows.Count} URL rows from {inputCsv}");

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
            var domain = FirstNonEmpty(AtsCsv.Get(row, "domain"), DomainFromUrl(AtsCsv.Get(row, "xml_url")));
            var xmlUrl = FirstNonEmpty(AtsCsv.Get(row, "xml_url"), $"https://{domain}/xml");
            if (string.IsNullOrWhiteSpace(domain)) return new List<ScrapedJob>();

            try
            {
                var xml = await _http.GetStringAsync(xmlUrl);
                var doc = XDocument.Parse(xml);
                var positions = doc.Descendants("position").ToList();
                if (maxJobsPerSite > 0) positions = positions.Take(maxJobsPerSite).ToList();

                var jobs = positions
                    .Select(position => MapJob(domain, position))
                    .Where(job => job != null)
                    .ToList();

                Console.WriteLine($"[Personio] {domain} listed={positions.Count} jobs={jobs.Count}");
                return jobs;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Personio] {domain} error: {ex.Message}");
                return new List<ScrapedJob>();
            }
        }

        private static ScrapedJob MapJob(string domain, XElement position)
        {
            var id = ElementText(position, "id");
            var title = CleanText(ElementText(position, "name"));
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title)) return null;

            var location = LocationFromPosition(position);
            var description = DescriptionFromPosition(position);
            var category = FirstNonEmpty(
                CleanText(ElementText(position, "department")),
                CleanText(ElementText(position, "recruitingCategory")),
                CleanText(ElementText(position, "occupationCategory")));
            var jobType = FirstNonEmpty(
                CleanText(ElementText(position, "schedule")),
                CleanText(ElementText(position, "employmentType")));
            var remote = IsRemote(location, title, CleanText(ElementText(position, "keywords")));

            if (!UsLocationFilter.IsUs(location) && !remote) return null;

            return new ScrapedJob
            {
                SourceId = SourceId,
                ExternalId = $"{SourceId}:{id}",
                Title = title,
                Company = FirstNonEmpty(CleanText(ElementText(position, "subcompany")), CompanyFromDomain(domain)),
                Location = string.IsNullOrWhiteSpace(location) && remote ? "Remote" : location,
                Description = description,
                JobUrl = $"https://{domain}/job/{id}",
                IsRemote = remote,
                DatePosted = ParseDate(FirstNonEmpty(ElementText(position, "publishedAt"), ElementText(position, "createdAt"))),
                JobType = jobType,
                Category = category
            };
        }

        private static string LocationFromPosition(XElement position)
        {
            var parts = new List<string>
            {
                CleanText(ElementText(position, "office")),
                CleanText(ElementText(position, "additionalOffices"))
            };

            return string.Join(" | ", parts.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private static string DescriptionFromPosition(XElement position)
        {
            var parts = new List<string>();
            foreach (var description in position.Descendants("jobDescription"))
            {
                var name = CleanText(ElementText(description, "name"));
                var value = StripTags(ElementText(description, "value"));
                if (string.IsNullOrWhiteSpace(value)) continue;

                parts.Add(string.IsNullOrWhiteSpace(name) ? value : $"{name}: {value}");
            }

            return string.Join(" ", parts);
        }

        private static bool IsRemote(params string[] values)
        {
            var text = string.Join(" ", values.Where(v => !string.IsNullOrWhiteSpace(v))).ToLowerInvariant();
            if (!Regex.IsMatch(text, @"\b(remote|home office|work from home|virtual)\b", RegexOptions.IgnoreCase)) return false;
            return !HasExplicitNonUsCountry(text) || text.Contains("united states") || Regex.IsMatch(text, @"\busa?\b", RegexOptions.IgnoreCase);
        }

        private static bool HasExplicitNonUsCountry(string text)
        {
            return new[]
            {
                "china", "germany", "latvia", "poland", "canada", "united kingdom", "india", "france",
                "spain", "italy", "netherlands", "sweden", "australia", "brazil", "mexico", "singapore"
            }.Any(country => text.Contains(country));
        }

        private static DateTime? ParseDate(string value)
        {
            return DateTime.TryParse(value, out var parsed) ? parsed : null;
        }

        private static string ElementText(XElement element, string name)
        {
            return element?.Element(name)?.Value?.Trim() ?? "";
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
