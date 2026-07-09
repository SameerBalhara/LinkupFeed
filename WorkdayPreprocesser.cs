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
        private readonly WorkdayScraperService _scraper;
        private readonly SqlConnection _db;

        public async Task<List<ScrapedJob>> ProcessAllTenantsAsync(int? limitTenants = null)
        {
            HttpClient client = new HttpClient();
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
                        if (ItJobFilter.IsIt(job.Title, job.TimeType))
                        {
                            try
                            {
                                var workdayjobdetail = await service.FetchJobDetailAsync(tenant, job.ExternalPath);
                                description = CleanDescription(workdayjobdetail?.Description);
                            }
                            catch
                            {
                                description = "";
                            }
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
                            JobType=job.TimeType,
                            IsRemote = job.Location?.IndexOf("remote", StringComparison.OrdinalIgnoreCase) >= 0,
                            DatePosted = DateTime.TryParse(job.PostedOn, out var dt) ? dt : (DateTime?)null
                        });                       
                        inserted++;                


                    }

                    Console.WriteLine($"  {tenant.Company}: {inserted} inserted, {skipped} skipped");
                }
                catch (HttpRequestException ex)
                {
                    // Some tenants return 401/422 — log and continue
                    Console.WriteLine($"  {tenant.Company}: FAILED — {ex.Message}");
                   // await LogToITJClogsAsync(tenant.Company, ex.Message);
                }

                await Task.Delay(3000); // delay between companies
            }
            return results;
        }

        private static string CleanDescription(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var text = WebUtility.HtmlDecode(Regex.Replace(value, "<[^>]+>", " "));
            return Regex.Replace(text, @"\s+", " ").Trim();
        }
    }
}



