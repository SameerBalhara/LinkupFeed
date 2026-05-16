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
    };
    }
}
