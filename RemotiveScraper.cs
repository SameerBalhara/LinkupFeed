using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LinkupFeed
{
    internal class RemotiveScraper
    {
        private const int SOURCE_ID = 51;
        private const int LimitPerCategory = 200;

        // IT-relevant Remotive categories (slugs from remotive.com/api/remote-jobs).
        private static readonly string[] Categories =
        {
            "software-dev", "data", "devops", "qa", "product"
        };

        public async Task<List<ScrapedJob>> FetchJobsAsync()
        {
            var results = new List<ScrapedJob>();

            foreach (var category in Categories)
            {
                try
                {
                    var url = $"https://remotive.com/api/remote-jobs?category={category}&limit={LimitPerCategory}";
                    var json = await Http.GetStringAsync(url);
                    var root = JsonDocument.Parse(json).RootElement;

                    if (!root.TryGetProperty("jobs", out var jobsEl)) continue;

                    foreach (var j in jobsEl.EnumerateArray())
                    {
                    // Filter USA-relevant: job_geo contains US, Worldwide, or blank
                    var geo = j.GetStringOrNull("candidate_required_location") ?? "";
                    if (!string.IsNullOrEmpty(geo) &&
                        !geo.Contains("USA", StringComparison.OrdinalIgnoreCase) &&
                        !geo.Contains("US", StringComparison.OrdinalIgnoreCase) &&
                        !geo.Contains("Worldwide", StringComparison.OrdinalIgnoreCase) &&
                        !geo.Contains("Anywhere", StringComparison.OrdinalIgnoreCase))
                        continue;

                    results.Add(new ScrapedJob
                    {
                        SourceId = SOURCE_ID,
                        ExternalId = j.TryGetProperty("id", out var idEl)
                                        ? idEl.GetInt32().ToString() : null,
                        Title = j.GetStringOrNull("title"),
                        Company = j.GetStringOrNull("company_name"),
                        Location = geo.Length > 0 ? geo : "Remote / USA",
                        Description = StripHtml(j.GetStringOrNull("description")),
                        JobUrl = j.GetStringOrNull("url"),
                        JobType = j.GetStringOrNull("job_type"),
                        IsRemote = true,
                        Category = j.GetStringOrNull("category"),
                        DatePosted = j.TryGetProperty("publication_date", out var pd)
                                        ? DateTime.TryParse(pd.GetString(), out var dt) ? dt : (DateTime?)null
                                        : null
                    });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Remotive] {category} error: {ex.Message}");
                }

                await Task.Delay(1000); // polite delay
            }

            return results;
        }


        private static string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return html;
            return System.Text.RegularExpressions.Regex
                .Replace(html, "<[^>]+>", " ")
                .Replace("&amp;", "&").Replace("&nbsp;", " ")
                .Trim();
        }
    }
}