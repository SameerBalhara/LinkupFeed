using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LinkupFeed
{
    // Lever ATS exposes a public JSON postings endpoint per company:
    //   GET https://api.lever.co/v0/postings/{company}?mode=json
    // Each posting has: id, text (title), categories{location,team,commitment},
    // descriptionPlain, description (HTML), hostedUrl, createdAt (epoch ms),
    // workplaceType (remote|hybrid|on-site).
    internal class LeverScraper
    {
        private const int SOURCE_ID = 57;

        // Curated list of Lever tenants. Lever has no public tenant directory,
        // so this list is harvested from known customers. Each token is the
        // slug used in https://api.lever.co/v0/postings/{token}?mode=json.
        // Invalid/dead tokens fail fast (404) and are skipped — safe to grow.
        private static readonly string[] Companies =
        {
            // ── Lever's own + classic showcase customers ─────────────────────
            "lever", "netflix", "shopify", "spotify", "kickstarter",
            "eventbrite", "yelp", "github",

            // ── AI / ML labs ────────────────────────────────────────────────
            "openai", "cohere", "perplexityai", "characterai", "anthropic",
            "huggingface", "runway", "stability", "adept", "writer",
            "elevenlabs", "mistral", "together", "fixie", "imbue",

            // ── Fintech / payments / crypto ─────────────────────────────────
            "plaid", "ramp", "mercury", "brex", "stripe-press",
            "kraken", "blockchain", "circle", "anchorage", "fireblocks",
            "chainalysis", "alchemy", "paxos", "gemini", "bitgo",
            "ledger", "ripple", "consensys", "bittrex", "blockfi",
            "ftx", "robinhood-markets", "trumid", "marqeta",
            "earnin", "wealthsimple", "stash", "varo", "current",
            "wisetack", "tala", "kueski", "petalcard", "tally",
            "modern-treasury", "rho", "mainstreet",

            // ── HR / payroll / fintech infra ────────────────────────────────
            "gusto", "rippling", "deel", "remote", "justworks",
            "checkr", "lattice", "personio", "bamboohr", "humaans",

            // ── Dev tools / infrastructure / data ───────────────────────────
            "hashicorp", "vercel", "netlify", "supabase", "render",
            "fly", "render-services", "snyk", "lacework", "stackrox",
            "tailscale", "1password", "dashlane", "auth0",
            "wiz-io", "orca", "sysdig", "datadog-internal",
            "elastic", "confluent", "redpanda", "materialize",
            "dbt", "prefect", "dagster", "airbyte", "fivetran",
            "monte-carlo", "lightup", "soda", "metaplane",
            "starburst", "imply", "rockset", "tinybird",
            "linear", "raycast", "warp", "fig",

            // ── SaaS / collaboration / productivity ─────────────────────────
            "notion", "miro", "loom", "figma-design", "framer",
            "linear-app", "height", "clickup", "monday",
            "calendly", "front", "intercom-careers", "kustomer",
            "klaviyo", "attentive", "iterable", "customerio",
            "segment", "amplitude", "heap", "fullstory",
            "mixpanel-careers", "pendo", "userpilot",
            "patreon", "substack", "buffer", "later", "hootsuite",

            // ── E-commerce / marketplaces / consumer ────────────────────────
            "shopify-platform", "wayfair", "instacart", "doordash",
            "gopuff", "imperfectfoods", "thrivemarket", "boxed",
            "faire", "rappi", "delivery-hero", "swiggy",
            "thumbtack", "taskrabbit", "houzz", "etsy",
            "reddit", "discord-careers", "twitch-careers",
            "patreon-careers", "vimeo", "soundcloud",
            "duolingo-careers", "coursera-careers", "khanacademy",
            "masterclass", "skillshare", "outschool", "varsity-tutors",
            "fitbit", "peloton", "whoop", "oura", "future",
            "everlane", "warbyparker", "allbirds", "rothys",
            "casper", "purple", "thirdlove", "harrys",
            "olipop", "magic-spoon", "hu", "athleticgreens",

            // ── Travel / mobility / logistics ───────────────────────────────
            "lyft-careers", "via", "lime", "bird", "turo",
            "flexport", "convoy", "shippo", "stord", "deliverr",
            "samsara", "nuro", "argo", "aurora-innovation",
            "tortoise", "starship", "kodiakcargo",

            // ── Healthcare / biotech / health-tech ──────────────────────────
            "rolihealth", "rohealth", "ro", "hims", "nurx",
            "carbonhealth", "tiahealth", "modernhealth",
            "lyrahealth", "spring", "headspace", "calm",
            "cedar", "olive", "innovaccer",
            "tempus-labs", "color", "23andme-careers", "veracyte",
            "pacbio", "guardanthealth", "natera",

            // ── Climate / energy / sustainability ───────────────────────────
            "watershed", "stripe-climate", "patch", "pachama",
            "carbon-direct", "carbonchain", "climeworks",
            "form-energy", "commonwealthfusion", "kairospower",
            "octopus", "arcadia", "leap", "voltus",
            "weave", "siliconranch",

            // ── Gaming / interactive / media ────────────────────────────────
            "epicgames", "riot-games", "unity", "unity-technologies",
            "discord", "twitch", "roblox-careers",
            "scopely", "machinezone", "playtika", "kongregate",
            "scientific-games", "scopely-careers",

            // ── Productivity / contract / legal / ops ───────────────────────
            "ironclad", "ironcladapp", "everlaw", "atrium",
            "lexion", "clio", "logikcull", "relativity",
            "icertis", "agiloft", "concord", "linksquares",

            // ── Marketing / analytics / adtech ──────────────────────────────
            "ironsource", "applovin", "moloco", "adjust",
            "branch", "appsflyer", "kochava", "moengage",
            "braze", "leanplum", "airship", "onesignal",
            "amplitude-careers", "permutive", "lotame",

            // ── Logistics / industrial / IoT ────────────────────────────────
            "samsara-networks", "verkada", "rhombus", "openpath",
            "kepler", "skydio", "shieldai", "anduril-industries",
            "saronic", "applied-intuition", "ouster",
            "velodynelidar", "luminar", "cepton",

            // ── Public sector / civic / education tech ──────────────────────
            "code-for-america", "khan-academy", "newsela", "remind",
            "schoolmint", "powerschool", "panorama",

            // ── Misc / scale-ups ────────────────────────────────────────────
            "scale", "scaleai-careers", "groq", "samsung-next",
            "shippo-platform", "modsy", "stitchfix-careers",
            "rover", "wag", "petco-careers",
            "bolt", "fast", "tiptap", "linktree",
            "carta-careers", "alto", "kindbody", "maven-clinic",
            "lyft", "instacart-careers",

            // ── Added batch: more Lever tenants (404s fail fast & are skipped) ─
            "udacity", "brave", "matterport", "benchprep", "leadgenius",
            "voiceflow", "vannevarlabs", "abnormal-security", "kong",
            "hightouch", "census", "secoda", "hex", "deepgram",
            "assemblyai", "modal", "baseten", "weights-biases", "neptune",
            "labelbox", "surge", "turing", "andela", "gitpod",
            "replit", "sourcegraph", "temporal", "cockroachlabs",
            "timescale", "clickhouse", "yugabyte", "planetscale-careers",
            "grafana", "honeycomb", "chronosphere", "cribl", "panther",
            "sentinelone", "snyk-io", "tines", "vanta", "drata",
            "secureframe", "huntress", "expel", "arctic-wolf"
        };

        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
            DefaultRequestHeaders =
            {
                { "User-Agent", "ITJobCafe-Scraper/1.0" }
            }
        };

        private static readonly Regex HtmlStripPattern =
            new Regex(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex MultiSpacePattern =
            new Regex(@"\s{2,}", RegexOptions.Compiled);

        public async Task<List<ScrapedJob>> FetchJobsAsync()
        {
            var results = new List<ScrapedJob>();

            foreach (var company in Companies)
            {
                try
                {
                    var url = $"https://api.lever.co/v0/postings/{company}?mode=json";
                    Console.WriteLine($"[Lever] {company} → starting");

                    var json = await _http.GetStringAsync(url);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.ValueKind != JsonValueKind.Array)
                    {
                        Console.WriteLine($"[Lever] {company} → unexpected payload shape");
                        continue;
                    }

                    int added = 0;
                    foreach (var j in root.EnumerateArray())
                    {
                        var title = j.GetStringOrNull("text");

                        string location = "";
                        string team = null;
                        string commitment = null;
                        if (j.TryGetProperty("categories", out var cat) &&
                            cat.ValueKind == JsonValueKind.Object)
                        {
                            location   = cat.GetStringOrNull("location") ?? "";
                            team       = cat.GetStringOrNull("team");
                            commitment = cat.GetStringOrNull("commitment");
                        }

                        var workplace = j.GetStringOrNull("workplaceType") ?? "";
                        bool isRemote =
                            workplace.Equals("remote", StringComparison.OrdinalIgnoreCase) ||
                            location.IndexOf("remote", StringComparison.OrdinalIgnoreCase) >= 0;

                        // US-only filter (mirrors what Program.cs would re-apply later).
                        if (!UsLocationFilter.IsUs(location) && !isRemote) continue;

                        DateTime? posted = null;
                        if (j.TryGetProperty("createdAt", out var ca) &&
                            ca.ValueKind == JsonValueKind.Number &&
                            ca.TryGetInt64(out var ms))
                        {
                            posted = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
                        }

                        var desc = j.GetStringOrNull("descriptionPlain")
                                ?? StripHtml(j.GetStringOrNull("description"));

                        results.Add(new ScrapedJob
                        {
                            SourceId    = SOURCE_ID,
                            ExternalId  = j.GetStringOrNull("id"),
                            Title       = title,
                            Company     = company.ToUpperFirst(),
                            Location    = location,
                            Description = desc,
                            JobUrl      = j.GetStringOrNull("hostedUrl") ?? j.GetStringOrNull("applyUrl"),
                            IsRemote    = isRemote,
                            DatePosted  = posted,
                            JobType     = commitment,
                            Category    = team
                        });
                        added++;
                    }

                    Console.WriteLine($"[Lever] {company} → {added} US jobs");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Lever] {company} error: {ex.Message}");
                }

                await Task.Delay(800);
            }

            return results;
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
