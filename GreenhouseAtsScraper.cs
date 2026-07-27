using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

namespace LinkupFeed
{

    internal class GreenhouseAtsScraper
    {
        private const int SOURCE_ID = 54;
        private static readonly int[] DelayMs = { 800, 1600, 3200 };

        // NOTE: Greenhouse board tokens are not always the same as the company name.
        // Companies that left Greenhouse (now on Workday/Lever/Ashby/custom) were removed.
        // Verified live via https://boards-api.greenhouse.io/v1/boards/{token}/jobs
        private static readonly string[] Companies = {
        // Big Tech / Cloud
        "anthropic", "stripe", "discord", "twilio",
        "hubspot", "asana",
        "dropbox", "boxinc", "okta", "cloudflare",
        "datadog", "newrelic", "pagerduty",

        // Dev Tools / Infrastructure
        "gitlab", "circleci",
        "postman", "vercel",
        "planetscale",

        // Fintech
        "brex", "carta",
        "chime", "robinhood", "affirm", "marqeta",

        // Data / AI
        "scaleai",
        "deepmind", "togetherai",

        // Enterprise SaaS
        "lattice", "figma",
        "airtable", "calendly",
        "intercom", "mixpanel",

        // E-commerce / Marketplace
        "instacart",
        "lyft", "airbnb",

        // Healthcare / Other Tech
        "oscar",
        "duolingo", "coursera", "udemy",

        // ── Added batch: more IT-heavy Greenhouse boards ──────────────────────
        // Tokens verified live via https://boards-api.greenhouse.io/v1/boards/{token}/jobs;
        // dead tokens 404 and are skipped, so this list is safe to grow.
        // Data / AI / infra
        "databricks", "mongodb", "datarobot", "elastic", "confluent",
        "snyk", "retool", "webflow", "grammarly",
        // Fintech
        "coinbase", "gemini", "sofi", "wealthfront", "betterment",
        "nerdwallet", "plaid",
        // Marketplaces / consumer
        "reddit", "pinterest", "doordash", "faire", "thumbtack",
        "samsara", "roblox", "squarespace", "peloton", "wayfair",
        "compass", "vimeo", "flexport", "gopuff", "opendoor",
        // Enterprise SaaS / dev tools
        "applovin", "checkr", "benchling", "pendo", "braze"
    };

        // ── IT title filter ──────────────────────────────────────────────────────
        private static readonly HashSet<string> ItTitleKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Engineering
        "software", "engineer", "developer", "programmer",
        // Data / AI
        "data", "analyst", "analytics", "machine learning", "ml", "ai",
        "data scientist", "data engineer", "business intelligence",
        // Infrastructure
        "devops", "sre", "cloud", "infrastructure", "platform",
        "kubernetes", "terraform", "aws", "azure", "gcp",
        // Security
        "security", "cybersecurity", "appsec", "infosec", "penetration",
        // Architecture / Leadership
        "architect", "tech lead", "technical lead", "engineering manager",
        "vp engineering", "director of engineering", "cto",
        // QA
        "qa", "quality assurance", "automation", "sdet", "tester",
        // Product / Program
        "product manager", "technical program",
        // Web / Mobile
        "frontend", "front-end", "backend", "back-end", "full stack",
        "fullstack", "mobile", "ios", "android",
        // IT Ops
        "database", "dba", "sql", "network", "systems",
        "it support", "helpdesk", "it manager", "erp", "sap"
    };

      

        // ── Location helpers ─────────────────────────────────────────────────────
        private static readonly string[] UsaCityFragments = {
        "new york", "san francisco", "seattle", "chicago", "austin", "boston",
        "los angeles", "denver", "atlanta", "miami", "dallas", "washington, dc"
    };

        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
            DefaultRequestHeaders =
    {
        { "User-Agent", "ITJobCafe-Scraper/1.0" }
    }
        };

        private static readonly Regex UsStatePattern =
    new Regex(@",\s*(AL|AK|AZ|AR|CA|CO|CT|DE|FL|GA|HI|ID|IL|IN|IA|KS|KY|LA|ME|MD|MA|MI|MN|MS|MO|MT|NE|NV|NH|NJ|NM|NY|NC|ND|OH|OK|OR|PA|RI|SC|SD|TN|TX|UT|VT|VA|WA|WV|WI|WY)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex HtmlStripPattern =
            new Regex(@"<[^>]+>", RegexOptions.Compiled);

        private static readonly Regex MultiSpacePattern =
            new Regex(@"\s{2,}", RegexOptions.Compiled);

        //private static readonly Regex UsStatePattern =
        //    new(@",\s*(AL|AK|AZ|AR|CA|CO|CT|DE|FL|GA|HI|ID|IL|IN|IA|KS|KY|LA|ME|MD|MA|MI|MN|MS|MO|MT|NE|NV|NH|NJ|NM|NY|NC|ND|OH|OK|OR|PA|RI|SC|SD|TN|TX|UT|VT|VA|WA|WV|WI|WY)\b",
        //        RegexOptions.IgnoreCase | RegexOptions.Compiled);

        //private static readonly Regex HtmlStripPattern =
        //    new(@"<[^>]+>", RegexOptions.Compiled);

        // ── Main fetch (parallel with concurrency cap) ───────────────────────────
        public async Task<List<ScrapedJob>> FetchJobsAsync()
        {
            var results = new List<ScrapedJob>();

            foreach (var company in Companies)
            {
                try
                {
                    var url = $"https://boards-api.greenhouse.io/v1/boards/{company}/jobs?content=true";
                    var json = await Http.GetStringAsync(url);
                    var root = JsonDocument.Parse(json).RootElement;

                    if (!root.TryGetProperty("jobs", out var jobsEl)) continue;

                    foreach (var j in jobsEl.EnumerateArray())
                    {
                        // Location filter — only USA
                        var location = j.TryGetProperty("location", out var locEl)
                            ? locEl.GetStringOrNull("name") ?? "" : "";
                        if (!IsUsaLocation(location)) continue;

                        var description = StripHtml(j.GetStringOrNull("content"));

                        results.Add(new ScrapedJob
                        {
                            SourceId = SOURCE_ID,
                            ExternalId = j.TryGetProperty("id", out var idEl)
                                            ? idEl.GetInt64().ToString() : null,
                            Title = j.GetStringOrNull("title"),
                            Company = company.ToUpperFirst(),
                            Location = location,
                            Description = description,
                            JobUrl = j.GetStringOrNull("absolute_url"),
                            IsRemote = location.Contains("Remote", StringComparison.OrdinalIgnoreCase),
                            DatePosted = j.TryGetProperty("updated_at", out var ua)
                                            ? DateTime.TryParse(ua.GetString(), out var dt) ? dt : (DateTime?)null
                                            : null,
                            JobType = ExtractJobType(j, description),
                            Category = ExtractCategory(j)
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Greenhouse] {company} error: {ex.Message}");
                }

                await Task.Delay(800);
            }

            return results;
        }

        // ── Retry with backoff ───────────────────────────────────────────────────
        //private async Task<string?> FetchWithRetryAsync(string url)
        //{
        //    for (int i = 0; i < DelayMs.Length; i++)
        //    {
        //        try
        //        {
        //            return await Http.GetStringAsync(url);
        //        }
        //        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        //        {
        //            if (i == DelayMs.Length - 1) throw;
        //            await Task.Delay(DelayMs[i]);
        //        }
        //    }
        //    return null;
        //}

        // ── Helpers ──────────────────────────────────────────────────────────────
        private static bool IsUsaLocation(string loc)
        {
            if (string.IsNullOrWhiteSpace(loc)) return false;
            var l = loc.ToLower();
            if (l.Contains("remote") || l.Contains("united states") || l.Contains("usa"))
                return true;
            if (UsStatePattern.IsMatch(loc)) return true;
            return UsaCityFragments.Any(city => l.Contains(city));
        }

        private static string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            var text = HtmlStripPattern.Replace(html, " ");
            return Regex.Replace(text, @"\s{2,}", " ").Trim();
        }

        private static string ExtractJobType(JsonElement job, string description)
        {
            var explicitType = FirstNonEmpty(
                job.GetStringOrNull("employment_type"),
                job.GetStringOrNull("employmentType"),
                job.GetStringOrNull("job_type"),
                job.GetStringOrNull("jobType"),
                job.GetStringOrNull("commitment"),
                job.GetStringOrNull("type"));
            if (!string.IsNullOrWhiteSpace(explicitType)) return NormalizeJobType(explicitType);

            var metadataType = JobTypeFromMetadata(job);
            if (!string.IsNullOrWhiteSpace(metadataType)) return NormalizeJobType(metadataType);

            return InferJobType($"{job.GetStringOrNull("title")} {description}");
        }

        private static string ExtractCategory(JsonElement job)
        {
            var explicitCategory = FirstNonEmpty(
                job.GetStringOrNull("category"),
                job.GetStringOrNull("department"),
                job.GetStringOrNull("team"),
                job.GetStringOrNull("function"));
            if (!string.IsNullOrWhiteSpace(explicitCategory)) return explicitCategory;

            var departments = NamesFromArrayOrObject(job, "departments");
            if (!string.IsNullOrWhiteSpace(departments)) return departments;

            var metadataCategory = CategoryFromMetadata(job);
            if (!string.IsNullOrWhiteSpace(metadataCategory)) return metadataCategory;

            return InferCategory($"{job.GetStringOrNull("title")}");
        }

        private static string NamesFromArrayOrObject(JsonElement job, string property)
        {
            if (!job.TryGetProperty(property, out var value)) return "";
            var names = new List<string>();
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    var name = NameFromElement(item);
                    if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                }
            }
            else
            {
                var name = NameFromElement(value);
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
            }

            return string.Join(" / ", names.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private static string NameFromElement(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String) return value.GetString();
            if (value.ValueKind != JsonValueKind.Object) return "";
            return FirstNonEmpty(
                value.GetStringOrNull("name"),
                value.GetStringOrNull("label"),
                value.GetStringOrNull("value"),
                value.GetStringOrNull("title"));
        }

        private static string CategoryFromMetadata(JsonElement job)
        {
            if (!job.TryGetProperty("metadata", out var metadata)) return "";
            if (metadata.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in metadata.EnumerateArray())
                {
                    var value = CategoryFromMetadataItem(item);
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }
            else if (metadata.ValueKind == JsonValueKind.Object)
            {
                return CategoryFromMetadataItem(metadata);
            }

            return "";
        }

        private static string CategoryFromMetadataItem(JsonElement item)
        {
            var name = FirstNonEmpty(
                item.GetStringOrNull("name"),
                item.GetStringOrNull("label"),
                item.GetStringOrNull("key"),
                item.GetStringOrNull("title"));
            if (string.IsNullOrWhiteSpace(name)) return "";
            if (!LooksLikeCategoryField(name)) return "";

            return FirstNonEmpty(
                item.GetStringOrNull("value"),
                item.GetStringOrNull("display_value"),
                item.GetStringOrNull("displayValue"));
        }

        private static bool LooksLikeCategoryField(string name)
        {
            var normalized = name.Trim().ToLowerInvariant();
            return normalized.Contains("department") ||
                   normalized.Contains("team") ||
                   normalized.Contains("function") ||
                   normalized.Contains("job family") ||
                   normalized.Contains("category");
        }

        private static string InferCategory(string text)
        {
            text ??= "";
            if (Regex.IsMatch(text, @"\b(security|compliance|legal|privacy)\b", RegexOptions.IgnoreCase)) return "Legal / Security";
            if (Regex.IsMatch(text, @"\b(data|analytics|business intelligence|machine learning|ai|ml)\b", RegexOptions.IgnoreCase)) return "Data / AI";
            if (Regex.IsMatch(text, @"\b(engineer|developer|software|platform|infrastructure|devops|sre)\b", RegexOptions.IgnoreCase)) return "Engineering";
            if (Regex.IsMatch(text, @"\b(product|design|designer|research)\b", RegexOptions.IgnoreCase)) return "Product / Design";
            if (Regex.IsMatch(text, @"\b(sales|account executive|customer success|partnership)\b", RegexOptions.IgnoreCase)) return "Sales / Customer";
            if (Regex.IsMatch(text, @"\b(marketing|growth|communications|brand)\b", RegexOptions.IgnoreCase)) return "Marketing";
            if (Regex.IsMatch(text, @"\b(finance|accounting|controller|revenue|treasury)\b", RegexOptions.IgnoreCase)) return "Finance";
            if (Regex.IsMatch(text, @"\b(people|recruit|talent|hr|human resources)\b", RegexOptions.IgnoreCase)) return "People";
            if (Regex.IsMatch(text, @"\b(operations|ops|program manager|project manager)\b", RegexOptions.IgnoreCase)) return "Operations";

            return "General";
        }

        private static string JobTypeFromMetadata(JsonElement job)
        {
            if (!job.TryGetProperty("metadata", out var metadata)) return "";
            if (metadata.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in metadata.EnumerateArray())
                {
                    var value = JobTypeFromMetadataItem(item);
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }
            else if (metadata.ValueKind == JsonValueKind.Object)
            {
                return JobTypeFromMetadataItem(metadata);
            }

            return "";
        }

        private static string JobTypeFromMetadataItem(JsonElement item)
        {
            var name = FirstNonEmpty(
                item.GetStringOrNull("name"),
                item.GetStringOrNull("label"),
                item.GetStringOrNull("key"),
                item.GetStringOrNull("title"));
            if (string.IsNullOrWhiteSpace(name)) return "";
            if (!LooksLikeJobTypeField(name)) return "";

            return FirstNonEmpty(
                item.GetStringOrNull("value"),
                item.GetStringOrNull("display_value"),
                item.GetStringOrNull("displayValue"));
        }

        private static bool LooksLikeJobTypeField(string name)
        {
            var normalized = name.Trim().ToLowerInvariant();
            if (normalized.Contains("location")) return false;
            return normalized.Contains("employment") ||
                   normalized.Contains("job type") ||
                   normalized.Contains("time type") ||
                   normalized.Contains("worker type") ||
                   normalized.Contains("position type") ||
                   normalized.Contains("schedule");
        }

        private static string InferJobType(string text)
        {
            text ??= "";
            if (Regex.IsMatch(text, @"\b(contract|contractor|contract-to-hire|c2h)\b", RegexOptions.IgnoreCase)) return "Contract";
            if (Regex.IsMatch(text, @"\bpart[-\s]?time\b", RegexOptions.IgnoreCase)) return "Part time";
            if (Regex.IsMatch(text, @"\b(temp|temporary)\b", RegexOptions.IgnoreCase)) return "Temporary";
            if (Regex.IsMatch(text, @"\bintern(ship)?\b", RegexOptions.IgnoreCase)) return "Internship";
            if (Regex.IsMatch(text, @"\bseasonal\b", RegexOptions.IgnoreCase)) return "Seasonal";
            if (Regex.IsMatch(text, @"\bfreelance\b", RegexOptions.IgnoreCase)) return "Freelance";
            if (Regex.IsMatch(text, @"\bper\s+diem\b", RegexOptions.IgnoreCase)) return "Per diem";
            if (Regex.IsMatch(text, @"\bfull[-\s]?time\b", RegexOptions.IgnoreCase)) return "Full time";

            return "Full time";
        }

        private static string NormalizeJobType(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            return InferJobType(value) == "Full time" && !Regex.IsMatch(value, @"\bfull[-\s]?time\b", RegexOptions.IgnoreCase)
                ? value.Trim()
                : InferJobType(value);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }

            return "";
        }
    }

    //internal class GreenhouseAtsScraper
    //{
    //    private const int SOURCE_ID = 54;

    //    // Add any company that uses Greenhouse ATS

    //    public async Task<List<ScrapedJob>> FetchJobsAsync()
    //    {
    //        var results = new List<ScrapedJob>();

    //        foreach (var company in Companies)
    //        {
    //            try
    //            {
    //                var url = $"https://boards-api.greenhouse.io/v1/boards/{company}/jobs?content=true";
    //                var json = await Http.GetStringAsync(url);
    //                var root = JsonDocument.Parse(json).RootElement;

    //                if (!root.TryGetProperty("jobs", out var jobsEl)) continue;

    //                foreach (var j in jobsEl.EnumerateArray())
    //                {
    //                    // Location filter — only USA
    //                    var location = j.TryGetProperty("location", out var locEl)
    //                        ? locEl.GetStringOrNull("name") ?? "" : "";
    //                    if (!IsUsaLocation(location)) continue;

    //                    results.Add(new ScrapedJob
    //                    {
    //                        SourceId = SOURCE_ID,
    //                        ExternalId = j.TryGetProperty("id", out var idEl)
    //                                        ? idEl.GetInt64().ToString() : null,
    //                        Title = j.GetStringOrNull("title"),
    //                        Company = company.ToUpperFirst(),
    //                        Location = location,
    //                        Description = StripHtml(j.GetStringOrNull("content")),
    //                        JobUrl = j.GetStringOrNull("absolute_url"),
    //                        IsRemote = location.Contains("Remote", StringComparison.OrdinalIgnoreCase),
    //                        DatePosted = j.TryGetProperty("updated_at", out var ua)
    //                                        ? DateTime.TryParse(ua.GetString(), out var dt) ? dt : (DateTime?)null
    //                                        : null
    //                    });
    //                }
    //            }
    //            catch (Exception ex)
    //            {
    //                Console.WriteLine($"[Greenhouse] {company} error: {ex.Message}");
    //            }

    //            await Task.Delay(800);
    //        }

    //        return results;
    //    }


    //    private static bool IsItJob(string? title)
    //    {
    //        if (string.IsNullOrWhiteSpace(title)) return false;
    //        return ItTitleKeywords.Any(kw =>
    //            title.Contains(kw, StringComparison.OrdinalIgnoreCase));
    //    }
    //    private static bool IsUsaLocation(string loc)
    //    {
    //        if (string.IsNullOrWhiteSpace(loc)) return false;
    //        var l = loc.ToLower();

    //        if (l.Contains("remote") || l.Contains("united states") || l.Contains("usa"))
    //            return true;

    //        if (UsStatePattern.IsMatch(loc)) return true;  // ", CA" but not "Canada"

    //        return UsaCityFragments.Any(city => l.Contains(city));
    //    }

    //    private static string StripHtml(string html)
    //    {
    //        if (string.IsNullOrEmpty(html)) return html;
    //        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ").Trim();
    //    }

    //}
}
