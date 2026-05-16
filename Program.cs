using System;
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


            SqlConnection Sqlconn = new SqlConnection("Data source=209.59.189.133\\ITJOBCAFESERVER,1435;Initial Catalog=feeds;User Id=itjobcafe;Pwd=Chand@789!");
            Sqlconn.Open();

            try
            {
                var jobs = await new FreeJobScraperOrchestrator().RunAllAsync();

                // Plug into your existing ETL:
                foreach (var job in jobs)
                {

                    FeedInsert.Post_Jobs(job.Location, job.Title, "", "", "", "USA", job.JobType, job.DatePosted.ToString(), job.ExternalId, job.Company, job.IsRemote == true ? 1 : 0, job.Category, "", job.JobUrl, job.Description, "", Sqlconn);
                }
                //--workday jobs
                var workdayjobs = await new WorkdayPreprocesser().ProcessAllTenantsAsync();
                foreach (var job in workdayjobs)
                {
                    FeedInsert.Post_Jobs(job.Location, job.Title, "", "", "", "USA", job.JobType, job.DatePosted.ToString(), job.ExternalId, job.Company, job.IsRemote == true ? 1 : 0, job.Category, "", job.JobUrl, job.Description, "", Sqlconn);
                }

            }

            catch (Exception ex)
            {
                Console.Write("error Message:" + ex.Message);
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

    }
}

