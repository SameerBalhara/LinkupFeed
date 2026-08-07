using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
            ("assembly", -4), ("assembler", -4), ("forklift", -4), ("batch technician", -4),
            ("machine operator", -4), ("cnc operator", -4), ("cnc machinist", -4),
            ("cmm operator", -4), ("production", -4), ("machinist", -4), ("electrician", -4),
            ("store", -4), ("retail", -4),
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

    internal static class JobCategoryMapper
    {
        private const int MaxCategoryLength = 30;

        private static readonly (string Category, string[] Signals)[] CategoryRules =
        {
            ("Cybersecurity", new[] { "cyber", "cybersecurity", "information security", "infosec", "appsec", "security operations", "security engineer" }),
            ("AI / Machine Learning", new[] { "machine learning", "artificial intelligence", " ai ", " ml ", "deep learning", "data science" }),
            ("Data / Analytics", new[] { "data", "analytics", "business intelligence", "bi ", "database", "etl", "warehouse", "reporting" }),
            ("Cloud / DevOps", new[] { "devops", "sre", "site reliability", "cloud", "infrastructure", "platform", "kubernetes", "terraform" }),
            ("Software Engineering", new[] { "software", "developer", "development", "application development", "programmer", "frontend", "front end", "backend", "back end", "full stack", "fullstack", "mobile engineer" }),
            ("QA / Testing", new[] { "quality assurance", " qa ", "sdet", "test engineer", "testing", "automation engineer" }),
            ("Information Technology", new[] { "information technology", " it ", "technology", "technical services", "network", "systems administrator", "help desk", "helpdesk", "service desk" }),
            ("Product", new[] { "product management", "product manager", "product" }),
            ("Design", new[] { "design", "ux", "ui ", "user experience", "creative" }),
            ("Engineering", new[] { "engineering", "engineer", "architecture" }),
            ("Healthcare", new[] { "healthcare", "health care", "patient care", "medical", "clinical", "radiology", "physician", "therapy", "therapist", "pharmacy" }),
            ("Nursing", new[] { "nursing", "nurse", " rn ", " lpn " }),
            ("Sales", new[] { "sales", "account executive", "business development", "go to market", "customer success" }),
            ("Marketing", new[] { "marketing", "communications", "brand", "growth" }),
            ("Customer Support", new[] { "customer support", "customer service", "client support", "guest services", "service operations" }),
            ("Operations", new[] { "operations", "facilities", "maintenance", "field leadership", "office and field", "administration operations" }),
            ("Finance", new[] { "finance", "accounting", "treasury", "audit", "underwriting", "claims" }),
            ("Legal", new[] { "legal", "attorney", "paralegal", "compliance" }),
            ("Human Resources", new[] { "human resources", " hr ", "people", "talent", "recruiting", "benefits" }),
            ("Education", new[] { "education", "instructional", "teacher", "school", "student", "academic" }),
            ("Manufacturing", new[] { "manufacturing", "production", "warehouse", "assembly", "machinist", "electronics engineering" }),
            ("Supply Chain", new[] { "supply chain", "logistics", "transportation", "distribution", "terminal" }),
            ("Construction", new[] { "construction", "building", "civil", "electrical", "mechanical" }),
            ("Retail", new[] { "retail", "store", "merchandising" }),
            ("Hospitality", new[] { "hospitality", "hotel", "rooms", "front office", "nightlife", "daylife" }),
            ("Public Sector", new[] { "federal", "government", "public sector", "department of army", "law enforcement" }),
            ("Science / Research", new[] { "scientific", "research", "laboratory", "lab " }),
            ("Administrative", new[] { "administrative", "admin", "clerical", "office" })
        };

        public static string Normalize(string category, string title = null, string description = null)
        {
            var cleanedCategory = Clean(category);
            var evidence = Pad($"{cleanedCategory} {Clean(title)} {Clean(description, 600)}");

            foreach (var (mappedCategory, signals) in CategoryRules)
            {
                if (signals.Any(signal => ContainsSignal(evidence, signal)))
                {
                    return mappedCategory;
                }
            }

            if (string.IsNullOrWhiteSpace(cleanedCategory))
            {
                return "";
            }

            return cleanedCategory.Length <= MaxCategoryLength
                ? cleanedCategory
                : "Other";
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

        private static string Clean(string value, int maxLength = 2000)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";

            value = Regex.Replace(value, "<.*?>", " ");
            value = Regex.Replace(value, @"\s+", " ").Trim();
            if (value.Length > maxLength)
            {
                value = value.Substring(0, maxLength);
            }

            return value;
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

        private static readonly Lazy<Dictionary<string, (string City, string State)>> ValidCityStates =
            new Lazy<Dictionary<string, (string City, string State)>>(LoadValidCityStates);

        private static readonly Lazy<Dictionary<string, (string City, string State)>> UnambiguousCityStates =
            new Lazy<Dictionary<string, (string City, string State)>>(LoadUnambiguousCityStates);

        private static readonly Dictionary<string, (string City, string State)> KnownCityStates =
            new Dictionary<string, (string City, string State)>(StringComparer.OrdinalIgnoreCase)
        {
            ["atlanta"] = ("Atlanta", "GA"),
            ["annapolis junction"] = ("Annapolis Junction", "MD"),
            ["arlington"] = ("Arlington", "VA"),
            ["austin"] = ("Austin", "TX"),
            ["baltimore"] = ("Baltimore", "MD"),
            ["beaverton"] = ("Beaverton", "OR"),
            ["bellevue"] = ("Bellevue", "WA"),
            ["boston"] = ("Boston", "MA"),
            ["boulder"] = ("Boulder", "CO"),
            ["brooklyn"] = ("Brooklyn", "NY"),
            ["cambridge"] = ("Cambridge", "MA"),
            ["charlotte"] = ("Charlotte", "NC"),
            ["chicago"] = ("Chicago", "IL"),
            ["cincinnati"] = ("Cincinnati", "OH"),
            ["cleveland"] = ("Cleveland", "OH"),
            ["colorado springs"] = ("Colorado Springs", "CO"),
            ["columbus"] = ("Columbus", "OH"),
            ["crystal city"] = ("Crystal City", "VA"),
            ["dallas"] = ("Dallas", "TX"),
            ["denver"] = ("Denver", "CO"),
            ["detroit"] = ("Detroit", "MI"),
            ["durham"] = ("Durham", "NC"),
            ["fremont"] = ("Fremont", "CA"),
            ["fort worth"] = ("Fort Worth", "TX"),
            ["frisco"] = ("Frisco", "TX"),
            ["houston"] = ("Houston", "TX"),
            ["indianapolis"] = ("Indianapolis", "IN"),
            ["irving"] = ("Irving", "TX"),
            ["jacksonville"] = ("Jacksonville", "FL"),
            ["jersey city"] = ("Jersey City", "NJ"),
            ["kansas city"] = ("Kansas City", "MO"),
            ["la"] = ("Los Angeles", "CA"),
            ["las vegas"] = ("Las Vegas", "NV"),
            ["los angeles"] = ("Los Angeles", "CA"),
            ["manhattan"] = ("Manhattan", "NY"),
            ["marietta"] = ("Marietta", "GA"),
            ["miami"] = ("Miami", "FL"),
            ["milwaukee"] = ("Milwaukee", "WI"),
            ["minneapolis"] = ("Minneapolis", "MN"),
            ["mountain view"] = ("Mountain View", "CA"),
            ["nashville"] = ("Nashville", "TN"),
            ["new york"] = ("New York", "NY"),
            ["new york city"] = ("New York City", "NY"),
            ["nyc"] = ("New York City", "NY"),
            ["oakland"] = ("Oakland", "CA"),
            ["orlando"] = ("Orlando", "FL"),
            ["palo alto"] = ("Palo Alto", "CA"),
            ["philadelphia"] = ("Philadelphia", "PA"),
            ["phoenix"] = ("Phoenix", "AZ"),
            ["pittsburgh"] = ("Pittsburgh", "PA"),
            ["plano"] = ("Plano", "TX"),
            ["portland"] = ("Portland", "OR"),
            ["raleigh"] = ("Raleigh", "NC"),
            ["redmond"] = ("Redmond", "WA"),
            ["redwood city"] = ("Redwood City", "CA"),
            ["reston"] = ("Reston", "VA"),
            ["sacramento"] = ("Sacramento", "CA"),
            ["salt lake city"] = ("Salt Lake City", "UT"),
            ["san antonio"] = ("San Antonio", "TX"),
            ["san diego"] = ("San Diego", "CA"),
            ["san francisco"] = ("San Francisco", "CA"),
            ["sf"] = ("San Francisco", "CA"),
            ["san jose"] = ("San Jose", "CA"),
            ["san mateo"] = ("San Mateo", "CA"),
            ["santa clara"] = ("Santa Clara", "CA"),
            ["santa monica"] = ("Santa Monica", "CA"),
            ["seattle"] = ("Seattle", "WA"),
            ["st. louis"] = ("St. Louis", "MO"),
            ["saint louis"] = ("St. Louis", "MO"),
            ["sunnyvale"] = ("Sunnyvale", "CA"),
            ["tampa"] = ("Tampa", "FL"),
            ["tempe"] = ("Tempe", "AZ"),
            ["washington dc"] = ("Washington DC", "DC"),
            ["washington, dc"] = ("Washington DC", "DC")
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

            if (IsRemoteUsLocation(location))
            {
                city = "Remote - US";
                state = "";
                return;
            }

            if (TryParseHyphenatedUsLocation(location, out city, out state))
            {
                return;
            }

            var semicolonParts = location
                .Split(';')
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();

            if (semicolonParts.Count > 1)
            {
                var preferredPart =
                    semicolonParts.FirstOrDefault(HasUsSignal) ??
                    semicolonParts.FirstOrDefault(p => !IsCountryToken(p) && !p.Equals("remote", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(preferredPart))
                {
                    SplitSingleLocation(preferredPart, out city, out state);
                    city = CleanCityForDatabase(city);
                    return;
                }
            }

            SplitSingleLocation(location, out city, out state);
            city = CleanCityForDatabase(city);
        }

        private static void SplitSingleLocation(string location, out string city, out string state)
        {
            city = "";
            state = "";
            if (string.IsNullOrWhiteSpace(location)) return;

            var parts = location
                .Split(',')
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();

            if (parts.Count == 0) return;

            var stateIndex = -1;

            // Look for a US state (2-letter code or full name), including Workday-style
            // segments such as "AR - JB Hunt Corporate" or "SC 29802 USA". Prefer
            // comma-delimited state/location segments over the first segment, because
            // city names such as "Mt Pleasant" and "Kansas City" can resemble state
            // abbreviations/names.
            var searchOrder = parts.Count > 1
                ? Enumerable.Range(1, parts.Count - 1).Concat(new[] { 0 })
                : Enumerable.Range(0, parts.Count);

            foreach (var i in searchOrder)
            {
                var code = NormalizeState(parts[i]);
                if (code != null)
                {
                    state = code;
                    stateIndex = i;
                    break;
                }
            }

            if (stateIndex > 0)
            {
                city = CleanCityToken(parts[stateIndex - 1]);
                if (city.Length == 0 && stateIndex + 1 < parts.Count)
                {
                    city = CleanCityToken(parts[stateIndex + 1]);
                }
            }
            else if (stateIndex == 0 && parts.Count > 1)
            {
                city = CleanCityToken(parts[1]);
            }
            else
            {
                city = CleanCityToken(parts.FirstOrDefault(p => !IsCountryToken(p)) ?? "");
            }

            ApplyKnownCityState(ref city, ref state);
        }

        private static bool IsRemoteUsLocation(string location)
        {
            var text = (location ?? "").Trim();
            if (text.Length == 0) return false;

            var hasRemote = Regex.IsMatch(text, @"\b(remote|work from home|wfh)\b", RegexOptions.IgnoreCase);
            if (!hasRemote) return false;

            var hasUs = Regex.IsMatch(text, @"\b(united states(?: of america)?|usa|u\.s\.a\.?|u\.s\.|us)\b", RegexOptions.IgnoreCase) ||
                        HasStateNameSignal(text) ||
                        Regex.IsMatch(text, @"(?:^|[-\s/])(AL|AK|AZ|AR|CA|CO|CT|DE|FL|GA|HI|ID|IL|IN|IA|KS|KY|LA|ME|MD|MA|MI|MN|MS|MO|MT|NE|NV|NH|NJ|NM|NY|NC|ND|OH|OK|OR|PA|RI|SC|SD|TN|TX|UT|VT|VA|WA|WV|WI|WY|DC)(?:$|[-\s/])", RegexOptions.IgnoreCase);
            if (!hasUs) return false;

            return !Regex.IsMatch(text, @"\b(dubai|uae|united arab emirates|canada|toronto|ontario|china|beijing|shanghai|india|singapore|europe|emea|apac|latam|united kingdom|uk|london|australia|mexico|brazil)\b", RegexOptions.IgnoreCase);
        }

        private static bool HasStateNameSignal(string text)
        {
            return StateNameToCode.Keys.Any(stateName =>
                Regex.IsMatch(text ?? "", $@"(?:^|[-\s/]){Regex.Escape(stateName)}(?:$|[-\s/])", RegexOptions.IgnoreCase));
        }

        private static bool TryParseHyphenatedUsLocation(string location, out string city, out string state)
        {
            city = "";
            state = "";

            var text = Regex.Replace((location ?? "").Trim(), @"\s+", " ");
            var prefixMatch = Regex.Match(text, @"^(?:usa?|u\.s\.a?\.?|united states(?: of america)?)\s*[-/]\s*(?<rest>.+)$", RegexOptions.IgnoreCase);
            if (!prefixMatch.Success) return false;

            var rest = prefixMatch.Groups["rest"].Value.Trim();
            foreach (var stateName in StateNameToCode.Keys.OrderByDescending(k => k.Length))
            {
                var stateMatch = Regex.Match(rest, $@"^{Regex.Escape(stateName)}\s*[-/]\s*(?<city>.+)$", RegexOptions.IgnoreCase);
                if (!stateMatch.Success) continue;

                var parsedCity = CleanCityToken(stateMatch.Groups["city"].Value);
                if (parsedCity.Length == 0) return false;

                var parsedState = StateNameToCode[stateName];
                if (!TryGetValidCityState(parsedCity, parsedState, out var canonicalCity, out var canonicalState))
                {
                    return false;
                }

                city = canonicalCity;
                state = canonicalState;
                return true;
            }

            var codeMatch = Regex.Match(rest, @"^(?<state>[A-Z]{2})\s*[-/]\s*(?<city>.+)$", RegexOptions.IgnoreCase);
            if (codeMatch.Success)
            {
                var parsedState = codeMatch.Groups["state"].Value.ToUpperInvariant();
                var parsedCity = CleanCityToken(codeMatch.Groups["city"].Value);
                if (StateCodes.Contains(parsedState) && parsedCity.Length > 0 &&
                    TryGetValidCityState(parsedCity, parsedState, out var canonicalCity, out var canonicalState))
                {
                    city = canonicalCity;
                    state = canonicalState;
                    return true;
                }
            }

            return false;
        }

        private static bool HasUsSignal(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            if (NormalizeState(token) != null) return true;

            var text = token.ToLowerInvariant();
            if (text.Contains("united states") ||
                Regex.IsMatch(text, @"\busa\b|\bu\.s\.\b|\bus\b", RegexOptions.IgnoreCase))
            {
                return true;
            }

            return text.Contains("new york") ||
                   text.Contains("san francisco") ||
                   text.Contains("los angeles") ||
                   text.Contains("washington dc") ||
                   text.Contains("atlanta") ||
                   text.Contains("boston") ||
                   text.Contains("boulder") ||
                   text.Contains("chicago") ||
                   text.Contains("dallas") ||
                   text.Contains("denver") ||
                   text.Contains("houston") ||
                   text.Contains("miami") ||
                   text.Contains("nashville") ||
                   text.Contains("philadelphia") ||
                   text.Contains("pittsburgh") ||
                   text.Contains("portland") ||
                   text.Contains("san diego") ||
                   text.Contains("seattle") ||
                   text.Contains("tempe");
        }

        private static string CleanCityForDatabase(string city)
        {
            city = (city ?? "").Trim();
            if (IsRemoteUsLocation(city)) return "Remote - US";
            if (city.Length <= 50) return LooksLikeCityToken(city) ? city : "";

            var first = city
                .Split(new[] { ';', '|', '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .FirstOrDefault(p => !IsCountryToken(p) && !p.Equals("remote", StringComparison.OrdinalIgnoreCase));

            var cleaned = first ?? city;
            return cleaned.Length <= 50 && LooksLikeCityToken(cleaned) ? cleaned : "";
        }

        private static string NormalizeState(string token)
        {
            token = (token ?? "").Trim();
            if (token.Length == 0) return null;

            token = Regex.Replace(token, @"^(?:usa?|u\.s\.a?\.?|united states(?: of america)?)\s*[-/]\s*", "", RegexOptions.IgnoreCase).Trim();
            token = Regex.Replace(token, @"\b(?:usa?|u\.s\.a?\.?|united states(?: of america)?)\b", "", RegexOptions.IgnoreCase).Trim();
            token = Regex.Replace(token, @"\([^)]*\)", "", RegexOptions.IgnoreCase).Trim();
            token = Regex.Replace(token, @"\b(remote|hybrid|onsite|on-site)\b", "", RegexOptions.IgnoreCase).Trim();

            if (StateCodes.Contains(token)) return token.ToUpperInvariant();
            if (StateNameToCode.TryGetValue(token, out var code)) return code;

            var codeMatch = Regex.Match(token, @"^(?<code>[A-Za-z]{2})(?:\b|\s*[-/])");
            if (codeMatch.Success)
            {
                var candidate = codeMatch.Groups["code"].Value.ToUpperInvariant();
                if (StateCodes.Contains(candidate)) return candidate;
            }

            foreach (var stateName in StateNameToCode.Keys.OrderByDescending(k => k.Length))
            {
                if (token.StartsWith(stateName + " -", StringComparison.OrdinalIgnoreCase) ||
                    token.StartsWith(stateName + "/", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals(stateName, StringComparison.OrdinalIgnoreCase))
                {
                    return StateNameToCode[stateName];
                }
            }

            return null;
        }

        private static string CleanCityToken(string token)
        {
            token = (token ?? "").Trim();
            token = Regex.Replace(token, @"^(?:usa?|u\.s\.a?\.?|united states(?: of america)?)\s*[-/]\s*", "", RegexOptions.IgnoreCase).Trim();
            token = Regex.Replace(token, @"\*\*", "").Trim();
            token = Regex.Replace(token, @"\([^)]*\)", "").Trim();
            token = Regex.Split(token, @"\s+(?:or|and)\s+relocation\b", RegexOptions.IgnoreCase).FirstOrDefault() ?? token;
            token = Regex.Split(token, @"\s+[–—-]\s+").FirstOrDefault() ?? token;
            token = token.Split(new[] { '/', '|' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
            token = Regex.Replace(token, @"\b(remote|hybrid|onsite|on-site|fully remote)\b", "", RegexOptions.IgnoreCase).Trim();
            token = token.Trim('-', ',', '.', ':');

            return LooksLikeCityToken(token) ? token : "";
        }

        private static bool LooksLikeCityToken(string token)
        {
            token = (token ?? "").Trim();
            if (token.Length == 0) return false;
            if (token.Length > 50) return false;
            if (IsCountryToken(token) || token.Equals("remote", StringComparison.OrdinalIgnoreCase)) return false;
            if (NormalizeState(token) != null) return false;
            if (Regex.IsMatch(token, @"\d")) return false;
            if (Regex.Matches(token, @"[-/]").Count > 1) return false;
            if (Regex.IsMatch(token, @"^(?:usa?|u\.s\.a?\.?|united states(?: of america)?|remote)\s*[-/]", RegexOptions.IgnoreCase)) return false;
            if (Regex.IsMatch(token, @"\s[-–—]\s")) return false;
            if (Regex.IsMatch(token, @"\*\*|:|<|>|\{|\}|\[|\]")) return false;
            if (Regex.IsMatch(token, @"\b(gbr|uk|united kingdom|canada|toronto|ontario|china|beijing|shanghai|india|singapore|europe|emea|apac|latam|australia|mexico|brazil|germany|france|ireland|netherlands|japan|dubai|uae|united arab emirates)\b", RegexOptions.IgnoreCase)) return false;
            if (Regex.IsMatch(token, @"\b(university|college|school|academy|clinic|clinical|associates?|companies|company|corporate|headquarters|hq|metropolitan|metro|area|township|village|joint base|base|charter|scholars|campus|facility|department|hospital|center|volkswagen|autonation)\b", RegexOptions.IgnoreCase)) return false;
            if (Regex.IsMatch(token, @"\b(position|responsibilit(?:y|ies)|overview|description|candidate|applicant|authorized|required|office|business hours|any location|relocation|multiple|states?|countries|america|worldwide|global)\b", RegexOptions.IgnoreCase)) return false;
            if (Regex.IsMatch(token, @"\b(will|must|should|can|may|work|working|seeking|join|lead|build|maintain|implement|develop|support|manage)\b", RegexOptions.IgnoreCase)) return false;
            if (Regex.Matches(token, @"[A-Za-z]+").Count > 4) return false;

            return Regex.IsMatch(token, @"^[A-Za-z][A-Za-z .'\-]+$");
        }

        private static void ApplyKnownCityState(ref string city, ref string state)
        {
            if (string.IsNullOrWhiteSpace(city)) return;

            if (IsRemoteUsLocation(city))
            {
                city = "Remote - US";
                state = "";
                return;
            }

            var normalized = NormalizeCityKey(city);
            if (KnownCityStates.TryGetValue(normalized, out var mapped))
            {
                if (string.IsNullOrWhiteSpace(state) ||
                    ((normalized == "la" || normalized == "los angeles") &&
                     state.Equals("LA", StringComparison.OrdinalIgnoreCase)) ||
                    state.Equals(mapped.State, StringComparison.OrdinalIgnoreCase))
                {
                    city = mapped.City;
                    state = mapped.State;
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(state) &&
                TryGetValidCityState(city, state, out var validCity, out var validState))
            {
                city = validCity;
                state = validState;
                return;
            }

            if (string.IsNullOrWhiteSpace(state) &&
                TryGetUnambiguousCityState(city, out var inferredCity, out var inferredState))
            {
                city = inferredCity;
                state = inferredState;
                return;
            }

            city = "";
        }

        private static string ToTitleCaseCity(string city)
        {
            city = Regex.Replace((city ?? "").Trim().ToLowerInvariant(), @"\s+", " ");
            return string.Join(" ", city.Split(' ').Select(part =>
            {
                if (part.Length == 0) return part;
                if (part.Equals("dc", StringComparison.OrdinalIgnoreCase)) return "DC";
                return string.Join("-", part.Split('-').Select(piece =>
                    piece.Length <= 1 ? piece.ToUpperInvariant() : char.ToUpperInvariant(piece[0]) + piece.Substring(1)));
            }));
        }

        internal static bool TryGetKnownCityState(string city, out string normalizedCity, out string state)
        {
            normalizedCity = "";
            state = "";

            var key = NormalizeCityKey(city);
            if (KnownCityStates.TryGetValue(key, out var mapped))
            {
                normalizedCity = mapped.City;
                state = mapped.State;
                return true;
            }

            return false;
        }

        private static bool TryGetValidCityState(string city, string state, out string canonicalCity, out string canonicalState)
        {
            canonicalCity = "";
            canonicalState = "";

            var stateCode = NormalizeState(state) ?? (state ?? "").Trim().ToUpperInvariant();
            if (!StateCodes.Contains(stateCode)) return false;

            var key = $"{stateCode}|{NormalizeCityKey(city)}";
            if (ValidCityStates.Value.TryGetValue(key, out var mapped))
            {
                canonicalCity = mapped.City;
                canonicalState = mapped.State;
                return true;
            }

            return false;
        }

        private static bool TryGetUnambiguousCityState(string city, out string canonicalCity, out string canonicalState)
        {
            canonicalCity = "";
            canonicalState = "";

            var key = NormalizeCityKey(city);
            if (key.Length == 0) return false;

            if (UnambiguousCityStates.Value.TryGetValue(key, out var mapped))
            {
                canonicalCity = mapped.City;
                canonicalState = mapped.State;
                return true;
            }

            return false;
        }

        private static Dictionary<string, (string City, string State)> LoadUnambiguousCityStates()
        {
            var grouped = ValidCityStates.Value.Values
                .GroupBy(v => NormalizeCityKey(v.City), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(v => v).Distinct().ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, (string City, string State)>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in grouped)
            {
                var distinctStates = pair.Value.Select(v => v.State).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (distinctStates.Count == 1)
                {
                    result[pair.Key] = pair.Value.First();
                }
            }

            foreach (var alias in KnownCityStates)
            {
                result[alias.Key] = alias.Value;
            }

            return result;
        }

        private static Dictionary<string, (string City, string State)> LoadValidCityStates()
        {
            var result = new Dictionary<string, (string City, string State)>(StringComparer.OrdinalIgnoreCase);
            foreach (var mapped in KnownCityStates.Values)
            {
                result[$"{mapped.State}|{NormalizeCityKey(mapped.City)}"] = mapped;
            }

            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Data", "us_city_state_allowlist.csv"),
                Path.Combine(Directory.GetCurrentDirectory(), "Data", "us_city_state_allowlist.csv")
            };

            var path = candidates.FirstOrDefault(File.Exists);
            if (path == null) return result;

            foreach (var line in File.ReadLines(path).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = SplitCsvLine(line).ToList();
                if (parts.Count < 3) continue;

                var state = parts[0].Trim().ToUpperInvariant();
                var key = parts[1].Trim();
                var city = parts[2].Trim();
                if (!StateCodes.Contains(state) || key.Length == 0 || city.Length == 0) continue;

                result[$"{state}|{key}"] = (city, state);
            }

            return result;
        }

        private static IEnumerable<string> SplitCsvLine(string line)
        {
            var current = new StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (ch == ',' && !inQuotes)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                else
                {
                    current.Append(ch);
                }
            }

            yield return current.ToString();
        }

        private static string NormalizeCityKey(string city)
        {
            city = (city ?? "").Trim().ToLowerInvariant();
            city = Regex.Replace(city, @"\s+", " ");
            city = city.Trim('.', ',');
            return city;
        }

        private static bool IsCountryToken(string token)
        {
            return CountryTokens.Contains((token ?? "").Trim());
        }
    }

    internal static class JobLocationExpander
    {
        private static readonly Regex CountryPrefixPattern =
            new Regex(@"^(?<prefix>[A-Z]{2})\s*-\s*(?<rest>.+)$", RegexOptions.Compiled);

        private static readonly Regex UsStatePathPattern =
            new Regex(@"^US/(?<state>[A-Z]{2})/(?<city>[^()|]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex StateDashAddressPattern =
            new Regex(@"^(?<state>[A-Z]{2})-(?<city>[A-Za-z .]+?)(?:-\d|-[A-Z0-9].*)?$", RegexOptions.Compiled);

        private static readonly Regex UsaStateCityAddressPattern =
            new Regex(@"(?:^|.*?\bUSA\s*-\s*)(?<state>[A-Z]{2})\s*-\s*(?<city>[^-–—|,]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly HashSet<string> StatePrefixCodes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "FL", "GA", "HI", "ID",
            "IL", "IN", "IA", "KS", "KY", "LA", "ME", "MD", "MA", "MI", "MN", "MS",
            "MO", "MT", "NE", "NV", "NH", "NJ", "NM", "NY", "NC", "ND", "OH", "OK",
            "OR", "PA", "RI", "SC", "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV",
            "WI", "WY", "DC"
        };

        private static readonly string[] NonUsSignals =
        {
            "beijing", "shanghai", "shenzhen", "suzhou", "toronto", "canada",
            "china", "united kingdom", "india", "australia", "singapore", "europe",
            "emea", "apac", "latam"
        };

        public static List<ScrapedJob> ExpandForDatabase(
            IEnumerable<ScrapedJob> jobs,
            out int expandedPostings,
            out int addedRows)
        {
            var output = new List<ScrapedJob>();
            expandedPostings = 0;
            addedRows = 0;

            foreach (var job in jobs ?? Enumerable.Empty<ScrapedJob>())
            {
                var variants = GetLocationVariants(job).ToList();
                if (variants.Count <= 1)
                {
                    if (variants.Count == 1 && ShouldCanonicalize(job.Location, variants[0].Location))
                    {
                        output.Add(CloneForLocation(job, variants[0], addLocationSuffix: false));
                    }
                    else
                    {
                        output.Add(job);
                    }

                    continue;
                }

                expandedPostings++;
                addedRows += variants.Count - 1;
                foreach (var variant in variants)
                {
                    output.Add(CloneForLocation(job, variant, addLocationSuffix: true));
                }
            }

            return output;
        }

        private static IEnumerable<LocationVariant> GetLocationVariants(ScrapedJob job)
        {
            var location = job?.Location ?? "";
            var parts = location
                .Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();

            if (parts.Count <= 1)
            {
                var single = BuildVariant(location, job?.IsRemote == true);
                if (single != null)
                {
                    yield return single;
                }

                yield break;
            }

            var variants = parts
                .Select(p => BuildVariant(p, job?.IsRemote == true))
                .Where(v => v != null)
                .GroupBy(v => v.Location, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var hasSpecificLocation = variants.Any(v => !v.IsBroad);
            var hasNonUnitedStatesVariant = variants.Any(v =>
                !v.Location.Equals("United States", StringComparison.OrdinalIgnoreCase));

            foreach (var variant in variants)
            {
                if (variant.Location.Equals("United States", StringComparison.OrdinalIgnoreCase) &&
                    hasNonUnitedStatesVariant)
                {
                    continue;
                }

                if (variant.IsBroad && hasSpecificLocation) continue;
                yield return variant;
            }
        }

        private static LocationVariant BuildVariant(string rawLocation, bool jobIsRemote)
        {
            var location = Clean(rawLocation);
            if (location.Length == 0) return null;

            var usaStateCityMatch = UsaStateCityAddressPattern.Match(location);
            if (usaStateCityMatch.Success &&
                StatePrefixCodes.Contains(usaStateCityMatch.Groups["state"].Value))
            {
                var cityCandidate = CleanCityName(usaStateCityMatch.Groups["city"].Value);
                if (!string.IsNullOrWhiteSpace(cityCandidate) && !HasNonUsSignal(cityCandidate))
                {
                    location = $"{cityCandidate}, {usaStateCityMatch.Groups["state"].Value.ToUpperInvariant()}";
                }
            }

            var pathMatch = UsStatePathPattern.Match(location);
            if (pathMatch.Success)
            {
                location = $"{CleanCityName(pathMatch.Groups["city"].Value)}, {pathMatch.Groups["state"].Value.ToUpperInvariant()}";
            }

            var stateDashMatch = StateDashAddressPattern.Match(location);
            if (stateDashMatch.Success && StatePrefixCodes.Contains(stateDashMatch.Groups["state"].Value))
            {
                var cityCandidate = CleanCityName(stateDashMatch.Groups["city"].Value);
                if (!string.IsNullOrWhiteSpace(cityCandidate) && !HasNonUsSignal(cityCandidate))
                {
                    location = $"{cityCandidate}, {stateDashMatch.Groups["state"].Value.ToUpperInvariant()}";
                }
            }

            var prefixMatch = CountryPrefixPattern.Match(location);
            if (prefixMatch.Success)
            {
                var prefix = prefixMatch.Groups["prefix"].Value;
                var rest = prefixMatch.Groups["rest"].Value.Trim();
                if (prefix.Equals("US", StringComparison.OrdinalIgnoreCase))
                {
                    location = rest;
                }
                else if (StatePrefixCodes.Contains(prefix))
                {
                    if (HasNonUsSignal(rest))
                    {
                        return null;
                    }

                    location = $"{rest}, {prefix.ToUpperInvariant()}";
                }
                else
                {
                    return null;
                }
            }

            if (IsRemote(location))
            {
                return new LocationVariant("Remote", ShortLocationCode("Remote"), isRemote: true, isBroad: false);
            }

            if (IsBroadUs(location))
            {
                return new LocationVariant("United States", ShortLocationCode("United States"), isRemote: jobIsRemote, isBroad: true);
            }

            if (HasNonUsSignal(location) && !UsLocationFilter.IsUs(location))
            {
                return null;
            }

            LocationSplitter.Split(location, out var city, out var state);
            if (string.IsNullOrWhiteSpace(city))
            {
                if (!UsLocationFilter.IsUs(location)) return null;
                return new LocationVariant(location, ShortLocationCode(location), isRemote: jobIsRemote, isBroad: true);
            }

            var hasKnownCityState = false;
            if (string.IsNullOrWhiteSpace(state) &&
                LocationSplitter.TryGetKnownCityState(city, out var mappedCity, out var mappedState))
            {
                city = mappedCity;
                state = mappedState;
                hasKnownCityState = true;
            }

            if (string.IsNullOrWhiteSpace(state) &&
                !hasKnownCityState &&
                !UsLocationFilter.IsUs(location))
            {
                return null;
            }

            var canonical = string.IsNullOrWhiteSpace(state)
                ? city.Trim()
                : $"{city.Trim()}, {state.Trim().ToUpperInvariant()}";

            return new LocationVariant(canonical, ShortLocationCode(canonical), isRemote: jobIsRemote, isBroad: false);
        }

        private static ScrapedJob CloneForLocation(ScrapedJob job, LocationVariant variant, bool addLocationSuffix)
        {
            return new ScrapedJob
            {
                Title = job.Title,
                Company = job.Company,
                Location = variant.Location,
                Description = job.Description,
                JobUrl = job.JobUrl,
                JobType = job.JobType,
                IsRemote = job.IsRemote || variant.IsRemote,
                Category = job.Category,
                DatePosted = job.DatePosted,
                ExternalId = addLocationSuffix
                    ? WithLocationSuffix(job.ExternalId, job.JobUrl, job.Title, job.Company, variant.ReferenceSuffix)
                    : job.ExternalId,
                SourceId = job.SourceId,
                IsIT = job.IsIT,
                ITScore = job.ITScore
            };
        }

        private static string WithLocationSuffix(
            string externalId,
            string jobUrl,
            string title,
            string company,
            string locationSuffix)
        {
            var baseId = FirstNonEmpty(externalId, jobUrl, $"{company}:{title}");
            if (string.IsNullOrWhiteSpace(baseId))
            {
                baseId = Guid.NewGuid().ToString();
            }

            var suffix = $":L{locationSuffix}";
            var combined = $"{baseId}{suffix}";
            if (combined.Length <= 200) return combined;

            var keep = Math.Max(1, 200 - suffix.Length);
            return baseId.Substring(0, Math.Min(baseId.Length, keep)) + suffix;
        }

        private static bool ShouldCanonicalize(string original, string canonical)
        {
            var text = original ?? "";
            if (string.Equals((original ?? "").Trim(), canonical, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(canonical, "Remote", StringComparison.OrdinalIgnoreCase) &&
                IsRemote(text))
            {
                return true;
            }

            return text.Contains(";") || text.Contains("|");
        }

        private static bool IsRemote(string location)
        {
            return (location ?? "").IndexOf("remote", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsBroadUs(string location)
        {
            var text = (location ?? "").Trim();
            return Regex.IsMatch(text, @"^(united states|usa|u\.s\.a\.?|us|u\.s\.)$", RegexOptions.IgnoreCase);
        }

        private static bool HasNonUsSignal(string location)
        {
            var text = (location ?? "").ToLowerInvariant();
            return NonUsSignals.Any(text.Contains);
        }

        private static string Clean(string value)
        {
            value = (value ?? "").Trim();
            value = Regex.Replace(value, @"\s+", " ");
            return value;
        }

        private static string CleanCityName(string value)
        {
            value = Clean(value);
            value = Regex.Replace(value, @"\s*\(.*?\)\s*", " ").Trim();
            value = Regex.Replace(value, @"\s*[-–—]\s*\d.*$", "").Trim();
            value = Regex.Replace(value, @"\s+\d.*$", "").Trim();
            return value;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
        }

        private static string ShortLocationCode(string value)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
            var number =
                ((uint)bytes[0] << 24) |
                ((uint)bytes[1] << 16) |
                ((uint)bytes[2] << 8) |
                bytes[3];

            return ToBase36(number % 1679616).PadLeft(4, '0');
        }

        private static string ToBase36(uint number)
        {
            const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
            if (number == 0) return "0";

            var chars = new Stack<char>();
            while (number > 0)
            {
                chars.Push(alphabet[(int)(number % 36)]);
                number /= 36;
            }

            return new string(chars.ToArray());
        }

        private sealed class LocationVariant
        {
            public LocationVariant(string location, string referenceSuffix, bool isRemote, bool isBroad)
            {
                Location = location;
                ReferenceSuffix = referenceSuffix;
                IsRemote = isRemote;
                IsBroad = isBroad;
            }

            public string Location { get; }
            public string ReferenceSuffix { get; }
            public bool IsRemote { get; }
            public bool IsBroad { get; }
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




