using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LinkupFeed
{
    internal class HelperClass
    {


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
