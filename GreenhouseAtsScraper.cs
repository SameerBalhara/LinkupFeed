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

                        results.Add(new ScrapedJob
                        {
                            SourceId = SOURCE_ID,
                            ExternalId = j.TryGetProperty("id", out var idEl)
                                            ? idEl.GetInt64().ToString() : null,
                            Title = j.GetStringOrNull("title"),
                            Company = company.ToUpperFirst(),
                            Location = location,
                            Description = StripHtml(j.GetStringOrNull("content")),
                            JobUrl = j.GetStringOrNull("absolute_url"),
                            IsRemote = location.Contains("Remote", StringComparison.OrdinalIgnoreCase),
                            DatePosted = j.TryGetProperty("updated_at", out var ua)
                                            ? DateTime.TryParse(ua.GetString(), out var dt) ? dt : (DateTime?)null
                                            : null
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

        private static string StripHtml(string? html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            var text = HtmlStripPattern.Replace(html, " ");
            return Regex.Replace(text, @"\s{2,}", " ").Trim();
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
