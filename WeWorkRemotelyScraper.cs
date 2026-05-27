using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using static System.Net.WebRequestMethods;

namespace LinkupFeed
{
    internal class WeWorkRemotelyScraper
    {

        private const int SOURCE_ID = 52;
        private static readonly string[] Feeds = {
            "https://weworkremotely.com/categories/remote-programming-jobs.rss",
            "https://weworkremotely.com/categories/remote-back-end-programming-jobs.rss",
            "https://weworkremotely.com/categories/remote-front-end-programming-jobs.rss",
            "https://weworkremotely.com/categories/remote-full-stack-programming-jobs.rss",
            "https://weworkremotely.com/categories/remote-devops-sysadmin-jobs.rss",
            "https://weworkremotely.com/categories/remote-product-jobs.rss",
            "https://weworkremotely.com/remote-jobs.rss"
        };

        public async Task<List<ScrapedJob>> FetchJobsAsync()
        {
            var results = new List<ScrapedJob>();

            foreach (var feedUrl in Feeds)
            {
                try
                {
                    var xml = await Http.GetStringAsync(feedUrl);
                    var doc = XDocument.Parse(xml);
                    XNamespace ns = "https://weworkremotely.com/";

                    foreach (var item in doc.Descendants("item"))
                    {
                        var title = item.Element("title")?.Value ?? "";
                        var company = item.Element(ns + "company")?.Value
                                    ?? ExtractCompanyFromTitle(title);
                        var region = item.Element(ns + "region")?.Value ?? "USA";
                        var link = item.Element("link")?.Value
                                    ?? item.Elements()
                                           .FirstOrDefault(e => e.Name.LocalName == "link")
                                           ?.Value ?? "";
                        var guid = Guid.NewGuid().ToString();
                        var pubDate = item.Element("pubDate")?.Value;
                        var desc = item.Element("description")?.Value ?? "";

                        // Skip non-USA postings if region is specified
                        if (!string.IsNullOrEmpty(region) &&
                            !region.Contains("USA", StringComparison.OrdinalIgnoreCase) &&
                            !region.Contains("Worldwide", StringComparison.OrdinalIgnoreCase) &&
                            !region.Contains("Anywhere", StringComparison.OrdinalIgnoreCase))
                            continue;

                        results.Add(new ScrapedJob
                        {
                            SourceId = SOURCE_ID,
                            ExternalId = guid,
                            Title = CleanTitle(title),
                            Company = company,
                            Location = region,
                            Description = StripHtml(desc),
                            JobUrl = link,
                            IsRemote = true,
                            DatePosted = DateTime.TryParse(pubDate, out var dt) ? dt : (DateTime?)null
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WWR] {feedUrl} error: {ex.Message}");
                }

                await Task.Delay(1500);
            }

            return results;
        }

        // WWR titles look like: "Company Name | Job Title at Company Name"
        private static string CleanTitle(string t) =>
            t.Contains(" at ") ? t.Split(" at ")[0].Trim() : t;
        private static string ExtractCompanyFromTitle(string t) =>
            t.Contains(" at ") ? t.Split(" at ").Last().Trim() : "";
        private static string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return html;
            return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ").Trim();
        }
    }
}
