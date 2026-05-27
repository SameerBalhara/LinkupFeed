using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LinkupFeed
{
   

    public static class WorkdayTenants
    {
        public static readonly List<WorkdayTenant> USTechCompanies = new()
    {
        // ── Big Tech ──────────────────────────────────────────────
        new() { Company="NVIDIA",      Tenant="nvidia",      Site="NVIDIAExternalCareerSite", WdServer="wd5"  },
        new() { Company="Adobe",       Tenant="adobe",       Site="external",                 WdServer="wd5"  },
        new() { Company="Salesforce",  Tenant="salesforce",  Site="External",                 WdServer="wd5"  },
        new() { Company="Intuit",      Tenant="intuit",      Site="careers",                  WdServer="wd5"  },
        new() { Company="PayPal",      Tenant="paypal",      Site="jobs",                     WdServer="wd5"  },
        new() { Company="Workday",     Tenant="workday",     Site="Workday",                  WdServer="wd5"  },

        // ── Finance / Banking ─────────────────────────────────────
        new() { Company="Capital One", Tenant="capitalone",  Site="Capital_One",              WdServer="wd12" },
        new() { Company="Bank of America", Tenant="bofa",    Site="Global",                   WdServer="wd1"  },
        new() { Company="Fidelity",    Tenant="fidelity",    Site="external",                 WdServer="wd5"  },
        new() { Company="Vanguard",    Tenant="vanguard",    Site="college",                  WdServer="wd5"  },

        // ── Healthcare / Pharma ────────────────────────────────────
        new() { Company="Pfizer",      Tenant="pfizer",      Site="External",                 WdServer="wd5"  },
        new() { Company="J&J",         Tenant="jnj",         Site="jnjexternalcareers",       WdServer="wd5"  },
        new() { Company="UnitedHealth",Tenant="uhg",         Site="Careers",                  WdServer="wd5"  },

        // ── Retail / Consumer ──────────────────────────────────────
        new() { Company="Target",      Tenant="target",      Site="TargetCareers",            WdServer="wd5"  },
        new() { Company="Nike",        Tenant="nike",        Site="global",                   WdServer="wd5"  },
        new() { Company="Walmart",     Tenant="walmart",     Site="Walmart_Web",              WdServer="wd5"  },

        // ── Aerospace / Defense ────────────────────────────────────
        new() { Company="Boeing",      Tenant="boeing",      Site="EXTERNAL_CAREERS",         WdServer="wd1"  },
        new() { Company="Raytheon",    Tenant="rtx",         Site="RTXCareers",               WdServer="wd5"  },
        new() { Company="Lockheed",    Tenant="lmcocareers", Site="LMCareers",                WdServer="wd5"  },

        // ── Added batch: 50 additional US Workday tenants ─────────
        // NOTE: tenant/site/wdServer values curated from public Workday job search URLs.
        // Verify each via https://{tenant}.{wdServer}.myworkdayjobs.com/{site} before relying on it;
        // Workday occasionally rotates the wdN data center or renames the site slug.

        // ── Tech / Semiconductors ─────────────────────────────────
        new() { Company="Cisco",            Tenant="cisco",        Site="External",                       WdServer="wd5"  },
        new() { Company="ServiceNow",       Tenant="servicenow",   Site="ServiceNowExternalCareer",       WdServer="wd1"  },
        new() { Company="Autodesk",         Tenant="autodesk",     Site="Ext",                            WdServer="wd1"  },
        new() { Company="AMD",              Tenant="amd",          Site="External",                       WdServer="wd1"  },
        new() { Company="Qualcomm",         Tenant="qualcomm",     Site="External",                       WdServer="wd5"  },
        new() { Company="Broadcom",         Tenant="broadcom",     Site="External_Career_Site",           WdServer="wd1"  },
        new() { Company="Dell",             Tenant="dell",         Site="External",                       WdServer="wd1"  },
        new() { Company="HP",               Tenant="hp",           Site="ExternalCareerSite",             WdServer="wd5"  },
        new() { Company="HPE",              Tenant="hpe",          Site="Jobsathpe",                      WdServer="wd5"  },
        new() { Company="VMware",           Tenant="vmware",       Site="VMware",                         WdServer="wd1"  },
        new() { Company="Micron",           Tenant="micron",       Site="External",                       WdServer="wd1"  },
        new() { Company="NXP",              Tenant="nxp",          Site="careers",                        WdServer="wd3"  },
        new() { Company="Analog Devices",   Tenant="analog",       Site="External",                       WdServer="wd1"  },

        // ── Telecom / Media ───────────────────────────────────────
        new() { Company="AT&T",             Tenant="att",          Site="ATTExternal",                    WdServer="wd1"  },
        new() { Company="Verizon",          Tenant="verizon",      Site="External",                       WdServer="wd5"  },
        new() { Company="T-Mobile",         Tenant="t-mobile",     Site="External",                       WdServer="wd1"  },
        new() { Company="Comcast",          Tenant="comcast",      Site="Comcast_Careers",                WdServer="wd5"  },
        new() { Company="Charter Spectrum", Tenant="charter",      Site="External",                       WdServer="wd5"  },
        new() { Company="Cox",              Tenant="cox",          Site="External",                       WdServer="wd5"  },

        // ── Finance / Banking / Cards ─────────────────────────────
        new() { Company="Mastercard",       Tenant="mastercard",   Site="CorporateCareers",               WdServer="wd1"  },
        new() { Company="Visa",             Tenant="visa",         Site="Visa",                           WdServer="wd1"  },
        new() { Company="American Express", Tenant="aexp",         Site="External",                       WdServer="wd1"  },
        new() { Company="JPMorgan Chase",   Tenant="jpmc",         Site="ExternalSite",                   WdServer="wd5"  },
        new() { Company="Morgan Stanley",   Tenant="morganstanley",Site="External",                       WdServer="wd5"  },
        new() { Company="BlackRock",        Tenant="blackrock",    Site="Professional",                   WdServer="wd1"  },
        new() { Company="Charles Schwab",   Tenant="schwab",       Site="External",                       WdServer="wd1"  },
        new() { Company="Ally",             Tenant="ally",         Site="Ally_External_Careers",          WdServer="wd5"  },
        new() { Company="Discover",         Tenant="discover",     Site="Discover",                       WdServer="wd5"  },

        // ── Insurance / Health Plans ──────────────────────────────
        new() { Company="Cigna",            Tenant="cigna",        Site="cignacareers",                   WdServer="wd5"  },
        new() { Company="Humana",           Tenant="humana",       Site="Humana_External_Career_Site",    WdServer="wd1"  },
        new() { Company="Elevance Health",  Tenant="elevancehealth",Site="ANT_External",                  WdServer="wd1"  },

        // ── Pharma / MedDev ───────────────────────────────────────
        new() { Company="Merck",            Tenant="merck",        Site="SearchJobs",                     WdServer="wd5"  },
        new() { Company="AstraZeneca",      Tenant="astrazeneca",  Site="Careers",                        WdServer="wd3"  },
        new() { Company="GSK",              Tenant="gsk",          Site="GSKCareers",                     WdServer="wd5"  },
        new() { Company="Moderna",          Tenant="moderna",      Site="M_tx",                           WdServer="wd1"  },
        new() { Company="AbbVie",           Tenant="abbvie",       Site="External",                       WdServer="wd5"  },
        new() { Company="Eli Lilly",        Tenant="lilly",        Site="LLY",                            WdServer="wd5"  },
        new() { Company="Medtronic",        Tenant="medtronic",    Site="MedtronicCareers",               WdServer="wd1"  },
        new() { Company="Abbott",           Tenant="abbott",       Site="abbottcareers",                  WdServer="wd5"  },

        // ── Industrial / Aerospace ────────────────────────────────
        new() { Company="Siemens",          Tenant="siemens",      Site="External",                       WdServer="wd1"  },
        new() { Company="Honeywell",        Tenant="honeywell",    Site="Honeywellexternal",              WdServer="wd1"  },
        new() { Company="GE Aerospace",     Tenant="ge",           Site="GE_External",                    WdServer="wd5"  },

        // ── Consumer / Food / Bev ─────────────────────────────────
        new() { Company="PepsiCo",          Tenant="pepsico",      Site="PepsiCoJobs",                    WdServer="wd5"  },
        new() { Company="Coca-Cola",        Tenant="coca-cola",    Site="coca-cola_career_site",          WdServer="wd1"  },
        new() { Company="McDonald's",       Tenant="mcdonalds",    Site="McDonalds",                      WdServer="wd5"  },

        // ── Travel / Hospitality ──────────────────────────────────
        new() { Company="Delta Air Lines",  Tenant="delta",        Site="DeltaCareers",                   WdServer="wd5"  },
        new() { Company="United Airlines",  Tenant="united",       Site="ualcareers",                     WdServer="wd5"  },
        new() { Company="American Airlines",Tenant="aa",           Site="American_Airlines_Career_Site",  WdServer="wd1"  },
        new() { Company="Marriott",         Tenant="marriott",     Site="Marriott",                       WdServer="wd1"  },
        new() { Company="Hilton",           Tenant="hilton",       Site="Hilton_Worldwide",               WdServer="wd1"  },

        // ── Added batch: more US tech/IT-heavy Workday tenants ─────
        // Curated from public Workday job-search URLs. Verify each via
        // https://{tenant}.{wdServer}.myworkdayjobs.com/{site} — Workday rotates
        // the wdN data center / renames site slugs; bad entries just error out.
        new() { Company="Snowflake",        Tenant="snowflake",    Site="careers",                        WdServer="wd1"  },
        new() { Company="Splunk",           Tenant="splunk",       Site="careers",                        WdServer="wd1"  },
        new() { Company="DocuSign",         Tenant="docusign",     Site="Docusign",                       WdServer="wd1"  },
        new() { Company="Roku",             Tenant="roku",         Site="Roku",                           WdServer="wd5"  },
        new() { Company="Zillow",           Tenant="zillow",       Site="Zillow_Group_External",          WdServer="wd5"  },
        new() { Company="Pure Storage",     Tenant="purestorage",  Site="PureStorage",                    WdServer="wd1"  },
        new() { Company="NetApp",           Tenant="netapp",       Site="careers",                        WdServer="wd1"  },
        new() { Company="Nutanix",          Tenant="nutanix",      Site="Nutanix",                        WdServer="wd1"  },
        new() { Company="Akamai",           Tenant="akamai",       Site="Akamai_Careers",                 WdServer="wd1"  },
        new() { Company="Synopsys",         Tenant="synopsys",     Site="Careers",                        WdServer="wd1"  },
        new() { Company="Cadence",          Tenant="cadence",      Site="External_Careers",               WdServer="wd1"  },
        new() { Company="KLA",              Tenant="kla",          Site="Search",                         WdServer="wd1"  },
        new() { Company="Applied Materials",Tenant="amat",         Site="External",                       WdServer="wd5"  },
        new() { Company="Lam Research",     Tenant="lamresearch",  Site="External",                       WdServer="wd1"  },
        new() { Company="Marvell",          Tenant="marvell",      Site="MarvellCareers2",                WdServer="wd1"  },
        new() { Company="Texas Instruments",Tenant="ti",           Site="External_US",                    WdServer="wd1"  },
        new() { Company="Western Digital",  Tenant="wdc",          Site="External",                       WdServer="wd1"  },
        new() { Company="Teradata",         Tenant="teradata",     Site="External",                       WdServer="wd5"  },
        new() { Company="Workiva",          Tenant="workiva",      Site="Workiva",                        WdServer="wd1"  },
        new() { Company="Sonos",            Tenant="sonos",        Site="Sonos",                          WdServer="wd1"  },
    };
    }
}
