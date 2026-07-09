using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace LinkupFeed
{
    

    // ── WorkdayTenant.cs ─────────────────────────────────────────────
    public class WorkdayTenant
    {
        public string Company { get; set; }  // display name
        public string Tenant { get; set; }  // subdomain
        public string Site { get; set; }  // board name
        public string WdServer { get; set; }  // wd1, wd3, wd5, wd12...
    }

    // ── WorkdayJob.cs ────────────────────────────────────────────────
    public class WorkdayJobPosting
    {
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("externalPath")] public string ExternalPath { get; set; }
        [JsonPropertyName("locationsText")] public string Location { get; set; }
        [JsonPropertyName("postedOn")] public string PostedOn { get; set; }
        [JsonPropertyName("jobReqId")] public string JobReqId { get; set; }
        [JsonPropertyName("employmentType")] public string EmploymentType { get; set; }
        [JsonPropertyName("timeType")] public string TimeType { get; set; }
    }

    public class WorkdayJobDetail : WorkdayJobPosting
    {
        [JsonPropertyName("jobDescription")] public string Description { get; set; }
    }

    public class WorkdayJobDetailResponse
    {
        [JsonPropertyName("jobPostingInfo")] public WorkdayJobDetail JobPostingInfo { get; set; }
    }

    public class WorkdayResponse
    {
        [JsonPropertyName("total")] public int Total { get; set; }
        [JsonPropertyName("jobPostings")] public List<WorkdayJobPosting> JobPostings { get; set; }
    }

    // ── WorkdayScraperService.cs ─────────────────────────────────────
    public class WorkdayScraperService
    {
        private readonly HttpClient _client;

        public WorkdayScraperService(HttpClient client)
        {
            _client = client;
            _client.DefaultRequestHeaders.Add("Accept", "application/json");
            _client.DefaultRequestHeaders.Add("Accept-Language", "en-US");
            _client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        private string BaseUrl(WorkdayTenant t) =>
            $"https://{t.Tenant}.{t.WdServer}.myworkdayjobs.com";

        // ── 1. Fetch one page of listings ───────────────────────────
        public async Task<WorkdayResponse> FetchListingsAsync(
            WorkdayTenant t, int limit = 20, int offset = 0, string searchText = "")
        {
            var url = $"{BaseUrl(t)}/wday/cxs/{t.Tenant}/{t.Site}/jobs";

            _client.DefaultRequestHeaders.Remove("Referer");
            _client.DefaultRequestHeaders.Add("Referer",
                $"{BaseUrl(t)}/en-US/{t.Site}");

            var payload = new
            {
                appliedFacets = new { },
                limit,
                offset,
                searchText
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await _client.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<WorkdayResponse>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        // ── 2. Fetch full job detail ─────────────────────────────────
        public async Task<WorkdayJobDetail> FetchJobDetailAsync(
            WorkdayTenant t, string externalPath)
        {
            // externalPath looks like: Software-Engineer_JR-123456
            var path = NormalizeExternalPath(externalPath);
            var url = $"{BaseUrl(t)}/wday/cxs/{t.Tenant}/{t.Site}/job/{path}";

            var response = await _client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var nested = JsonSerializer.Deserialize<WorkdayJobDetailResponse>(body, options);
            return nested?.JobPostingInfo ?? JsonSerializer.Deserialize<WorkdayJobDetail>(body, options);
        }

        private static string NormalizeExternalPath(string externalPath)
        {
            var path = (externalPath ?? "").Trim();
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
            {
                path = uri.AbsolutePath;
            }

            path = path.TrimStart('/');
            if (path.StartsWith("job/", StringComparison.OrdinalIgnoreCase)) path = path.Substring(4);
            return path;
        }

        // ── 3. Paginate all jobs for a tenant ────────────────────────
        public async IAsyncEnumerable<WorkdayJobPosting> FetchAllJobsAsync(
            WorkdayTenant t, string searchText = "", int pageSize = 20)
        {
            int offset = 0;

            while (true)
            {
                var result = await FetchListingsAsync(t, pageSize, offset, searchText);

                if (result?.JobPostings == null || result.JobPostings.Count == 0)
                    yield break;

                foreach (var job in result.JobPostings)
                    yield return job;

                offset += pageSize;
                if (offset >= result.Total) yield break;

                await Task.Delay(1500); // be polite
            }
        }

        // ── 4. Build apply URL from externalPath ─────────────────────
        //public string GetApplyUrl(WorkdayTenant t, string externalPath) =>
        //    $"{BaseUrl(t)}/en-US/{t.Site}/job/{externalPath}";

        public string GetApplyUrl(WorkdayTenant t, string externalPath) =>
    $"{BaseUrl(t)}/en-US/{t.Site}{externalPath}";
    }
}
