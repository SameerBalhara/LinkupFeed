
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using System.Threading.Tasks;

namespace LinkupFeed
{
    

public static class FeedInsert
    {


        public static string GetLast(string source, int tail_length)
        {
            if (tail_length >= source.Length)
                return source;
            return source.Substring(source.Length - tail_length);
        }        

        
        public static void Post_Jobs(string Location, string title, string City, string State, string Zip, string country, string Jobtype, string posted_at, string job_reference, string company, int Isremote, string category, string html_jobs, string url, string body, string cpc, SqlConnection Sqlconn)
        {
           try
            {
              
                SqlParameter[] par = new SqlParameter[16];
                par[0] = new SqlParameter("@Location", Location);
                par[1] = new SqlParameter("@title", title);
                par[2] = new SqlParameter("@City", City);
                par[3] = new SqlParameter("@State", State);
                par[4] = new SqlParameter("@Zip", Zip);
                par[5] = new SqlParameter("@country", country);
                par[6] = new SqlParameter("@Job_type", Jobtype);
                par[7] = new SqlParameter("@posted_at", posted_at);
                par[8] = new SqlParameter("@job_reference", job_reference);
                par[9] = new SqlParameter("@company", company);
                par[10] = new SqlParameter("@Isremote", Isremote);
                par[11] = new SqlParameter("@category", category);
                par[12] = new SqlParameter("@html_jobs", html_jobs);
                par[13] = new SqlParameter("@url", url);
                par[14] = new SqlParameter("@body", body);
                par[15] = new SqlParameter("@cpc", 0);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Jobs_Insert_SP", par);
            }
            catch (Exception ex)
            {
                throw;
            }
          
        }
    }
}

