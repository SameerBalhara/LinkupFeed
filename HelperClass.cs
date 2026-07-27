using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LinkupFeed
{
    internal class HelperClass
    {


    }

    internal static class UsLocationFilter
    {
        private static readonly Regex UsStatePattern = new Regex(
            @"(?:^|[,\s])(AL|AK|AZ|AR|CA|CO|CT|DE|FL|GA|HI|ID|IL|IN|IA|KS|KY|LA|ME|MD|MA|MI|MN|MS|MO|MT|NE|NV|NH|NJ|NM|NY|NC|ND|OH|OK|OR|PA|RI|SC|SD|TN|TX|UT|VT|VA|WA|WV|WI|WY|DC)(?:[,\s]|$)",
            RegexOptions.Compiled);

        private static readonly string[] UsStateNames =
        {
            "alabama","alaska","arizona","arkansas","california","colorado","connecticut",
            "delaware","florida","georgia","hawaii","idaho","illinois","indiana","iowa",
            "kansas","kentucky","louisiana","maine","maryland","massachusetts","michigan",
            "minnesota","mississippi","missouri","montana","nebraska","nevada","new hampshire",
            "new jersey","new mexico","new york","north carolina","north dakota","ohio",
            "oklahoma","oregon","pennsylvania","rhode island","south carolina","south dakota",
            "tennessee","texas","utah","vermont","virginia","washington","west virginia",
            "wisconsin","wyoming","district of columbia"
        };

        private static readonly string[] UsCityFragments =
        {
            "new york","san francisco","seattle","chicago","austin","boston","los angeles",
            "denver","atlanta","miami","dallas","houston","san diego","san jose","phoenix",
            "philadelphia","portland","minneapolis","detroit","nashville","charlotte",
            "raleigh","pittsburgh","cleveland","cincinnati","st. louis","saint louis",
            "kansas city","salt lake city","las vegas","tampa","orlando","jacksonville",
            "indianapolis","columbus","milwaukee","baltimore","sacramento","san antonio",
            "fort worth","oakland","mountain view","palo alto","sunnyvale","santa clara",
            "santa monica","brooklyn","manhattan","washington dc","washington, dc"
        };

        private static readonly string[] NonUsCountrySignals =
        {
            "united kingdom","england","scotland","wales","ireland","northern ireland",
            "canada","mexico","brazil","argentina","colombia","chile","peru",
            "germany","france","spain","italy","netherlands","belgium","portugal",
            "switzerland","austria","sweden","norway","denmark","finland","iceland",
            "poland","czech","romania","hungary","greece","ukraine","russia",
            "india","pakistan","bangladesh","sri lanka","china","japan","south korea",
            "singapore","malaysia","indonesia","philippines","thailand","vietnam",
            "australia","new zealand",
            "israel","turkey","egypt","south africa","nigeria","kenya",
            "uae","united arab emirates","saudi arabia","qatar",
            "emea","apac","latam","europe","asia","africa"
        };

        public static bool IsUs(string location)
        {
            if (string.IsNullOrWhiteSpace(location)) return false;
            var l = location.ToLowerInvariant();

            if (l.Contains("united states") || l.Contains("u.s.a") || l.Contains("u.s.")
                || System.Text.RegularExpressions.Regex.IsMatch(l, @"\busa\b")
                || System.Text.RegularExpressions.Regex.IsMatch(l, @"\bus\b"))
                return true;

            foreach (var c in NonUsCountrySignals)
                if (l.Contains(c)) return false;

            if (UsStateNames.Any(s => l.Contains(s))) return true;
            if (UsCityFragments.Any(c => l.Contains(c))) return true;
            if (UsStatePattern.IsMatch(location)) return true;

            return false;
        }
    }

    internal sealed class ItJobClassification
    {
        public bool IsIT { get; set; }
        public int Score { get; set; }
    }

    // Decides whether a posting is an Information Technology / software role.
    // Uses title/category as primary evidence and description as supporting
    // evidence only, so generic job descriptions do not let everything through.
    internal static class ItJobFilter
    {
        private const int ItThreshold = 3;
        private const int MaybeThreshold = 1;

        private static readonly (string Signal, int Weight)[] StrongTitleSignals =
        {
            // Engineering / development
            ("software", 4), ("developer", 4), ("programmer", 4), ("full stack", 4), ("fullstack", 4),
            ("front end", 4), ("frontend", 4), ("front-end", 4), ("back end", 4), ("backend", 4), ("back-end", 4),
            ("web developer", 4), ("mobile developer", 4), ("mobile engineer", 4),
            ("ios developer", 4), ("ios engineer", 4), ("android developer", 4),
            ("android engineer", 4), ("embedded software", 4), ("firmware", 4),
            ("java developer", 4), ("python developer", 4), (".net", 4), ("react", 3), ("node", 3), ("golang", 4),
            // Data / AI
            ("data engineer", 4), ("data scientist", 4), ("data analyst", 4), ("data architect", 4),
            ("machine learning", 4), ("artificial intelligence", 4), (" ai ", 3), ("ml engineer", 4),
            ("business intelligence", 4), ("data warehouse", 4), ("etl", 4),
            // Infrastructure / cloud / ops
            ("devops", 4), ("sre", 4), ("site reliability", 4), ("cloud", 3), ("kubernetes", 4), ("terraform", 4),
            ("platform engineer", 4), ("infrastructure engineer", 4), ("network engineer", 4),
            ("network administrator", 4), ("systems administrator", 4), ("system administrator", 4),
            ("sysadmin", 4),
            // Security
            ("cyber", 4), ("cybersecurity", 4), ("information security", 4), ("infosec", 4), ("appsec", 4),
            ("security engineer", 4), ("security analyst", 4), ("penetration test", 4),
            // QA
            ("qa engineer", 4), ("quality assurance", 3), ("sdet", 4), ("test engineer", 3),
            ("automation engineer", 3),
            // Database
            ("database administrator", 4), ("dba", 4), ("sql", 3),
            // IT operations / support
            ("it support", 4), ("help desk", 4), ("helpdesk", 4), ("it manager", 4), ("it analyst", 4),
            ("it specialist", 4), ("information technology", 4), ("servicenow", 4),
            // Architecture / leadership / process
            ("solutions architect", 4), ("software architect", 4), ("cloud architect", 4),
            ("enterprise architect", 3), ("technical architect", 3), ("scrum master", 3),
            ("technical program manager", 3), ("erp", 3), ("salesforce", 4)
        };

        private static readonly (string Signal, int Weight)[] WeakTitleSignals =
        {
            ("engineer", 1), ("engineering", 1), ("architect", 1), ("analyst", 1),
            ("technician", 1), ("technologist", 1), ("technology", 1), ("technical", 1),
            ("systems", 1), ("automation", 1), ("operations", 1)
        };

        private static readonly (string Signal, int Weight)[] DescriptionSignals =
        {
            ("software development", 1), ("application development", 1), ("cloud infrastructure", 1),
            ("database", 1), ("sql", 1), ("api", 1), ("cybersecurity", 1), ("network", 1),
            ("linux", 1), ("windows server", 1), ("kubernetes", 1), ("data pipeline", 1),
            ("etl", 1), ("business intelligence", 1), ("salesforce", 1), ("servicenow", 1)
        };

        private static readonly (string Signal, int Weight)[] NegativeTitleSignals =
        {
            ("mechanical", -4), ("civil", -4), ("chemical", -4), ("electrical", -3), ("industrial", -3),
            ("structural", -4), ("process engineer", -4), ("petroleum", -4), ("aerospace", -3),
            ("manufacturing", -4), ("environmental", -3), ("geotechnical", -4), ("materials", -3),
            ("sales engineer", -4), ("sales representative", -4), ("account executive", -4),
            ("marketing", -4), ("financial analyst", -4), ("finance", -3), ("accounting", -4),
            ("human resources", -4), ("recruit", -4), ("nurse", -4), ("nursing", -4),
            ("clinical", -4), ("patient care", -4), ("caregiver", -4), ("hospital", -3),
            ("medical", -4), ("rehab", -4), ("pharmacy", -4), ("physician", -4), ("dental", -4),
            ("therapist", -4), ("behavioral health", -4), ("logistics", -4), ("supply chain", -4),
            ("warehouse", -4), ("maintenance", -4), ("facilities", -4), ("construction", -4),
            ("machinist", -4), ("electrician", -4), ("store", -4), ("retail", -4),
            ("teacher", -4), ("professor", -3), ("laboratory", -4), ("legal", -4),
            ("attorney", -4), ("paralegal", -4), ("customer service", -4),
            ("repair technician", -4), ("field service technician", -4)
        };

        private static readonly (string Signal, int Weight)[] CategorySignals =
        {
            ("engineering", 1), ("technology", 2), ("information technology", 3), ("it", 2),
            ("software", 3), ("data", 2), ("security", 2), ("product", 1)
        };

        public static bool IsIt(string title, string category = null)
        {
            return Classify(title, category).IsIT;
        }

        public static ItJobClassification Apply(ScrapedJob job)
        {
            var classification = Classify(job?.Title, job?.Category, job?.Description);
            if (job != null)
            {
                job.IsIT = classification.IsIT;
                job.ITScore = classification.Score;
            }

            return classification;
        }

        public static ItJobClassification Classify(string title, string category = null, string description = null)
        {
            var result = new ItJobClassification();
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(category))
            {
                return result;
            }

            var titleHay = Pad(title);
            var categoryHay = Pad(category);
            var descriptionHay = Pad(Truncate(description, 2000));

            AddMatches(result, titleHay, StrongTitleSignals);
            AddMatches(result, titleHay, WeakTitleSignals);
            AddMatches(result, categoryHay, CategorySignals);
            AddMatches(result, titleHay, NegativeTitleSignals);

            // Description is supporting evidence only. It cannot turn a job into
            // IT by itself unless the title/category is at least weakly technical.
            if (result.Score >= MaybeThreshold)
            {
                AddMatches(result, descriptionHay, DescriptionSignals);
            }

            result.IsIT = result.Score >= ItThreshold;
            return result;
        }

        private static void AddMatches(ItJobClassification result, string haystack, IEnumerable<(string Signal, int Weight)> signals)
        {
            foreach (var (signal, weight) in signals)
            {
                if (!ContainsSignal(haystack, signal)) continue;

                result.Score += weight;
            }
        }

        private static bool ContainsSignal(string haystack, string signal)
        {
            if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(signal)) return false;
            signal = signal.ToLowerInvariant();
            if (signal.Trim().Length <= 3 && Regex.IsMatch(signal, @"^[a-z0-9+#.]+$"))
            {
                return Regex.IsMatch(haystack, $@"(?<![a-z0-9+#.]){Regex.Escape(signal.Trim())}(?![a-z0-9+#.])", RegexOptions.IgnoreCase);
            }

            return haystack.Contains(signal);
        }

        private static string Pad(string value)
        {
            return " " + (value ?? "").ToLowerInvariant() + " ";
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value;
            return value.Substring(0, maxLength);
        }

    }

    internal static class JsonExtensions
    {
        public static string GetStringOrNull(this JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() : null;

        public static string GetStringOrNull(this JsonElement el, string prop1, string prop2) =>
            el.GetStringOrNull(prop1) ?? el.GetStringOrNull(prop2);
    }

    internal static class StringExtensions
    {
        public static string ToUpperFirst(this string s) =>
            string.IsNullOrEmpty(s) ? s :
            char.ToUpper(s[0]) + s.Substring(1);
    }

    // Splits a free-text location ("San Francisco, CA", "Houston, TX, USA",
    // "New York, New York") into a City and a 2-letter State code. Country
    // tokens (USA / United States / US) are discarded. When no US state can be
    // identified the State comes back empty; when the string has no usable city
    // (e.g. "Remote", "United States") the City comes back empty too.
    internal static class LocationSplitter
    {
        private static readonly Dictionary<string, string> StateNameToCode =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["alabama"] = "AL", ["alaska"] = "AK", ["arizona"] = "AZ", ["arkansas"] = "AR",
            ["california"] = "CA", ["colorado"] = "CO", ["connecticut"] = "CT",
            ["delaware"] = "DE", ["florida"] = "FL", ["georgia"] = "GA", ["hawaii"] = "HI",
            ["idaho"] = "ID", ["illinois"] = "IL", ["indiana"] = "IN", ["iowa"] = "IA",
            ["kansas"] = "KS", ["kentucky"] = "KY", ["louisiana"] = "LA", ["maine"] = "ME",
            ["maryland"] = "MD", ["massachusetts"] = "MA", ["michigan"] = "MI",
            ["minnesota"] = "MN", ["mississippi"] = "MS", ["missouri"] = "MO",
            ["montana"] = "MT", ["nebraska"] = "NE", ["nevada"] = "NV",
            ["new hampshire"] = "NH", ["new jersey"] = "NJ", ["new mexico"] = "NM",
            ["new york"] = "NY", ["north carolina"] = "NC", ["north dakota"] = "ND",
            ["ohio"] = "OH", ["oklahoma"] = "OK", ["oregon"] = "OR", ["pennsylvania"] = "PA",
            ["rhode island"] = "RI", ["south carolina"] = "SC", ["south dakota"] = "SD",
            ["tennessee"] = "TN", ["texas"] = "TX", ["utah"] = "UT", ["vermont"] = "VT",
            ["virginia"] = "VA", ["washington"] = "WA", ["west virginia"] = "WV",
            ["wisconsin"] = "WI", ["wyoming"] = "WY", ["district of columbia"] = "DC"
        };

        private static readonly HashSet<string> StateCodes =
            new HashSet<string>(StateNameToCode.Values, StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> CountryTokens =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "usa", "u.s.a", "u.s.a.", "us", "u.s.", "united states",
            "united states of america", "remote"
        };

        public static string CityOf(string location)
        {
            Split(location, out var city, out _);
            return city;
        }

        public static string StateOf(string location)
        {
            Split(location, out _, out var state);
            return state;
        }

        public static void Split(string location, out string city, out string state)
        {
            city = "";
            state = "";
            if (string.IsNullOrWhiteSpace(location)) return;

            var parts = location
                .Split(',')
                .Select(p => p.Trim())
                .Where(p => p.Length > 0 && !CountryTokens.Contains(p))
                .ToList();

            if (parts.Count == 0) return;

            city = parts[0];

            // Look for a US state (2-letter code or full name) in the remaining segments.
            for (int i = 1; i < parts.Count; i++)
            {
                var code = NormalizeState(parts[i]);
                if (code != null) { state = code; break; }
            }

            // No recognizable state but a second segment exists — keep it verbatim
            // (handles regions/provinces we don't map explicitly).
            if (state.Length == 0 && parts.Count > 1)
                state = parts[1];
        }

        private static string NormalizeState(string token)
        {
            if (StateCodes.Contains(token)) return token.ToUpperInvariant();
            if (StateNameToCode.TryGetValue(token, out var code)) return code;
            return null;
        }
    }

    public class FreeJobScraperOrchestrator
    {
        public async Task<List<ScrapedJob>> RunAllAsync()
        {
            var all = new List<ScrapedJob>();

            Console.WriteLine("[Orchestrator] Starting Jobicy...");
            all.AddRange(await new JobicyScraper().FetchJobsAsync());
            Console.WriteLine($"[Orchestrator] Jobicy done. Total so far: {all.Count}");

            Console.WriteLine("[Orchestrator] Starting Remotive...");
            all.AddRange(await new RemotiveScraper().FetchJobsAsync());
            Console.WriteLine($"[Orchestrator] Remotive done. Total so far: {all.Count}");

            Console.WriteLine("[Orchestrator] Starting We Work Remotely...");
            all.AddRange(await new WeWorkRemotelyScraper().FetchJobsAsync());
            Console.WriteLine($"[Orchestrator] WWR done. Total so far: {all.Count}");

            Console.WriteLine("[Orchestrator] Starting Arbeitnow...");
            all.AddRange(await new ArbeitnowScraper().FetchJobsAsync());
            Console.WriteLine($"[Orchestrator] Arbeitnow done. Total so far: {all.Count}");

            Console.WriteLine("[Orchestrator] Starting Greenhouse ATS...");
            all.AddRange(await new GreenhouseAtsScraper().FetchJobsAsync());
            Console.WriteLine($"[Orchestrator] Greenhouse done. Total so far: {all.Count}");

            Console.WriteLine("[Orchestrator] Starting Taleo...");
            all.AddRange(await new TaleoScraper().FetchJobsAsync());
            Console.WriteLine($"[Orchestrator] Taleo done. Total so far: {all.Count}");

            // Deduplicate by URL
            var deduped = all
                .GroupBy(j => j.JobUrl?.Trim().ToLower() ?? j.ExternalId ?? Guid.NewGuid().ToString())
                .Select(g => g.First())
                .ToList();

            Console.WriteLine($"[Orchestrator] Complete. {deduped.Count} unique jobs (from {all.Count} raw).");
            return deduped;
        }
    }


}




