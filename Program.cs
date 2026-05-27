using System;
using System.Collections.Generic;
using System.Linq;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml;

namespace LinkupFeed
{
    class Program
    {
        static async System.Threading.Tasks.Task Main(string[] args)
        {
            // await RemoteJobsScraping();

            if (args != null && args.Length > 0 &&
                string.Equals(args[0], "taleo", StringComparison.OrdinalIgnoreCase))
            {
                await RunTaleoOnlyAsync();
                return;
            }

            if (args != null && args.Length > 0 &&
                string.Equals(args[0], "successfactors", StringComparison.OrdinalIgnoreCase))
            {
                await RunSuccessFactorsOnlyAsync();
                return;
            }

            if (args != null && args.Length > 0 &&
                string.Equals(args[0], "lever", StringComparison.OrdinalIgnoreCase))
            {
                await RunLeverOnlyAsync();
                return;
            }

            SqlConnection Sqlconn = new SqlConnection("Data source=209.59.189.133\\ITJOBCAFESERVER,1435;Initial Catalog=feeds;User Id=itjobcafe;Pwd=Chand@789!");
            Sqlconn.Open();
            Console.WriteLine($"[DB] Connection opened: state={Sqlconn.State}");

            try
            {
                var jobs = await new FreeJobScraperOrchestrator().RunAllAsync();

                // US-only filter
                int freeBefore = jobs.Count;
                jobs = jobs.Where(j => UsLocationFilter.IsUs(j.Location)).ToList();
                Console.WriteLine($"[Free scrapers] US filter: {freeBefore} -> {jobs.Count}");

                // IT-only filter
                int freeItBefore = jobs.Count;
                jobs = jobs.Where(j => ItJobFilter.IsIt(j.Title, j.Category)).ToList();
                Console.WriteLine($"[Free scrapers] IT filter: {freeItBefore} -> {jobs.Count}");

                // Plug into your existing ETL:
                int freeOk = 0, freeFail = 0;
                foreach (var job in jobs)
                {
                    try
                    {
                        job.ExternalId = Guid.NewGuid().ToString();
                        FeedInsert.Post_Jobs(job.Location, job.Title, LocationSplitter.CityOf(job.Location), LocationSplitter.StateOf(job.Location), "", "USA", job.JobType, job.DatePosted.ToString(), job.ExternalId, job.Company, job.IsRemote == true ? 1 : 0, job.Category, "", job.JobUrl, job.Description, "", Sqlconn);
                        freeOk++;
                    }
                    catch (Exception jobEx)
                    {
                        freeFail++;
                        Console.WriteLine($"[Insert-Free] FAILED ExternalId={job.ExternalId} Source={job.SourceId} — {jobEx.Message}");
                    }
                }
                Console.WriteLine($"[Free scrapers] Attempted={jobs.Count} Inserted={freeOk} Failed={freeFail}");

                //--workday jobs
                var workdayjobs = await new WorkdayPreprocesser().ProcessAllTenantsAsync();

                // US-only filter
                int wdBefore = workdayjobs.Count;
                workdayjobs = workdayjobs.Where(j => UsLocationFilter.IsUs(j.Location)).ToList();
                Console.WriteLine($"[Workday] US filter: {wdBefore} -> {workdayjobs.Count}");

                int wdItBefore = workdayjobs.Count;
                workdayjobs = workdayjobs.Where(j => ItJobFilter.IsIt(j.Title, j.Category)).ToList();
                Console.WriteLine($"[Workday] IT filter: {wdItBefore} -> {workdayjobs.Count}");

                int wdOk = 0, wdFail = 0;
                foreach (var job in workdayjobs)
                {
                    try
                    {
                        job.ExternalId = Guid.NewGuid().ToString();
                        FeedInsert.Post_Jobs(job.Location, job.Title, LocationSplitter.CityOf(job.Location), LocationSplitter.StateOf(job.Location), "", "USA", job.JobType, job.DatePosted.ToString(), job.ExternalId, job.Company, job.IsRemote == true ? 1 : 0, job.Category, "", job.JobUrl, job.Description, "", Sqlconn);
                        wdOk++;
                    }
                    catch (Exception jobEx)
                    {
                        wdFail++;
                        Console.WriteLine($"[Insert-Workday] FAILED ExternalId={job.ExternalId} Company={job.Company} — {jobEx.Message}");
                    }
                }
                Console.WriteLine($"[Workday] Attempted={workdayjobs.Count} Inserted={wdOk} Failed={wdFail}");

                //--SuccessFactors jobs
                var sfJobs = await new SuccessFactorsScraper().FetchJobsAsync();

                int sfBefore = sfJobs.Count;
                sfJobs = sfJobs.Where(j => UsLocationFilter.IsUs(j.Location)).ToList();
                Console.WriteLine($"[SuccessFactors] US filter: {sfBefore} -> {sfJobs.Count}");

                int sfItBefore = sfJobs.Count;
                sfJobs = sfJobs.Where(j => ItJobFilter.IsIt(j.Title, j.Category)).ToList();
                Console.WriteLine($"[SuccessFactors] IT filter: {sfItBefore} -> {sfJobs.Count}");

                int sfOk = 0, sfFail = 0;
                foreach (var job in sfJobs)
                {
                    try
                    {
                        job.ExternalId = Guid.NewGuid().ToString();
                        FeedInsert.Post_Jobs(job.Location, job.Title, LocationSplitter.CityOf(job.Location), LocationSplitter.StateOf(job.Location), "", "USA", job.JobType, job.DatePosted.ToString(), job.ExternalId, job.Company, job.IsRemote == true ? 1 : 0, job.Category, "", job.JobUrl, job.Description, "", Sqlconn);
                        sfOk++;
                    }
                    catch (Exception jobEx)
                    {
                        sfFail++;
                        Console.WriteLine($"[Insert-SuccessFactors] FAILED ExternalId={job.ExternalId} Company={job.Company} — {jobEx.Message}");
                    }
                }
                Console.WriteLine($"[SuccessFactors] Attempted={sfJobs.Count} Inserted={sfOk} Failed={sfFail}");

                //--Lever jobs
                var leverJobs = await new LeverScraper().FetchJobsAsync();

                int lvBefore = leverJobs.Count;
                leverJobs = leverJobs.Where(j => UsLocationFilter.IsUs(j.Location) || j.IsRemote == true).ToList();
                Console.WriteLine($"[Lever] US filter: {lvBefore} -> {leverJobs.Count}");

                int lvItBefore = leverJobs.Count;
                leverJobs = leverJobs.Where(j => ItJobFilter.IsIt(j.Title, j.Category)).ToList();
                Console.WriteLine($"[Lever] IT filter: {lvItBefore} -> {leverJobs.Count}");

                int lvOk = 0, lvFail = 0;
                foreach (var job in leverJobs)
                {
                    try
                    {
                        job.ExternalId = Guid.NewGuid().ToString();
                        FeedInsert.Post_Jobs(job.Location, job.Title, LocationSplitter.CityOf(job.Location), LocationSplitter.StateOf(job.Location), "", "USA", job.JobType, job.DatePosted.ToString(), job.ExternalId, job.Company, job.IsRemote == true ? 1 : 0, job.Category, "", job.JobUrl, job.Description, "", Sqlconn);
                        lvOk++;
                    }
                    catch (Exception jobEx)
                    {
                        lvFail++;
                        Console.WriteLine($"[Insert-Lever] FAILED ExternalId={job.ExternalId} Company={job.Company} — {jobEx.Message}");
                    }
                }
                Console.WriteLine($"[Lever] Attempted={leverJobs.Count} Inserted={lvOk} Failed={lvFail}");
            }

            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            finally
            {
                if (Sqlconn != null)
                {
                    if (Sqlconn.State.ToString() == "Open")
                    {
                        Sqlconn.Close();
                    }
                }
            }

        }

        private static async System.Threading.Tasks.Task RunSuccessFactorsOnlyAsync()
        {
            Console.WriteLine("[SF-Only] Starting scraper...");
            var jobs = await new SuccessFactorsScraper().FetchJobsAsync();
            Console.WriteLine($"[SF-Only] Scraper returned {jobs.Count} jobs (pre-US-filter).");

            int before = jobs.Count;
            jobs = jobs.Where(j => UsLocationFilter.IsUs(j.Location)).ToList();
            Console.WriteLine($"[SF-Only] US filter: {before} -> {jobs.Count}");

            int itBefore = jobs.Count;
            jobs = jobs.Where(j => ItJobFilter.IsIt(j.Title, j.Category)).ToList();
            Console.WriteLine($"[SF-Only] IT filter: {itBefore} -> {jobs.Count}");

            foreach (var j in jobs.Take(10))
                Console.WriteLine($"  - [{j.Company}] {j.Title} | {j.Location} | {j.JobUrl}");

            using var Sqlconn = new SqlConnection("Data source=209.59.189.133\\ITJOBCAFESERVER,1435;Initial Catalog=feeds;User Id=itjobcafe;Pwd=Chand@789!");
            Sqlconn.Open();
            Console.WriteLine($"[DB] Connection opened: state={Sqlconn.State}");

            int ok = 0, fail = 0;
            foreach (var job in jobs)
            {
                try
                {
                    job.ExternalId = Guid.NewGuid().ToString();
                    FeedInsert.Post_Jobs(job.Location, job.Title, LocationSplitter.CityOf(job.Location), LocationSplitter.StateOf(job.Location), "", "USA", job.JobType, job.DatePosted.ToString(), job.ExternalId, job.Company, job.IsRemote == true ? 1 : 0, job.Category, "", job.JobUrl, job.Description, "", Sqlconn);
                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    Console.WriteLine($"[Insert-SF] FAILED ExternalId={job.ExternalId} — {ex.Message}");
                }
            }
            Console.WriteLine($"[SF-Only] Attempted={jobs.Count} Inserted={ok} Failed={fail}");
        }

        private static async System.Threading.Tasks.Task RunLeverOnlyAsync()
        {
            Console.WriteLine("[Lever-Only] Starting scraper...");
            var jobs = await new LeverScraper().FetchJobsAsync();
            Console.WriteLine($"[Lever-Only] Scraper returned {jobs.Count} jobs (pre-US-filter).");

            int before = jobs.Count;
            jobs = jobs.Where(j => UsLocationFilter.IsUs(j.Location) || j.IsRemote == true).ToList();
            Console.WriteLine($"[Lever-Only] US filter: {before} -> {jobs.Count}");

            int itBefore = jobs.Count;
            jobs = jobs.Where(j => ItJobFilter.IsIt(j.Title, j.Category)).ToList();
            Console.WriteLine($"[Lever-Only] IT filter: {itBefore} -> {jobs.Count}");

            foreach (var j in jobs.Take(10))
                Console.WriteLine($"  - [{j.Company}] {j.Title} | {j.Location} | {j.JobUrl}");

            using var Sqlconn = new SqlConnection("Data source=209.59.189.133\\ITJOBCAFESERVER,1435;Initial Catalog=feeds;User Id=itjobcafe;Pwd=Chand@789!");
            Sqlconn.Open();
            Console.WriteLine($"[DB] Connection opened: state={Sqlconn.State}");

            int ok = 0, fail = 0;
            foreach (var job in jobs)
            {
                try
                {
                    job.ExternalId = Guid.NewGuid().ToString();
                    FeedInsert.Post_Jobs(job.Location, job.Title, LocationSplitter.CityOf(job.Location), LocationSplitter.StateOf(job.Location), "", "USA", job.JobType, job.DatePosted.ToString(), job.ExternalId, job.Company, job.IsRemote == true ? 1 : 0, job.Category, "", job.JobUrl, job.Description, "", Sqlconn);
                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    Console.WriteLine($"[Insert-Lever] FAILED ExternalId={job.ExternalId} — {ex.Message}");
                }
            }
            Console.WriteLine($"[Lever-Only] Attempted={jobs.Count} Inserted={ok} Failed={fail}");
        }

        private static async System.Threading.Tasks.Task RunTaleoOnlyAsync()
        {
            Console.WriteLine("[Taleo-Only] Starting scraper...");
            var jobs = await new TaleoScraper().FetchJobsAsync();
            Console.WriteLine($"[Taleo-Only] Scraper returned {jobs.Count} jobs (pre-US-filter).");

            int before = jobs.Count;
            jobs = jobs.Where(j => UsLocationFilter.IsUs(j.Location)).ToList();
            Console.WriteLine($"[Taleo-Only] US filter: {before} -> {jobs.Count}");

            int itBefore = jobs.Count;
            jobs = jobs.Where(j => ItJobFilter.IsIt(j.Title, j.Category)).ToList();
            Console.WriteLine($"[Taleo-Only] IT filter: {itBefore} -> {jobs.Count}");

            foreach (var j in jobs.Take(10))
                Console.WriteLine($"  - [{j.Company}] {j.Title} | {j.Location} | {j.JobUrl}");

            using var Sqlconn = new SqlConnection("Data source=209.59.189.133\\ITJOBCAFESERVER,1435;Initial Catalog=feeds;User Id=itjobcafe;Pwd=Chand@789!");
            Sqlconn.Open();
            Console.WriteLine($"[DB] Connection opened: state={Sqlconn.State}");

            int ok = 0, fail = 0;
            foreach (var job in jobs)
            {
                try
                {
                    job.ExternalId = Guid.NewGuid().ToString();
                    FeedInsert.Post_Jobs(job.Location, job.Title, LocationSplitter.CityOf(job.Location), LocationSplitter.StateOf(job.Location), "", "USA", job.JobType, job.DatePosted.ToString(), job.ExternalId, job.Company, job.IsRemote == true ? 1 : 0, job.Category, "", job.JobUrl, job.Description, "", Sqlconn);
                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    Console.WriteLine($"[Insert-Taleo] FAILED ExternalId={job.ExternalId} — {ex.Message}");
                }
            }
            Console.WriteLine($"[Taleo-Only] Attempted={jobs.Count} Inserted={ok} Failed={fail}");
        }
    }
}

