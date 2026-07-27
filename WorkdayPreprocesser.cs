using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;

namespace LinkupFeed
{
  
    public class WorkdayPreprocesser
    {
        public async Task<List<ScrapedJob>> ProcessAllTenantsAsync(int? limitTenants = null)
        {
            HttpClient client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            var results = new List<ScrapedJob>();
            WorkdayScraperService service = new WorkdayScraperService(client);
            var tenants = limitTenants.HasValue && limitTenants.Value > 0
                ? WorkdayTenants.USTechCompanies.Take(limitTenants.Value).ToList()
                : WorkdayTenants.USTechCompanies;

            foreach (var tenant in tenants)
            {
                Console.WriteLine($"Processing: {tenant.Company}");
                int inserted = 0, skipped = 0;

                try
                {
                    await foreach (var job in service.FetchAllJobsAsync(tenant))
                    {
                        //// Duplicate check by external URL
                        var applyUrl = service.GetApplyUrl(tenant, job.ExternalPath);
                       

                        var description = "";
                        WorkdayJobDetail workdayjobdetail = null;
                        try
                        {
                            workdayjobdetail = await service.FetchJobDetailAsync(tenant, job.ExternalPath);
                            description = CleanDescription(workdayjobdetail?.Description);
                        }
                        catch
                        {
                            description = "";
                        }

                        

                        results.Add(new ScrapedJob
                        {
                            SourceId =99,
                            ExternalId = job.JobReqId,
                            Title = job.Title,
                            Company = tenant.Company,
                            Location = job.Location,
                            Description = description,
                            JobUrl = applyUrl,
                            JobType = FirstNonEmpty(job.TimeType, workdayjobdetail?.TimeType, workdayjobdetail?.EmploymentType),
                            IsRemote = job.Location?.IndexOf("remote", StringComparison.OrdinalIgnoreCase) >= 0,
                            DatePosted = ParseWorkdayDate(FirstNonEmpty(
                                workdayjobdetail?.StartDate,
                                job.StartDate,
                                workdayjobdetail?.PostedOn,
                                job.PostedOn))
                        });                       
                        inserted++;                


                    }

                    Console.WriteLine($"  {tenant.Company}: {inserted} inserted, {skipped} skipped");
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException)
                {
                    // Some tenants return 401/422 — log and continue
                    Console.WriteLine($"  {tenant.Company}: FAILED — {ex.Message}");
                   // await LogToITJClogsAsync(tenant.Company, ex.Message);
                }

                await Task.Delay(3000); // delay between companies
            }
            return results;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }

            return "";
        }

        private static DateTime? ParseWorkdayDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var text = value.Trim();
            if (text.Equals("Posted Today", StringComparison.OrdinalIgnoreCase) || text.Equals("Today", StringComparison.OrdinalIgnoreCase)) return DateTime.Today;
            if (text.Equals("Posted Yesterday", StringComparison.OrdinalIgnoreCase) || text.Equals("Yesterday", StringComparison.OrdinalIgnoreCase)) return DateTime.Today.AddDays(-1);
            var dayMatch = Regex.Match(text, @"Posted\s+(\d+)\+?\s+Days?\s+Ago", RegexOptions.IgnoreCase);
            if (dayMatch.Success && int.TryParse(dayMatch.Groups[1].Value, out var days)) return DateTime.Today.AddDays(-days);
            return DateTime.TryParse(text, out var parsed) ? parsed : null;
        }

        private static string CleanDescription(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var text = WebUtility.HtmlDecode(Regex.Replace(value, "<[^>]+>", " "));
            return Regex.Replace(text, @"\s+", " ").Trim();
        }
    }
}



