using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

namespace LinkupFeed
{
    internal class ArbeitnowScraper
    {
        private const int SOURCE_ID = 53;
        // Filter by English-language / USA-visible postings
        private const string URL =
            "https://www.arbeitnow.com/api/job-board-api?page=1";

        public async Task<List<ScrapedJob>> FetchJobsAsync(int maxPages = 3)
        {
            var results = new List<ScrapedJob>();

            for (int page = 1; page <= maxPages; page++)
            {
                try
                {
                    var url = $"https://www.arbeitnow.com/api/job-board-api?page={page}";
                    var json = await Http.GetStringAsync(url);
                    var root = JsonDocument.Parse(json).RootElement;

                    if (!root.TryGetProperty("data", out var dataEl)) break;
                    var items = dataEl.EnumerateArray().ToList();
                    if (!items.Any()) break;

                    foreach (var j in items)
                    {
                        var tags = j.TryGetProperty("tags", out var tagsEl)
                            ? string.Join(", ", tagsEl.EnumerateArray()
                                .Select(t => t.GetString()))
                            : "";

                        results.Add(new ScrapedJob
                        {
                            SourceId = SOURCE_ID,
                            ExternalId = j.GetStringOrNull("slug"),
                            Title = j.GetStringOrNull("title"),
                            Company = j.GetStringOrNull("company_name"),
                            Location = j.GetStringOrNull("location") ?? "Remote",
                            Description = j.GetStringOrNull("description"),
                            JobUrl = j.GetStringOrNull("url"),
                            IsRemote = j.TryGetProperty("remote", out var rem)
                                            && rem.GetBoolean(),
                            Category = tags,
                            DatePosted = j.TryGetProperty("created_at", out var ca)
                                            ? DateTimeOffset.FromUnixTimeSeconds(ca.GetInt64()).DateTime
                                            : (DateTime?)null
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Arbeitnow] page {page} error: {ex.Message}");
                    break;
                }

                await Task.Delay(1000);
            }

            return results;
        }
    }
}
