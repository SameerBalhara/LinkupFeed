using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace LinkupFeed
{
    internal static class Http
    {
        private static readonly HttpClient _client = new HttpClient();
        static Http()
        {
            _client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (compatible; ITJobCafeBot/1.0; +https://itjobcafe.com)");
            _client.Timeout = TimeSpan.FromSeconds(30);
        }
        public static Task<string> GetStringAsync(string url) =>
            _client.GetStringAsync(url);
    }

    public class ScrapedJob
    {
        public string Title { get; set; }
        public string Company { get; set; }
        public string Location { get; set; }
        public string Description { get; set; }
        public string JobUrl { get; set; }
        public string JobType { get; set; }   // fulltime / parttime / contract
        public bool IsRemote { get; set; }
        public string Category { get; set; }
        public DateTime? DatePosted { get; set; }
        public string ExternalId { get; set; }   // source's own ID (for dedup)
        public int SourceId { get; set; }
        public bool IsIT { get; set; }
        public int ITScore { get; set; }
    }
}
