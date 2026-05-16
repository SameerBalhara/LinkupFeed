using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

namespace LinkupFeed
{
    internal class JobicyScraper
    {
        private const int SOURCE_ID = 50;
        private const string BASE_URL =
            "https://jobicy.com/api/v2/remote-jobs?count=50&industry={0}";

        // Industries: "technology", "design", "marketing", "sales", "finance"
        private static readonly string[] Industries = { "technology", "design" };

        public async Task<List<ScrapedJob>> FetchJobsAsync()
        {
            var results = new List<ScrapedJob>();

            foreach (var industry in Industries)
            {
                try
                {
                    var url = string.Format(BASE_URL, industry);
                    var json = await Http.GetStringAsync(url);
                    var root = JsonDocument.Parse(json).RootElement;

                    if (!root.TryGetProperty("jobs", out var jobsEl)) continue;

                    foreach (var j in jobsEl.EnumerateArray())
                    {
                        results.Add(new ScrapedJob
                        {
                            SourceId = SOURCE_ID,
                            ExternalId = j.GetStringOrNull("id"),
                            Title = j.GetStringOrNull("jobTitle"),
                            Company = j.GetStringOrNull("companyName"),
                            Location = j.GetStringOrNull("jobGeo") ?? "Remote",
                            Description = j.GetStringOrNull("jobExcerpt"),
                            JobUrl = j.GetStringOrNull("url"),
                            JobType = j.GetStringOrNull("jobType"),
                            IsRemote = true,
                            Category = j.GetStringOrNull("jobIndustry"),
                            DatePosted = j.TryGetProperty("pubDate", out var pd)
                                            ? DateTime.TryParse(pd.GetString(), out var dt) ? dt : (DateTime?)null
                                            : null
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Jobicy] {industry} error: {ex.Message}");
                }

                await Task.Delay(1000); // polite delay
            }

            return results;
        }
    }
}
