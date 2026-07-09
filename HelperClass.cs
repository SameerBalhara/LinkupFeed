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

    // Decides whether a posting is an Information Technology / software role.
    // Mirrors UsLocationFilter: cheap keyword matching against the job title and
    // category (description is intentionally ignored — it mentions "software"/"systems"
    // on nearly every posting and would let everything through).
    internal static class ItJobFilter
    {
        // Phrases that, on their own, almost always identify an IT/software role.
        private static readonly string[] StrongItSignals =
        {
            // Engineering / development
            "software", "developer", "programmer", "full stack", "fullstack",
            "front end", "frontend", "front-end", "back end", "backend", "back-end",
            "web developer", "web designer", "mobile developer", "ios developer",
            "android developer", "embedded software", "firmware",
            "java developer", "python developer", ".net", "react", "node", "golang",
            // Data / AI
            "data engineer", "data scientist", "data analyst", "data architect",
            "machine learning", "artificial intelligence", " ai ", "ml engineer",
            "business intelligence", "data warehouse", "etl",
            // Infrastructure / cloud / ops
            "devops", "sre", "site reliability", "cloud", "kubernetes", "terraform",
            "platform engineer", "infrastructure engineer", "network engineer",
            "network administrator", "systems administrator", "system administrator",
            "sysadmin",
            // Security
            "cyber", "cybersecurity", "information security", "infosec", "appsec",
            "security engineer", "security analyst", "penetration test",
            // QA
            "qa engineer", "quality assurance", "sdet", "test engineer",
            "automation engineer",
            // Database
            "database administrator", "dba", "sql",
            // IT operations / support
            "it support", "help desk", "helpdesk", "it manager", "it analyst",
            "it specialist", "information technology", "servicenow",
            // Architecture / leadership / process
            "solutions architect", "software architect", "cloud architect",
            "enterprise architect", "technical architect", "scrum master",
            "technical program manager", "erp", "salesforce"
        };

        // Generic words that signal IT only when no competing non-IT qualifier is present.
        private static readonly string[] AmbiguousSignals =
        {
            "engineer", "engineering", "architect", "analyst",
            "technician", "technologist", "technology", "technical"
        };

        // If an ambiguous title also contains one of these, it is NOT an IT role
        // (mechanical engineer, sales analyst, clinical technician, etc.).
        private static readonly string[] NonItQualifiers =
        {
            "mechanical", "civil", "chemical", "electrical", "industrial",
            "structural", "process", "petroleum", "aerospace", "biomedical",
            "manufacturing", "environmental", "geotechnical", "materials",
            "sales", "account", "member services", "marketing", "financial", "finance", "accounting",
            "human resources", "recruit", "nurse", "nursing", "clinical", "patient care",
            "caregiver", "hospital", "health", "healthcare", "medical", "rehab", "rehabilitation",
            "respiratory", "radiology", "pharmacy", "pharmacist", "physician", "dental",
            "therapist", "therapy", "behavior", "behavioral", "behavioral health",
            "logistics", "supply chain", "warehouse", "maintenance", "facilities", "construction",
            "safety", "machinist", "electrician", "store", "retail",
            "teacher", "professor", "laboratory", "legal", "attorney",
            "paralegal", "customer service"
        };

        public static bool IsIt(string title, string category = null)
        {
            // Pad so boundary tokens like " ai " match at the start/end too.
            var hay = " " + (title ?? "").ToLowerInvariant() + " | " +
                      (category ?? "").ToLowerInvariant() + " ";

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(category))
                return false;

            // 1) Unambiguous IT signal anywhere in title/category wins.
            if (StrongItSignals.Any(s => hay.Contains(s))) return true;

            // 2) Generic tech word counts only when no non-IT qualifier competes.
            if (AmbiguousSignals.Any(s => hay.Contains(s)))
                return !NonItQualifiers.Any(n => hay.Contains(n));

            return false;
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




