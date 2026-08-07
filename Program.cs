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
            try
            {
                await RunAsync(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Fatal] {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex);
                Environment.ExitCode = 1;
            }
        }

        private static async System.Threading.Tasks.Task RunAsync(string[] args)
        {
            // await RemoteJobsScraping();

            if (args != null && args.Length > 0 &&
                (string.Equals(args[0], "refresh-ats-urls", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], "refreshurls", StringComparison.OrdinalIgnoreCase)))
            {
                await AtsUrlDiscovery.RefreshAsync(
                    GetStringArg(args, "--input") ?? GetStringArg(args, "--job-sites"),
                    GetStringArg(args, "--output-root") ?? "outputs",
                    GetIntArg(args, "--limit"),
                    GetStringArg(args, "--only") ?? GetStringArg(args, "--source"));
                return;
            }

            if (args != null && args.Length > 0 &&
                (string.Equals(args[0], "backfill-it-flags", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], "backfill-it", StringComparison.OrdinalIgnoreCase)))
            {
                var connectionString = Environment.GetEnvironmentVariable(JobDatabaseSync.ConnectionStringEnvVar);
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException($"Set {JobDatabaseSync.ConnectionStringEnvVar} before backfilling IT flags.");
                }

                JobDatabaseSync.BackfillMissingItClassification(connectionString, "[IT-Backfill]");
                return;
            }

            if (args != null && args.Length > 0 &&
                (string.Equals(args[0], "backfill-location-parts", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], "backfill-location", StringComparison.OrdinalIgnoreCase)))
            {
                var connectionString = Environment.GetEnvironmentVariable(JobDatabaseSync.ConnectionStringEnvVar);
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException($"Set {JobDatabaseSync.ConnectionStringEnvVar} before backfilling location parts.");
                }

                JobDatabaseSync.BackfillLocationParts(connectionString, "[Location-Backfill]");
                return;
            }

            if (args != null && args.Length > 0 &&
                (string.Equals(args[0], "backfill-location-variants", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], "backfill-location-splits", StringComparison.OrdinalIgnoreCase)))
            {
                var connectionString = Environment.GetEnvironmentVariable(JobDatabaseSync.ConnectionStringEnvVar);
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException($"Set {JobDatabaseSync.ConnectionStringEnvVar} before backfilling location variants.");
                }

                JobDatabaseSync.BackfillMultiLocationVariants(connectionString, "[Location-Variant-Backfill]");
                return;
            }

            if (args != null && args.Length > 0 &&
                (string.Equals(args[0], "cleanup-city-production-blockers", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], "cleanup-city", StringComparison.OrdinalIgnoreCase)))
            {
                var connectionString = Environment.GetEnvironmentVariable(JobDatabaseSync.ConnectionStringEnvVar);
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException($"Set {JobDatabaseSync.ConnectionStringEnvVar} before cleaning city production blockers.");
                }

                JobDatabaseSync.CleanupCityProductionBlockers(connectionString, "[City-Cleanup]");
                return;
            }

            if (args != null && args.Length > 0 &&
                (string.Equals(args[0], "backfill-categories", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], "backfill-category", StringComparison.OrdinalIgnoreCase)))
            {
                var connectionString = Environment.GetEnvironmentVariable(JobDatabaseSync.ConnectionStringEnvVar);
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException($"Set {JobDatabaseSync.ConnectionStringEnvVar} before backfilling categories.");
                }

                JobDatabaseSync.BackfillNormalizedCategories(connectionString, "[Category-Backfill]");
                return;
            }

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

            if (args != null && args.Length > 0 &&
                string.Equals(args[0], "ashby", StringComparison.OrdinalIgnoreCase))
            {
                await RunAshbyOnlyAsync(
                    HasArg(args, "--write-db") && !HasArg(args, "--dry-run") && !HasArg(args, "--no-write-db"),
                    GetIntArg(args, "--limit") ?? GetIntArg(args, "--max-inserts"),
                    GetStringArg(args, "--company") ?? GetStringArg(args, "--slug"));
                return;
            }

            if (args != null && args.Length > 0 &&
                string.Equals(args[0], "workday", StringComparison.OrdinalIgnoreCase))
            {
                await RunWorkdayOnlyAsync(
                    HasArg(args, "--write-db") && !HasArg(args, "--dry-run") && !HasArg(args, "--no-write-db"),
                    GetIntArg(args, "--limit") ?? GetIntArg(args, "--max-inserts"),
                    GetStringArg(args, "--url-csv"),
                    GetIntArg(args, "--limit-sites"));
                return;
            }

            if (args != null && args.Length > 0 &&
                string.Equals(args[0], "icims", StringComparison.OrdinalIgnoreCase))
            {
                await RunIcimsOnlyAsync(
                    HasArg(args, "--write-db") && !HasArg(args, "--dry-run") && !HasArg(args, "--no-write-db"),
                    GetIntArg(args, "--limit") ?? GetIntArg(args, "--max-inserts"),
                    GetStringArg(args, "--url-csv"),
                    GetIntArg(args, "--limit-sites"));
                return;
            }

            if (args != null && args.Length > 0 &&
                (string.Equals(args[0], "jazzhr", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], "applytojob", StringComparison.OrdinalIgnoreCase)))
            {
                await RunJazzHrOnlyAsync(
                    HasArg(args, "--write-db") && !HasArg(args, "--dry-run") && !HasArg(args, "--no-write-db"),
                    GetIntArg(args, "--limit") ?? GetIntArg(args, "--max-inserts"),
                    GetStringArg(args, "--url-csv"),
                    GetIntArg(args, "--limit-sites"),
                    GetIntArg(args, "--max-jobs-per-site") ?? GetIntArg(args, "--limit-jobs-per-site"));
                return;
            }

            if (args != null && args.Length > 0 &&
                (string.Equals(args[0], "bamboohr", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], "bamboo", StringComparison.OrdinalIgnoreCase)))
            {
                await RunBambooHrOnlyAsync(
                    HasArg(args, "--write-db") && !HasArg(args, "--dry-run") && !HasArg(args, "--no-write-db"),
                    GetIntArg(args, "--limit") ?? GetIntArg(args, "--max-inserts"),
                    GetStringArg(args, "--url-csv"),
                    GetIntArg(args, "--limit-sites"),
                    GetIntArg(args, "--max-jobs-per-site") ?? GetIntArg(args, "--limit-jobs-per-site"));
                return;
            }

            if (args != null && args.Length > 0 &&
                (string.Equals(args[0], "breezyhr", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], "breezy", StringComparison.OrdinalIgnoreCase)))
            {
                await RunBreezyHrOnlyAsync(
                    HasArg(args, "--write-db") && !HasArg(args, "--dry-run") && !HasArg(args, "--no-write-db"),
                    GetIntArg(args, "--limit") ?? GetIntArg(args, "--max-inserts"),
                    GetStringArg(args, "--url-csv"),
                    GetIntArg(args, "--limit-sites"),
                    GetIntArg(args, "--max-jobs-per-site") ?? GetIntArg(args, "--limit-jobs-per-site"));
                return;
            }

            if (args != null && args.Length > 0 &&
                (string.Equals(args[0], "oraclecloud", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], "oracle", StringComparison.OrdinalIgnoreCase)))
            {
                await RunOracleCloudOnlyAsync(
                    HasArg(args, "--write-db") && !HasArg(args, "--dry-run") && !HasArg(args, "--no-write-db"),
                    GetIntArg(args, "--limit") ?? GetIntArg(args, "--max-inserts"),
                    GetStringArg(args, "--url-csv"),
                    GetIntArg(args, "--limit-sites"),
                    GetIntArg(args, "--max-jobs-per-site") ?? GetIntArg(args, "--limit-jobs-per-site"));
                return;
            }

            if (args != null && args.Length > 0 &&
                (string.Equals(args[0], "pinpointhq", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], "pinpoint", StringComparison.OrdinalIgnoreCase)))
            {
                await RunPinpointHqOnlyAsync(
                    HasArg(args, "--write-db") && !HasArg(args, "--dry-run") && !HasArg(args, "--no-write-db"),
                    GetIntArg(args, "--limit") ?? GetIntArg(args, "--max-inserts"),
                    GetStringArg(args, "--url-csv"),
                    GetIntArg(args, "--limit-sites"),
                    GetIntArg(args, "--max-jobs-per-site") ?? GetIntArg(args, "--limit-jobs-per-site"));
                return;
            }

            if (args != null && args.Length > 0 &&
                string.Equals(args[0], "personio", StringComparison.OrdinalIgnoreCase))
            {
                await RunPersonioOnlyAsync(
                    HasArg(args, "--write-db") && !HasArg(args, "--dry-run") && !HasArg(args, "--no-write-db"),
                    GetIntArg(args, "--limit") ?? GetIntArg(args, "--max-inserts"),
                    GetStringArg(args, "--url-csv"),
                    GetIntArg(args, "--limit-sites"),
                    GetIntArg(args, "--max-jobs-per-site") ?? GetIntArg(args, "--limit-jobs-per-site"));
                return;
            }

            if (args != null && args.Length > 0 &&
                (string.Equals(args[0], "freshteam", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], "fresh-team", StringComparison.OrdinalIgnoreCase)))
            {
                await RunFreshteamOnlyAsync(
                    HasArg(args, "--write-db") && !HasArg(args, "--dry-run") && !HasArg(args, "--no-write-db"),
                    GetIntArg(args, "--limit") ?? GetIntArg(args, "--max-inserts"),
                    GetStringArg(args, "--url-csv"),
                    GetIntArg(args, "--limit-sites"),
                    GetIntArg(args, "--max-jobs-per-site") ?? GetIntArg(args, "--limit-jobs-per-site"));
                return;
            }

            if (args != null && args.Length > 0 &&
                (string.Equals(args[0], "jobsoid", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], "job-soid", StringComparison.OrdinalIgnoreCase)))
            {
                await RunJobsoidOnlyAsync(
                    HasArg(args, "--write-db") && !HasArg(args, "--dry-run") && !HasArg(args, "--no-write-db"),
                    GetIntArg(args, "--limit") ?? GetIntArg(args, "--max-inserts"),
                    GetStringArg(args, "--url-csv"),
                    GetIntArg(args, "--limit-sites"),
                    GetIntArg(args, "--max-jobs-per-site") ?? GetIntArg(args, "--limit-jobs-per-site"));
                return;
            }

            if (args != null && args.Length > 0 &&
                (string.Equals(args[0], "applicantpro", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], "applicant-pro", StringComparison.OrdinalIgnoreCase)))
            {
                await RunApplicantProOnlyAsync(
                    HasArg(args, "--write-db") && !HasArg(args, "--dry-run") && !HasArg(args, "--no-write-db"),
                    GetIntArg(args, "--limit") ?? GetIntArg(args, "--max-inserts"),
                    GetStringArg(args, "--url-csv"),
                    GetIntArg(args, "--limit-sites"),
                    GetIntArg(args, "--max-jobs-per-site") ?? GetIntArg(args, "--limit-jobs-per-site"));
                return;
            }

            if (args != null && args.Length > 0 &&
                (string.Equals(args[0], "catsone", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], "cats-one", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], "cats", StringComparison.OrdinalIgnoreCase)))
            {
                await RunCatsOneOnlyAsync(
                    HasArg(args, "--write-db") && !HasArg(args, "--dry-run") && !HasArg(args, "--no-write-db"),
                    GetIntArg(args, "--limit") ?? GetIntArg(args, "--max-inserts"),
                    GetStringArg(args, "--url-csv"),
                    GetIntArg(args, "--limit-sites"),
                    GetIntArg(args, "--max-jobs-per-site") ?? GetIntArg(args, "--limit-jobs-per-site"));
                return;
            }

            if (args != null && args.Length > 0 &&
                (string.Equals(args[0], "zohorecruit", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], "zoho-recruit", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], "zoho", StringComparison.OrdinalIgnoreCase)))
            {
                await RunZohoRecruitOnlyAsync(
                    HasArg(args, "--write-db") && !HasArg(args, "--dry-run") && !HasArg(args, "--no-write-db"),
                    GetIntArg(args, "--limit") ?? GetIntArg(args, "--max-inserts"),
                    GetStringArg(args, "--url-csv"),
                    GetIntArg(args, "--limit-sites"),
                    GetIntArg(args, "--max-jobs-per-site") ?? GetIntArg(args, "--limit-jobs-per-site"));
                return;
            }

            if (args != null && args.Length > 0 &&
                string.Equals(args[0], "smartrecruiters", StringComparison.OrdinalIgnoreCase))
            {
                await RunSmartRecruitersOnlyAsync(
                    HasArg(args, "--write-db") && !HasArg(args, "--dry-run") && !HasArg(args, "--no-write-db"),
                    GetIntArg(args, "--limit") ?? GetIntArg(args, "--max-inserts"),
                    GetStringArg(args, "--company") ?? GetStringArg(args, "--slug"));
                return;
            }

            if (args != null && args.Length > 0 &&
                string.Equals(args[0], "dayforce", StringComparison.OrdinalIgnoreCase))
            {
                await RunDayforceOnlyAsync(
                    HasArg(args, "--write-db") && !HasArg(args, "--dry-run") && !HasArg(args, "--no-write-db"),
                    GetIntArg(args, "--limit") ?? GetIntArg(args, "--max-inserts"),
                    GetStringArg(args, "--company") ?? GetStringArg(args, "--client") ?? GetStringArg(args, "--slug"));
                return;
            }

            if (args != null && args.Length > 0 &&
                string.Equals(args[0], "recruitee", StringComparison.OrdinalIgnoreCase))
            {
                await RunRecruiteeOnlyAsync(
                    HasArg(args, "--write-db") && !HasArg(args, "--dry-run") && !HasArg(args, "--no-write-db"),
                    GetIntArg(args, "--limit") ?? GetIntArg(args, "--max-inserts"),
                    GetStringArg(args, "--company") ?? GetStringArg(args, "--subdomain") ?? GetStringArg(args, "--slug"));
                return;
            }

            if (args == null || args.Length == 0 ||
                string.Equals(args[0], "all", StringComparison.OrdinalIgnoreCase))
            {
                await RunAllScrapersWithDedupeAsync(
                    !HasArg(args, "--dry-run") && !HasArg(args, "--no-write-db"),
                    GetIntArg(args, "--limit") ?? GetIntArg(args, "--max-inserts"),
                    GetStringArg(args, "--only") ?? GetStringArg(args, "--source"),
                    GetIntArg(args, "--limit-sites"),
                    GetIntArg(args, "--max-jobs-per-site") ?? GetIntArg(args, "--limit-jobs-per-site"),
                    GetStringArg(args, "--workday-url-csv"),
                    GetStringArg(args, "--icims-url-csv"),
                    GetStringArg(args, "--jazzhr-url-csv"),
                    GetStringArg(args, "--bamboohr-url-csv"),
                    GetStringArg(args, "--breezyhr-url-csv"),
                    GetStringArg(args, "--oraclecloud-url-csv"),
                    GetStringArg(args, "--pinpointhq-url-csv") ?? GetStringArg(args, "--pinpoint-url-csv"),
                    GetStringArg(args, "--personio-url-csv"),
                    GetStringArg(args, "--freshteam-url-csv") ?? GetStringArg(args, "--fresh-team-url-csv"),
                    GetStringArg(args, "--jobsoid-url-csv") ?? GetStringArg(args, "--job-soid-url-csv"),
                    GetStringArg(args, "--applicantpro-url-csv") ?? GetStringArg(args, "--applicant-pro-url-csv"),
                    GetStringArg(args, "--catsone-url-csv") ?? GetStringArg(args, "--cats-one-url-csv"),
                    GetStringArg(args, "--zohorecruit-url-csv") ?? GetStringArg(args, "--zoho-recruit-url-csv") ?? GetStringArg(args, "--zoho-url-csv"));
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

                jobs = ClassifyJobsForFlagging(jobs, "Free scrapers");

                // Plug into your existing ETL:
                int freeOk = 0, freeFail = 0;
                foreach (var job in jobs)
                {
                    try
                    {
                        job.ExternalId = Guid.NewGuid().ToString();
                        //FeedInsert.Post_Jobs(job.Location, job.Title, LocationSplitter.CityOf(job.Location), LocationSplitter.StateOf(job.Location), "", "USA", job.JobType, job.DatePosted.ToString(), job.ExternalId, job.Company, job.IsRemote == true ? 1 : 0, job.Category, "", job.JobUrl, job.Description, "", Sqlconn);
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

                workdayjobs = ClassifyJobsForFlagging(workdayjobs, "Workday");

                int wdOk = 0, wdFail = 0;
                foreach (var job in workdayjobs)
                {
                    try
                    {
                        job.ExternalId = Guid.NewGuid().ToString();
                        //FeedInsert.Post_Jobs(job.Location, job.Title, LocationSplitter.CityOf(job.Location), LocationSplitter.StateOf(job.Location), "", "USA", job.JobType, job.DatePosted.ToString(), job.ExternalId, job.Company, job.IsRemote == true ? 1 : 0, job.Category, "", job.JobUrl, job.Description, "", Sqlconn);
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

                sfJobs = ClassifyJobsForFlagging(sfJobs, "SuccessFactors");

                int sfOk = 0, sfFail = 0;
                foreach (var job in sfJobs)
                {
                    try
                    {
                        job.ExternalId = Guid.NewGuid().ToString();
                        //FeedInsert.Post_Jobs(job.Location, job.Title, LocationSplitter.CityOf(job.Location), LocationSplitter.StateOf(job.Location), "", "USA", job.JobType, job.DatePosted.ToString(), job.ExternalId, job.Company, job.IsRemote == true ? 1 : 0, job.Category, "", job.JobUrl, job.Description, "", Sqlconn);
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

                leverJobs = ClassifyJobsForFlagging(leverJobs, "Lever");

                int lvOk = 0, lvFail = 0;
                foreach (var job in leverJobs)
                {
                    try
                    {
                        job.ExternalId = Guid.NewGuid().ToString();
                        //FeedInsert.Post_Jobs(job.Location, job.Title, LocationSplitter.CityOf(job.Location), LocationSplitter.StateOf(job.Location), "", "USA", job.JobType, job.DatePosted.ToString(), job.ExternalId, job.Company, job.IsRemote == true ? 1 : 0, job.Category, "", job.JobUrl, job.Description, "", Sqlconn);
                        lvOk++;
                    }
                    catch (Exception jobEx)
                    {
                        lvFail++;
                        Console.WriteLine($"[Insert-Lever] FAILED ExternalId={job.ExternalId} Company={job.Company} — {jobEx.Message}");
                    }
                }
                Console.WriteLine($"[Lever] Attempted={leverJobs.Count} Inserted={lvOk} Failed={lvFail}");

                //--Ashby jobs
                var ashbyJobs = await new AshbyScraper().FetchJobsAsync();

                int ashbyBefore = ashbyJobs.Count;
                ashbyJobs = ashbyJobs.Where(j => UsLocationFilter.IsUs(j.Location) || j.IsRemote == true).ToList();
                Console.WriteLine($"[Ashby] US filter: {ashbyBefore} -> {ashbyJobs.Count}");

                ashbyJobs = ClassifyJobsForFlagging(ashbyJobs, "Ashby");

                int ashbyOk = 0, ashbyFail = 0;
                foreach (var job in ashbyJobs)
                {
                    try
                    {
                        job.ExternalId = Guid.NewGuid().ToString();
                        //FeedInsert.Post_Jobs(job.Location, job.Title, LocationSplitter.CityOf(job.Location), LocationSplitter.StateOf(job.Location), "", "USA", job.JobType, job.DatePosted.ToString(), job.ExternalId, job.Company, job.IsRemote == true ? 1 : 0, job.Category, "", job.JobUrl, job.Description, "", Sqlconn);
                        ashbyOk++;
                    }
                    catch (Exception jobEx)
                    {
                        ashbyFail++;
                        Console.WriteLine($"[Insert-Ashby] FAILED ExternalId={job.ExternalId} Company={job.Company} - {jobEx.Message}");
                    }
                }
                Console.WriteLine($"[Ashby] Attempted={ashbyJobs.Count} Inserted={ashbyOk} Failed={ashbyFail}");
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

        private static async System.Threading.Tasks.Task RunAllScrapersWithDedupeAsync(bool writeToDatabase, int? insertLimit, string onlySource, int? limitSites, int? maxJobsPerSite, string workdayUrlCsv, string icimsUrlCsv, string jazzHrUrlCsv, string bambooHrUrlCsv, string breezyHrUrlCsv, string oracleCloudUrlCsv, string pinpointHqUrlCsv, string personioUrlCsv, string freshteamUrlCsv, string jobsoidUrlCsv, string applicantProUrlCsv, string catsOneUrlCsv, string zohoRecruitUrlCsv)
        {
            var connectionString = Environment.GetEnvironmentVariable(JobDatabaseSync.ConnectionStringEnvVar);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Set {JobDatabaseSync.ConnectionStringEnvVar} before running the combined scraper.");
            }

            var allJobs = new List<ScrapedJob>();

            if (!string.IsNullOrWhiteSpace(onlySource))
            {
                Console.WriteLine($"[All] Source filter enabled: {onlySource}");
            }

            if (ShouldRunSource(onlySource, "Free scrapers", "free"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "Free scrapers",
                    () => new FreeJobScraperOrchestrator().RunAllAsync(),
                    includeRemoteWithoutUsLocation: false);
            }

            if (ShouldRunSource(onlySource, "Workday"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "Workday",
                    () => FetchCombinedWorkdayJobsAsync(workdayUrlCsv, limitSites),
                    includeRemoteWithoutUsLocation: true);
            }

            if (ShouldRunSource(onlySource, "iCIMS", "icims"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "iCIMS",
                    () => new IcimsScraper().FetchJobsAsync(icimsUrlCsv, limitSites),
                    includeRemoteWithoutUsLocation: true);
            }

            if (ShouldRunSource(onlySource, "JazzHR", "jazz", "applytojob", "apply-to-job"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "JazzHR",
                    () => new JazzHrScraper().FetchJobsAsync(jazzHrUrlCsv, limitSites, maxJobsPerSite ?? 0),
                    includeRemoteWithoutUsLocation: true);
            }

            if (ShouldRunSource(onlySource, "BambooHR", "bamboo"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "BambooHR",
                    () => new BambooHrScraper().FetchJobsAsync(bambooHrUrlCsv, limitSites, maxJobsPerSite ?? 0),
                    includeRemoteWithoutUsLocation: true);
            }

            if (ShouldRunSource(onlySource, "BreezyHR", "breezy"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "BreezyHR",
                    () => new BreezyHrScraper().FetchJobsAsync(breezyHrUrlCsv, limitSites, maxJobsPerSite ?? 0),
                    includeRemoteWithoutUsLocation: true);
            }

            if (ShouldRunSource(onlySource, "OracleCloud", "oracle", "oraclecloud"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "OracleCloud",
                    () => new OracleCloudScraper().FetchJobsAsync(oracleCloudUrlCsv, limitSites, maxJobsPerSite ?? 0),
                    includeRemoteWithoutUsLocation: true);
            }

            if (ShouldRunSource(onlySource, "PinpointHQ", "pinpoint", "pinpointhq"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "PinpointHQ",
                    () => new PinpointHqScraper().FetchJobsAsync(pinpointHqUrlCsv, limitSites, maxJobsPerSite ?? 0),
                    includeRemoteWithoutUsLocation: true);
            }

            if (ShouldRunSource(onlySource, "Personio"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "Personio",
                    () => new PersonioScraper().FetchJobsAsync(personioUrlCsv, limitSites, maxJobsPerSite ?? 0),
                    includeRemoteWithoutUsLocation: true);
            }

            if (ShouldRunSource(onlySource, "Freshteam", "fresh-team"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "Freshteam",
                    () => new FreshteamScraper().FetchJobsAsync(freshteamUrlCsv, limitSites, maxJobsPerSite ?? 0),
                    includeRemoteWithoutUsLocation: true);
            }

            if (ShouldRunSource(onlySource, "Jobsoid", "job-soid"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "Jobsoid",
                    () => new JobsoidScraper().FetchJobsAsync(jobsoidUrlCsv, limitSites, maxJobsPerSite ?? 0),
                    includeRemoteWithoutUsLocation: true);
            }

            if (ShouldRunSource(onlySource, "ApplicantPro", "applicant-pro"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "ApplicantPro",
                    () => new ApplicantProScraper().FetchJobsAsync(applicantProUrlCsv, limitSites, maxJobsPerSite ?? 0),
                    includeRemoteWithoutUsLocation: true);
            }

            if (ShouldRunSource(onlySource, "CATSOne", "catsone", "cats-one", "cats"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "CATSOne",
                    () => new CatsOneScraper().FetchJobsAsync(catsOneUrlCsv, limitSites, maxJobsPerSite ?? 0),
                    includeRemoteWithoutUsLocation: true);
            }

            if (ShouldRunSource(onlySource, "ZohoRecruit", "zoho", "zohorecruit", "zoho-recruit"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "ZohoRecruit",
                    () => new ZohoRecruitScraper().FetchJobsAsync(zohoRecruitUrlCsv, limitSites, maxJobsPerSite ?? 0),
                    includeRemoteWithoutUsLocation: true);
            }

            if (ShouldRunSource(onlySource, "SuccessFactors", "success-factors"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "SuccessFactors",
                    () => new SuccessFactorsScraper().FetchJobsAsync(),
                    includeRemoteWithoutUsLocation: false);
            }

            if (ShouldRunSource(onlySource, "Lever"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "Lever",
                    () => new LeverScraper().FetchJobsAsync(),
                    includeRemoteWithoutUsLocation: true);
            }

            if (ShouldRunSource(onlySource, "Ashby"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "Ashby",
                    () => new AshbyScraper().FetchJobsAsync(),
                    includeRemoteWithoutUsLocation: true);
            }

            if (ShouldRunSource(onlySource, "Greenhouse"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "Greenhouse",
                    () => new GreenhouseAtsScraper().FetchJobsAsync(),
                    includeRemoteWithoutUsLocation: true);
            }

            if (ShouldRunSource(onlySource, "SmartRecruiters", "smart-recruiters"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "SmartRecruiters",
                    () => new SmartRecruitersScraper().FetchJobsAsync(),
                    includeRemoteWithoutUsLocation: true);
            }

            if (ShouldRunSource(onlySource, "Dayforce"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "Dayforce",
                    () => new DayforceScraper().FetchJobsAsync(),
                    includeRemoteWithoutUsLocation: true);
            }

            if (ShouldRunSource(onlySource, "Recruitee"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "Recruitee",
                    () => new RecruiteeScraper().FetchJobsAsync(),
                    includeRemoteWithoutUsLocation: true);
            }

            if (ShouldRunSource(onlySource, "Taleo"))
            {
                await AddFilteredJobsAsync(
                    allJobs,
                    "Taleo",
                    () => new TaleoScraper().FetchJobsAsync(),
                    includeRemoteWithoutUsLocation: false);
            }

            Console.WriteLine($"[All] Combined filtered jobs before dedupe: {allJobs.Count}");

            Console.WriteLine("[All] Loading existing database keys...");
            var existing = JobDatabaseSync.LoadExistingKeys(connectionString);
            Console.WriteLine(
                $"[All] Existing DB urls={existing.Urls.Count}, references={existing.References.Count}, fingerprints={existing.Fingerprints.Count}");

            var deduped = JobDatabaseSync.RemoveDuplicates(allJobs, existing);

            Console.WriteLine($"[All] Skipped DB duplicates: {deduped.DbDuplicates}");
            Console.WriteLine($"[All] Skipped batch duplicates: {deduped.BatchDuplicates}");
            Console.WriteLine($"[All] New jobs after dedupe: {deduped.NewJobs.Count}");

            if (writeToDatabase)
            {
                var jobsToInsert = insertLimit.HasValue
                    ? deduped.NewJobs.Take(insertLimit.Value).ToList()
                    : deduped.NewJobs;

                if (insertLimit.HasValue)
                {
                    Console.WriteLine($"[All] Insert limit enabled: {jobsToInsert.Count} of {deduped.NewJobs.Count} deduped jobs will be inserted.");
                }

                Console.WriteLine("[All] WRITE MODE ENABLED. Refreshing existing duplicates and inserting new jobs into database...");
                WriteDedupeResultToDatabase(connectionString, deduped, jobsToInsert, insertLimit, "[All]");
            }
            else
            {
                Console.WriteLine("[All] Dry run only; no database writes. Remove --dry-run/--no-write-db to insert deduped jobs.");
            }
        }

        private static async System.Threading.Tasks.Task AddFilteredJobsAsync(
            List<ScrapedJob> allJobs,
            string label,
            Func<System.Threading.Tasks.Task<List<ScrapedJob>>> fetch,
            bool includeRemoteWithoutUsLocation)
        {
            try
            {
                Console.WriteLine($"[{label}] Starting scraper...");
                var jobs = await fetch();
                Console.WriteLine($"[{label}] Raw jobs: {jobs.Count}");

                int locationBefore = jobs.Count;
                jobs = jobs
                    .Where(j => UsLocationFilter.IsUs(j.Location) || (includeRemoteWithoutUsLocation && j.IsRemote))
                    .ToList();
                Console.WriteLine($"[{label}] US/remote filter: {locationBefore} -> {jobs.Count}");

                jobs = ExpandLocationsForDatabase(jobs, label);
                jobs = ClassifyJobsForFlagging(jobs, label);
                PrintJobTypeSummary(label, jobs);

                allJobs.AddRange(jobs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{label}] FAILED: {ex.Message}");
            }
        }

        private static List<ScrapedJob> ExpandLocationsForDatabase(List<ScrapedJob> jobs, string label)
        {
            var before = jobs.Count;
            var expanded = JobLocationExpander.ExpandForDatabase(jobs, out var expandedPostings, out var addedRows);
            if (expandedPostings > 0 || expanded.Count != before)
            {
                Console.WriteLine(
                    $"[{label}] Location expansion: {before} -> {expanded.Count} rows; expanded postings={expandedPostings}, added city rows={addedRows}");
            }

            return expanded;
        }

        private static List<ScrapedJob> ClassifyJobsForFlagging(List<ScrapedJob> jobs, string label)
        {
            foreach (var job in jobs)
            {
                ItJobFilter.Apply(job);
            }

            var itJobs = jobs.Where(j => j.IsIT).ToList();
            var nonItJobs = jobs.Where(j => !j.IsIT).ToList();

            Console.WriteLine($"[{label}] IT classification: {jobs.Count} total -> {itJobs.Count} IT, {nonItJobs.Count} non-IT; keeping both with flags");
            PrintClassificationSamples(label, itJobs, nonItJobs);
            return jobs;
        }

        private static void PrintJobTypeSummary(string label, List<ScrapedJob> jobs)
        {
            var missing = jobs.Count(j => string.IsNullOrWhiteSpace(j.JobType));
            var populated = jobs.Count - missing;
            var pct = jobs.Count == 0 ? 0 : Math.Round(100.0 * populated / jobs.Count, 2);
            Console.WriteLine($"[{label}] Job type coverage: {populated}/{jobs.Count} populated ({pct}%). Missing={missing}");

            var missingCategory = jobs.Count(j => string.IsNullOrWhiteSpace(j.Category));
            var populatedCategory = jobs.Count - missingCategory;
            var categoryPct = jobs.Count == 0 ? 0 : Math.Round(100.0 * populatedCategory / jobs.Count, 2);
            Console.WriteLine($"[{label}] Category coverage: {populatedCategory}/{jobs.Count} populated ({categoryPct}%). Missing={missingCategory}");

            var missingPosted = jobs.Count(j => !j.DatePosted.HasValue);
            var populatedPosted = jobs.Count - missingPosted;
            var postedPct = jobs.Count == 0 ? 0 : Math.Round(100.0 * populatedPosted / jobs.Count, 2);
            Console.WriteLine($"[{label}] Posted date coverage: {populatedPosted}/{jobs.Count} populated ({postedPct}%). Missing={missingPosted}");

            foreach (var group in jobs
                .Where(j => !string.IsNullOrWhiteSpace(j.JobType))
                .GroupBy(j => j.JobType.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Take(5))
            {
                Console.WriteLine($"[{label}] Job type sample: {group.Key} = {group.Count()}");
            }
        }

        private static void PrintClassificationSamples(string label, List<ScrapedJob> itJobs, List<ScrapedJob> nonItJobs)
        {
            foreach (var job in itJobs.Take(3))
            {
                Console.WriteLine($"[{label}] IT sample +{job.ITScore}: {job.Title} | {job.Company}");
            }

            foreach (var job in nonItJobs.Take(3))
            {
                Console.WriteLine($"[{label}] Non-IT sample {job.ITScore}: {job.Title} | {job.Company}");
            }
        }

        private static bool ShouldRunSource(string onlySource, string label, params string[] aliases)
        {
            if (string.IsNullOrWhiteSpace(onlySource)) return true;

            var wanted = NormalizeSourceName(onlySource);
            if (wanted == NormalizeSourceName(label)) return true;

            return aliases.Any(alias => wanted == NormalizeSourceName(alias));
        }

        private static string NormalizeSourceName(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? ""
                : new string(value
                    .Where(char.IsLetterOrDigit)
                    .Select(char.ToLowerInvariant)
                    .ToArray());
        }

        private static bool HasArg(string[] args, string name)
        {
            return args != null && args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        }

        private static int? GetIntArg(string[] args, string name)
        {
            if (args == null) return null;

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) &&
                    i + 1 < args.Length &&
                    int.TryParse(args[i + 1], out var value) &&
                    value >= 0)
                {
                    return value;
                }
            }

            return null;
        }

        private static string GetStringArg(string[] args, string name)
        {
            if (args == null) return null;

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) &&
                    i + 1 < args.Length)
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static void WriteDedupeResultToDatabase(
            string connectionString,
            JobDatabaseSync.DedupeResult deduped,
            List<ScrapedJob> jobsToInsert,
            int? writeLimit,
            string logPrefix)
        {
            var duplicateJobsToUpdate = writeLimit.HasValue
                ? deduped.DbDuplicateJobs.Take(writeLimit.Value).ToList()
                : deduped.DbDuplicateJobs;

            if (writeLimit.HasValue && duplicateJobsToUpdate.Count < deduped.DbDuplicateJobs.Count)
            {
                Console.WriteLine($"{logPrefix} Duplicate update limit enabled: {duplicateJobsToUpdate.Count} of {deduped.DbDuplicateJobs.Count}");
            }

            JobDatabaseSync.UpdateExistingJobs(connectionString, duplicateJobsToUpdate, logPrefix);
            JobDatabaseSync.InsertJobs(connectionString, jobsToInsert, logPrefix);
            JobDatabaseSync.BackfillMissingItClassification(connectionString, logPrefix);
        }

        private static async System.Threading.Tasks.Task RunSuccessFactorsOnlyAsync()
        {
            Console.WriteLine("[SF-Only] Starting scraper...");
            var jobs = await new SuccessFactorsScraper().FetchJobsAsync();
            Console.WriteLine($"[SF-Only] Scraper returned {jobs.Count} jobs (pre-US-filter).");

            int before = jobs.Count;
            jobs = jobs.Where(j => UsLocationFilter.IsUs(j.Location)).ToList();
            Console.WriteLine($"[SF-Only] US filter: {before} -> {jobs.Count}");

            jobs = ClassifyJobsForFlagging(jobs, "SF-Only");

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
                    //FeedInsert.Post_Jobs(job.Location, job.Title, LocationSplitter.CityOf(job.Location), LocationSplitter.StateOf(job.Location), "", "USA", job.JobType, job.DatePosted.ToString(), job.ExternalId, job.Company, job.IsRemote == true ? 1 : 0, job.Category, "", job.JobUrl, job.Description, "", Sqlconn);
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

            jobs = ClassifyJobsForFlagging(jobs, "Lever-Only");

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
                    //FeedInsert.Post_Jobs(job.Location, job.Title, LocationSplitter.CityOf(job.Location), LocationSplitter.StateOf(job.Location), "", "USA", job.JobType, job.DatePosted.ToString(), job.ExternalId, job.Company, job.IsRemote == true ? 1 : 0, job.Category, "", job.JobUrl, job.Description, "", Sqlconn);
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

        private static async System.Threading.Tasks.Task RunAshbyOnlyAsync(bool writeToDatabase, int? insertLimit, string onlyCompany)
        {
            Console.WriteLine("[Ashby-Only] Starting scraper...");
            var jobs = await new AshbyScraper().FetchJobsAsync(onlyCompany);
            Console.WriteLine($"[Ashby-Only] Scraper returned {jobs.Count} jobs (pre-US-filter).");

            int before = jobs.Count;
            jobs = jobs.Where(j => UsLocationFilter.IsUs(j.Location) || j.IsRemote == true).ToList();
            Console.WriteLine($"[Ashby-Only] US filter: {before} -> {jobs.Count}");

            jobs = ExpandLocationsForDatabase(jobs, "Ashby-Only");
            jobs = ClassifyJobsForFlagging(jobs, "Ashby-Only");

            foreach (var j in jobs.Take(10))
                Console.WriteLine($"  - [{j.Company}] {j.Title} | {j.Location} | {j.JobUrl}");

            if (!writeToDatabase)
            {
                Console.WriteLine("[Ashby-Only] Inspection mode only; add --write-db to insert deduped jobs.");
                return;
            }

            var connectionString = Environment.GetEnvironmentVariable(JobDatabaseSync.ConnectionStringEnvVar);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Set {JobDatabaseSync.ConnectionStringEnvVar} before writing Ashby jobs to the database.");
            }

            Console.WriteLine("[Ashby-Only] Loading existing database keys...");
            var existing = JobDatabaseSync.LoadExistingKeys(connectionString);
            Console.WriteLine(
                $"[Ashby-Only] Existing DB urls={existing.Urls.Count}, references={existing.References.Count}, fingerprints={existing.Fingerprints.Count}");

            var deduped = JobDatabaseSync.RemoveDuplicates(jobs, existing);

            Console.WriteLine($"[Ashby-Only] Skipped DB duplicates: {deduped.DbDuplicates}");
            Console.WriteLine($"[Ashby-Only] Skipped batch duplicates: {deduped.BatchDuplicates}");
            Console.WriteLine($"[Ashby-Only] New jobs after dedupe: {deduped.NewJobs.Count}");

            var jobsToInsert = insertLimit.HasValue
                ? deduped.NewJobs.Take(insertLimit.Value).ToList()
                : deduped.NewJobs;

            if (insertLimit.HasValue)
            {
                Console.WriteLine($"[Ashby-Only] Insert limit enabled: {jobsToInsert.Count} of {deduped.NewJobs.Count} deduped jobs will be inserted.");
            }

            Console.WriteLine("[Ashby-Only] WRITE MODE ENABLED. Refreshing existing duplicates and inserting new jobs into database...");
            WriteDedupeResultToDatabase(connectionString, deduped, jobsToInsert, insertLimit, "[Ashby-Only]");
        }

        private static async System.Threading.Tasks.Task<List<ScrapedJob>> FetchCombinedWorkdayJobsAsync(string inputCsv, int? limitSites)
        {
            var combined = new List<ScrapedJob>();

            var discoveredJobs = await new WorkdayScraper().FetchJobsAsync(inputCsv, limitSites);
            Console.WriteLine($"[Workday] CSV-discovered pipeline returned {discoveredJobs.Count} jobs.");
            combined.AddRange(discoveredJobs);

            Console.WriteLine($"[Workday] Combined Workday jobs before shared filters/dedupe: {combined.Count}");
            return combined;
        }
        private static async System.Threading.Tasks.Task RunWorkdayOnlyAsync(bool writeToDatabase, int? insertLimit, string inputCsv, int? limitSites)
        {
            await RunSingleScraperWithDedupeAsync(
                "Workday-Only",
                () => FetchCombinedWorkdayJobsAsync(inputCsv, limitSites),
                writeToDatabase,
                insertLimit,
                includeRemoteWithoutUsLocation: true);
        }

        private static async System.Threading.Tasks.Task RunIcimsOnlyAsync(bool writeToDatabase, int? insertLimit, string inputCsv, int? limitSites)
        {
            await RunSingleScraperWithDedupeAsync(
                "iCIMS-Only",
                () => new IcimsScraper().FetchJobsAsync(inputCsv, limitSites),
                writeToDatabase,
                insertLimit,
                includeRemoteWithoutUsLocation: true);
        }

        private static async System.Threading.Tasks.Task RunJazzHrOnlyAsync(bool writeToDatabase, int? insertLimit, string inputCsv, int? limitSites, int? maxJobsPerSite)
        {
            await RunSingleScraperWithDedupeAsync(
                "JazzHR-Only",
                () => new JazzHrScraper().FetchJobsAsync(inputCsv, limitSites, maxJobsPerSite ?? 0),
                writeToDatabase,
                insertLimit,
                includeRemoteWithoutUsLocation: true);
        }

        private static async System.Threading.Tasks.Task RunBambooHrOnlyAsync(bool writeToDatabase, int? insertLimit, string inputCsv, int? limitSites, int? maxJobsPerSite)
        {
            await RunSingleScraperWithDedupeAsync(
                "BambooHR-Only",
                () => new BambooHrScraper().FetchJobsAsync(inputCsv, limitSites, maxJobsPerSite ?? 0),
                writeToDatabase,
                insertLimit,
                includeRemoteWithoutUsLocation: true);
        }

        private static async System.Threading.Tasks.Task RunBreezyHrOnlyAsync(bool writeToDatabase, int? insertLimit, string inputCsv, int? limitSites, int? maxJobsPerSite)
        {
            await RunSingleScraperWithDedupeAsync(
                "BreezyHR-Only",
                () => new BreezyHrScraper().FetchJobsAsync(inputCsv, limitSites, maxJobsPerSite ?? 0),
                writeToDatabase,
                insertLimit,
                includeRemoteWithoutUsLocation: true);
        }

        private static async System.Threading.Tasks.Task RunOracleCloudOnlyAsync(bool writeToDatabase, int? insertLimit, string inputCsv, int? limitSites, int? maxJobsPerSite)
        {
            await RunSingleScraperWithDedupeAsync(
                "OracleCloud-Only",
                () => new OracleCloudScraper().FetchJobsAsync(inputCsv, limitSites, maxJobsPerSite ?? 0),
                writeToDatabase,
                insertLimit,
                includeRemoteWithoutUsLocation: true);
        }

        private static async System.Threading.Tasks.Task RunPinpointHqOnlyAsync(bool writeToDatabase, int? insertLimit, string inputCsv, int? limitSites, int? maxJobsPerSite)
        {
            await RunSingleScraperWithDedupeAsync(
                "PinpointHQ-Only",
                () => new PinpointHqScraper().FetchJobsAsync(inputCsv, limitSites, maxJobsPerSite ?? 0),
                writeToDatabase,
                insertLimit,
                includeRemoteWithoutUsLocation: true);
        }

        private static async System.Threading.Tasks.Task RunPersonioOnlyAsync(bool writeToDatabase, int? insertLimit, string inputCsv, int? limitSites, int? maxJobsPerSite)
        {
            await RunSingleScraperWithDedupeAsync(
                "Personio-Only",
                () => new PersonioScraper().FetchJobsAsync(inputCsv, limitSites, maxJobsPerSite ?? 0),
                writeToDatabase,
                insertLimit,
                includeRemoteWithoutUsLocation: true);
        }

        private static async System.Threading.Tasks.Task RunFreshteamOnlyAsync(bool writeToDatabase, int? insertLimit, string inputCsv, int? limitSites, int? maxJobsPerSite)
        {
            await RunSingleScraperWithDedupeAsync(
                "Freshteam-Only",
                () => new FreshteamScraper().FetchJobsAsync(inputCsv, limitSites, maxJobsPerSite ?? 0),
                writeToDatabase,
                insertLimit,
                includeRemoteWithoutUsLocation: true);
        }

        private static async System.Threading.Tasks.Task RunJobsoidOnlyAsync(bool writeToDatabase, int? insertLimit, string inputCsv, int? limitSites, int? maxJobsPerSite)
        {
            await RunSingleScraperWithDedupeAsync(
                "Jobsoid-Only",
                () => new JobsoidScraper().FetchJobsAsync(inputCsv, limitSites, maxJobsPerSite ?? 0),
                writeToDatabase,
                insertLimit,
                includeRemoteWithoutUsLocation: true);
        }

        private static async System.Threading.Tasks.Task RunApplicantProOnlyAsync(bool writeToDatabase, int? insertLimit, string inputCsv, int? limitSites, int? maxJobsPerSite)
        {
            await RunSingleScraperWithDedupeAsync(
                "ApplicantPro-Only",
                () => new ApplicantProScraper().FetchJobsAsync(inputCsv, limitSites, maxJobsPerSite ?? 0),
                writeToDatabase,
                insertLimit,
                includeRemoteWithoutUsLocation: true);
        }

        private static async System.Threading.Tasks.Task RunCatsOneOnlyAsync(bool writeToDatabase, int? insertLimit, string inputCsv, int? limitSites, int? maxJobsPerSite)
        {
            await RunSingleScraperWithDedupeAsync(
                "CATSOne-Only",
                () => new CatsOneScraper().FetchJobsAsync(inputCsv, limitSites, maxJobsPerSite ?? 0),
                writeToDatabase,
                insertLimit,
                includeRemoteWithoutUsLocation: true);
        }

        private static async System.Threading.Tasks.Task RunZohoRecruitOnlyAsync(bool writeToDatabase, int? insertLimit, string inputCsv, int? limitSites, int? maxJobsPerSite)
        {
            await RunSingleScraperWithDedupeAsync(
                "ZohoRecruit-Only",
                () => new ZohoRecruitScraper().FetchJobsAsync(inputCsv, limitSites, maxJobsPerSite ?? 0),
                writeToDatabase,
                insertLimit,
                includeRemoteWithoutUsLocation: true);
        }

        private static async System.Threading.Tasks.Task RunSingleScraperWithDedupeAsync(
            string label,
            Func<System.Threading.Tasks.Task<List<ScrapedJob>>> fetch,
            bool writeToDatabase,
            int? insertLimit,
            bool includeRemoteWithoutUsLocation)
        {
            Console.WriteLine($"[{label}] Starting scraper...");
            var jobs = await fetch();
            Console.WriteLine($"[{label}] Scraper returned {jobs.Count} jobs (pre-filter).");

            int before = jobs.Count;
            jobs = jobs.Where(j => UsLocationFilter.IsUs(j.Location) || (includeRemoteWithoutUsLocation && j.IsRemote)).ToList();
            Console.WriteLine($"[{label}] US/remote filter: {before} -> {jobs.Count}");

            jobs = ExpandLocationsForDatabase(jobs, label);
            jobs = ClassifyJobsForFlagging(jobs, label);
            PrintJobTypeSummary(label, jobs);

            foreach (var j in jobs.Take(10))
                Console.WriteLine($"  - [{j.Company}] {j.Title} | {j.Location} | {j.JobUrl}");

            if (!writeToDatabase)
            {
                Console.WriteLine($"[{label}] Inspection mode only; add --write-db to insert deduped jobs.");
                return;
            }

            var connectionString = Environment.GetEnvironmentVariable(JobDatabaseSync.ConnectionStringEnvVar);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"Set {JobDatabaseSync.ConnectionStringEnvVar} before writing {label} jobs to the database.");
            }

            Console.WriteLine($"[{label}] Loading existing database keys...");
            var existing = JobDatabaseSync.LoadExistingKeys(connectionString);
            Console.WriteLine($"[{label}] Existing DB urls={existing.Urls.Count}, references={existing.References.Count}, fingerprints={existing.Fingerprints.Count}");

            var deduped = JobDatabaseSync.RemoveDuplicates(jobs, existing);
            Console.WriteLine($"[{label}] Skipped DB duplicates: {deduped.DbDuplicates}");
            Console.WriteLine($"[{label}] Skipped batch duplicates: {deduped.BatchDuplicates}");
            Console.WriteLine($"[{label}] New jobs after dedupe: {deduped.NewJobs.Count}");

            var jobsToInsert = insertLimit.HasValue ? deduped.NewJobs.Take(insertLimit.Value).ToList() : deduped.NewJobs;
            if (insertLimit.HasValue)
            {
                Console.WriteLine($"[{label}] Insert limit enabled: {jobsToInsert.Count} of {deduped.NewJobs.Count} deduped jobs will be inserted.");
            }

            Console.WriteLine($"[{label}] WRITE MODE ENABLED. Refreshing existing duplicates and inserting new jobs into database...");
            WriteDedupeResultToDatabase(connectionString, deduped, jobsToInsert, insertLimit, $"[{label}]");
        }
        private static async System.Threading.Tasks.Task RunSmartRecruitersOnlyAsync(bool writeToDatabase, int? insertLimit, string onlyCompany)
        {
            Console.WriteLine("[SmartRecruiters-Only] Starting scraper...");
            var jobs = await new SmartRecruitersScraper().FetchJobsAsync(onlyCompany);
            Console.WriteLine($"[SmartRecruiters-Only] Scraper returned {jobs.Count} jobs (pre-US-filter).");

            int before = jobs.Count;
            jobs = jobs.Where(j => UsLocationFilter.IsUs(j.Location) || j.IsRemote == true).ToList();
            Console.WriteLine($"[SmartRecruiters-Only] US filter: {before} -> {jobs.Count}");

            jobs = ExpandLocationsForDatabase(jobs, "SmartRecruiters-Only");
            jobs = ClassifyJobsForFlagging(jobs, "SmartRecruiters-Only");

            foreach (var j in jobs.Take(10))
                Console.WriteLine($"  - [{j.Company}] {j.Title} | {j.Location} | {j.JobUrl}");

            if (!writeToDatabase)
            {
                Console.WriteLine("[SmartRecruiters-Only] Inspection mode only; add --write-db to insert deduped jobs.");
                return;
            }

            var connectionString = Environment.GetEnvironmentVariable(JobDatabaseSync.ConnectionStringEnvVar);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Set {JobDatabaseSync.ConnectionStringEnvVar} before writing SmartRecruiters jobs to the database.");
            }

            Console.WriteLine("[SmartRecruiters-Only] Loading existing database keys...");
            var existing = JobDatabaseSync.LoadExistingKeys(connectionString);
            Console.WriteLine(
                $"[SmartRecruiters-Only] Existing DB urls={existing.Urls.Count}, references={existing.References.Count}, fingerprints={existing.Fingerprints.Count}");

            var deduped = JobDatabaseSync.RemoveDuplicates(jobs, existing);

            Console.WriteLine($"[SmartRecruiters-Only] Skipped DB duplicates: {deduped.DbDuplicates}");
            Console.WriteLine($"[SmartRecruiters-Only] Skipped batch duplicates: {deduped.BatchDuplicates}");
            Console.WriteLine($"[SmartRecruiters-Only] New jobs after dedupe: {deduped.NewJobs.Count}");

            var jobsToInsert = insertLimit.HasValue
                ? deduped.NewJobs.Take(insertLimit.Value).ToList()
                : deduped.NewJobs;

            if (insertLimit.HasValue)
            {
                Console.WriteLine($"[SmartRecruiters-Only] Insert limit enabled: {jobsToInsert.Count} of {deduped.NewJobs.Count} deduped jobs will be inserted.");
            }

            Console.WriteLine("[SmartRecruiters-Only] WRITE MODE ENABLED. Refreshing existing duplicates and inserting new jobs into database...");
            WriteDedupeResultToDatabase(connectionString, deduped, jobsToInsert, insertLimit, "[SmartRecruiters-Only]");
        }

        private static async System.Threading.Tasks.Task RunDayforceOnlyAsync(bool writeToDatabase, int? insertLimit, string onlyCompany)
        {
            Console.WriteLine("[Dayforce-Only] Starting scraper...");
            var jobs = await new DayforceScraper().FetchJobsAsync(onlyCompany);
            Console.WriteLine($"[Dayforce-Only] Scraper returned {jobs.Count} jobs (pre-US-filter).");

            int before = jobs.Count;
            jobs = jobs.Where(j => UsLocationFilter.IsUs(j.Location) || j.IsRemote == true).ToList();
            Console.WriteLine($"[Dayforce-Only] US filter: {before} -> {jobs.Count}");

            jobs = ExpandLocationsForDatabase(jobs, "Dayforce-Only");
            jobs = ClassifyJobsForFlagging(jobs, "Dayforce-Only");

            foreach (var j in jobs.Take(10))
                Console.WriteLine($"  - [{j.Company}] {j.Title} | {j.Location} | {j.JobUrl}");

            if (!writeToDatabase)
            {
                Console.WriteLine("[Dayforce-Only] Inspection mode only; add --write-db to insert deduped jobs.");
                return;
            }

            var connectionString = Environment.GetEnvironmentVariable(JobDatabaseSync.ConnectionStringEnvVar);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Set {JobDatabaseSync.ConnectionStringEnvVar} before writing Dayforce jobs to the database.");
            }

            Console.WriteLine("[Dayforce-Only] Loading existing database keys...");
            var existing = JobDatabaseSync.LoadExistingKeys(connectionString);
            Console.WriteLine(
                $"[Dayforce-Only] Existing DB urls={existing.Urls.Count}, references={existing.References.Count}, fingerprints={existing.Fingerprints.Count}");

            var deduped = JobDatabaseSync.RemoveDuplicates(jobs, existing);

            Console.WriteLine($"[Dayforce-Only] Skipped DB duplicates: {deduped.DbDuplicates}");
            Console.WriteLine($"[Dayforce-Only] Skipped batch duplicates: {deduped.BatchDuplicates}");
            Console.WriteLine($"[Dayforce-Only] New jobs after dedupe: {deduped.NewJobs.Count}");

            var jobsToInsert = insertLimit.HasValue
                ? deduped.NewJobs.Take(insertLimit.Value).ToList()
                : deduped.NewJobs;

            if (insertLimit.HasValue)
            {
                Console.WriteLine($"[Dayforce-Only] Insert limit enabled: {jobsToInsert.Count} of {deduped.NewJobs.Count} deduped jobs will be inserted.");
            }

            Console.WriteLine("[Dayforce-Only] WRITE MODE ENABLED. Refreshing existing duplicates and inserting new jobs into database...");
            WriteDedupeResultToDatabase(connectionString, deduped, jobsToInsert, insertLimit, "[Dayforce-Only]");
        }

        private static async System.Threading.Tasks.Task RunRecruiteeOnlyAsync(bool writeToDatabase, int? insertLimit, string onlyCompany)
        {
            Console.WriteLine("[Recruitee-Only] Starting scraper...");
            var jobs = await new RecruiteeScraper().FetchJobsAsync(onlyCompany);
            Console.WriteLine($"[Recruitee-Only] Scraper returned {jobs.Count} jobs (pre-US-filter).");

            int before = jobs.Count;
            jobs = jobs.Where(j => UsLocationFilter.IsUs(j.Location) || j.IsRemote == true).ToList();
            Console.WriteLine($"[Recruitee-Only] US filter: {before} -> {jobs.Count}");

            jobs = ExpandLocationsForDatabase(jobs, "Recruitee-Only");
            jobs = ClassifyJobsForFlagging(jobs, "Recruitee-Only");

            foreach (var j in jobs.Take(10))
                Console.WriteLine($"  - [{j.Company}] {j.Title} | {j.Location} | {j.JobUrl}");

            if (!writeToDatabase)
            {
                Console.WriteLine("[Recruitee-Only] Inspection mode only; add --write-db to insert deduped jobs.");
                return;
            }

            var connectionString = Environment.GetEnvironmentVariable(JobDatabaseSync.ConnectionStringEnvVar);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Set {JobDatabaseSync.ConnectionStringEnvVar} before writing Recruitee jobs to the database.");
            }

            Console.WriteLine("[Recruitee-Only] Loading existing database keys...");
            var existing = JobDatabaseSync.LoadExistingKeys(connectionString);
            Console.WriteLine(
                $"[Recruitee-Only] Existing DB urls={existing.Urls.Count}, references={existing.References.Count}, fingerprints={existing.Fingerprints.Count}");

            var deduped = JobDatabaseSync.RemoveDuplicates(jobs, existing);

            Console.WriteLine($"[Recruitee-Only] Skipped DB duplicates: {deduped.DbDuplicates}");
            Console.WriteLine($"[Recruitee-Only] Skipped batch duplicates: {deduped.BatchDuplicates}");
            Console.WriteLine($"[Recruitee-Only] New jobs after dedupe: {deduped.NewJobs.Count}");

            var jobsToInsert = insertLimit.HasValue
                ? deduped.NewJobs.Take(insertLimit.Value).ToList()
                : deduped.NewJobs;

            if (insertLimit.HasValue)
            {
                Console.WriteLine($"[Recruitee-Only] Insert limit enabled: {jobsToInsert.Count} of {deduped.NewJobs.Count} deduped jobs will be inserted.");
            }

            Console.WriteLine("[Recruitee-Only] WRITE MODE ENABLED. Refreshing existing duplicates and inserting new jobs into database...");
            WriteDedupeResultToDatabase(connectionString, deduped, jobsToInsert, insertLimit, "[Recruitee-Only]");
        }

        private static async System.Threading.Tasks.Task RunTaleoOnlyAsync()
        {
            Console.WriteLine("[Taleo-Only] Starting scraper...");
            var jobs = await new TaleoScraper().FetchJobsAsync();
            Console.WriteLine($"[Taleo-Only] Scraper returned {jobs.Count} jobs (pre-US-filter).");

            int before = jobs.Count;
            jobs = jobs.Where(j => UsLocationFilter.IsUs(j.Location)).ToList();
            Console.WriteLine($"[Taleo-Only] US filter: {before} -> {jobs.Count}");

            jobs = ClassifyJobsForFlagging(jobs, "Taleo-Only");

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
                    //FeedInsert.Post_Jobs(job.Location, job.Title, LocationSplitter.CityOf(job.Location), LocationSplitter.StateOf(job.Location), "", "USA", job.JobType, job.DatePosted.ToString(), job.ExternalId, job.Company, job.IsRemote == true ? 1 : 0, job.Category, "", job.JobUrl, job.Description, "", Sqlconn);
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






