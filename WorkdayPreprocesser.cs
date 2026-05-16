using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace LinkupFeed
{
  
    public class WorkdayPreprocesser
    {
        private readonly WorkdayScraperService _scraper;
        private readonly SqlConnection _db;

        public async Task<List<ScrapedJob>> ProcessAllTenantsAsync()
        {
            HttpClient client = new HttpClient();
            var results = new List<ScrapedJob>();
            WorkdayScraperService service = new WorkdayScraperService(client);
            foreach (var tenant in WorkdayTenants.USTechCompanies)
            {
                Console.WriteLine($"Processing: {tenant.Company}");
                int inserted = 0, skipped = 0;

                try
                {
                    await foreach (var job in service.FetchAllJobsAsync(tenant))
                    {
                        //// Duplicate check by external URL
                        var applyUrl = service.GetApplyUrl(tenant, job.ExternalPath);
                       

                        // Optionally fetch full description
                         //var workdayjobdetail = await service.FetchJobDetailAsync(tenant, job.ExternalPath);

                        

                        results.Add(new ScrapedJob
                        {
                            SourceId =99,
                            ExternalId = job.JobReqId,
                            Title = job.Title,
                            Company = tenant.Company,
                            Location = job.Location,
                            Description ="",  //workdayjobdetail.Description,
                            JobUrl = applyUrl,
                            JobType=job.TimeType,
                            IsRemote = true,
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
    }
}
