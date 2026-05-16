
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



        public static string ConStr = "Data source=209.59.189.133\\ITJOBCAFESERVER,1435;Initial Catalog=Feeds;User Id=itjobcafe;Pwd=Chand@789!";
     


        public static void PostEquestJob(string Internal_id, string CName, string StreetAddress, string City, string State, string PostalCode, string Phone, string Fax, string RName, string REmail, string JobTitle, string Skills, string Description, string PositionType, string Job_City, string Job_PostalCode, string Job_State, string Job_Applyurl, string Job_ApplyEmailId, string Job_Actionrequest)
        {

            SqlConnection Sqlconn = new SqlConnection(ConStr);


            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[20];
                par[0] = new SqlParameter("@InternalID", Internal_id);
                par[1] = new SqlParameter("@CName", CName);
                par[2] = new SqlParameter("@StreetAddress", StreetAddress);
                par[3] = new SqlParameter("@City", City);
                par[4] = new SqlParameter("@State", State);
                par[5] = new SqlParameter("@PostalCode", PostalCode);
                par[6] = new SqlParameter("@Phone", Phone);
                par[7] = new SqlParameter("@Fax", Fax);
                par[8] = new SqlParameter("@RName", RName);
                par[9] = new SqlParameter("@REmail", REmail);
                par[10] = new SqlParameter("@JobTitle", JobTitle);
                par[11] = new SqlParameter("@Skills", Skills);
                par[12] = new SqlParameter("@Description", Description);
                par[13] = new SqlParameter("@PositionType", PositionType);
                par[14] = new SqlParameter("@Job_City", Job_City);
                par[15] = new SqlParameter("@Job_PostalCode", Job_PostalCode);
                par[16] = new SqlParameter("@Job_State", Job_State);
                par[17] = new SqlParameter("@Job_Applyurl", Job_Applyurl);
                par[18] = new SqlParameter("@Job_ApplyEmailId", Job_ApplyEmailId);
                par[19] = new SqlParameter("@Job_Actionrequest", Job_Actionrequest);

                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Insert_EquestJob", par);
                // Status = par[3].Value.ToString();

            }
            catch (Exception ex)
            {

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

        public static void PostMyjobhelperJobs(string Location, string title, string City, string State, string Zip, string country, string Jobtype, string posted_at, string job_reference, string company, string mobile_friendly_apply, string category, string html_jobs, string url, string body, string cpc)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);
            //@Location ,@Title ,@city ,@state ,@zip ,@country,@job_type ,@posted_at ,@job_reference ,@company ,@mobile_friendly_apply varchar(20),@category varchar(150),@html_jobs varchar(20),@url varchar(800),@body varchar(max),@cpc decimal(5,2)

            try
            {
                Sqlconn.Open();
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
                par[10] = new SqlParameter("@mobile_friendly_apply", mobile_friendly_apply);
                par[11] = new SqlParameter("@category", category);
                par[12] = new SqlParameter("@html_jobs", html_jobs);
                par[13] = new SqlParameter("@url", url);
                par[14] = new SqlParameter("@body", body);
                par[15] = new SqlParameter("@cpc", cpc);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "MYjobHelperInsertJobfeed", par);


            }
            catch (Exception ex)
            {
                throw ex;
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



        public static void PostAdzunaJobs(string JobTitle, string job_reference, string Zip, string City, string State, string Description, string company, string url, string Jobtype, string Tag, string Postedate, string cpc)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);

            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[12];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@City", City);
                par[2] = new SqlParameter("@State", State);
                par[3] = new SqlParameter("@Zip", Zip);
                par[4] = new SqlParameter("@job_reference", job_reference);
                par[5] = new SqlParameter("@body", Description);
                par[6] = new SqlParameter("@company", company);
                par[7] = new SqlParameter("@url", url);
                par[8] = new SqlParameter("@Jobtype", Jobtype);
                par[9] = new SqlParameter("@Tag", Tag);
                par[10] = new SqlParameter("@Postedate", Postedate);
                par[11] = new SqlParameter("@Cpc", cpc);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Adzuna_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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

        public static void PostAdzunaNOnITJobs(string JobTitle, string job_reference, string Zip, string City, string State, string Description, string company, string url, string Jobtype, string Tag, string Postedate, string cpc)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);


            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[12];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@City", City);
                par[2] = new SqlParameter("@State", State);
                par[3] = new SqlParameter("@Zip", Zip);
                par[4] = new SqlParameter("@job_reference", job_reference);
                par[5] = new SqlParameter("@body", Description);
                par[6] = new SqlParameter("@company", company);
                par[7] = new SqlParameter("@url", url);
                par[8] = new SqlParameter("@Jobtype", Jobtype);
                par[9] = new SqlParameter("@Tag", Tag);
                par[10] = new SqlParameter("@Postedate", Postedate);
                par[11] = new SqlParameter("@cpc", cpc);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Adzuna_NonIT_InsertJobFeed", par);


            }
            catch (Exception ex)
            {
                throw ex;
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

        public static void PostwaysupJobs(string Location, string title, string City, string State, string Zip, string country, string Jobtype, string posted_at, string job_reference, string company, string mobile_friendly_apply, string category, string html_jobs, string url, string body, string cpc)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);
            //@Location ,@Title ,@city ,@state ,@zip ,@country,@job_type ,@posted_at ,@job_reference ,@company ,@mobile_friendly_apply varchar(20),@category varchar(150),@html_jobs varchar(20),@url varchar(800),@body varchar(max),@cpc decimal(5,2)

            try
            {
                Sqlconn.Open();
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
                par[10] = new SqlParameter("@mobile_friendly_apply", mobile_friendly_apply);
                par[11] = new SqlParameter("@category", category);
                par[12] = new SqlParameter("@html_jobs", html_jobs);
                par[13] = new SqlParameter("@url", url);
                par[14] = new SqlParameter("@body", body);
                par[15] = new SqlParameter("@cpc", cpc);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "spWayupInsertJobfeed", par);


            }
            catch (Exception ex)
            {
                throw ex;
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
        public static void PostResumeLibraryITJobs(string Job_ID, string Job_title, string job_Type, string Description, string location_text, string Distance, string Latitude, string longitude, string industry, string company, string Post_date, string expire_date, string job_url, string salary_text, string annual_salary_max, string annual_salary_min)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);


            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[16];
                par[0] = new SqlParameter("@Job_ID", Job_ID);
                par[1] = new SqlParameter("@Job_title", Job_title);
                par[2] = new SqlParameter("@job_Type", job_Type);
                par[3] = new SqlParameter("@Description", Description);
                par[4] = new SqlParameter("@location_text", location_text);
                par[5] = new SqlParameter("@Distance", Distance);
                par[6] = new SqlParameter("@Latitude", Latitude);
                par[7] = new SqlParameter("@longitude", longitude);
                par[8] = new SqlParameter("@industry", industry);
                par[9] = new SqlParameter("@company", company);
                par[10] = new SqlParameter("@Post_date", Post_date);
                par[11] = new SqlParameter("@expire_date", expire_date);
                par[12] = new SqlParameter("@job_url", job_url);
                par[13] = new SqlParameter("@salary_text", salary_text);
                par[14] = new SqlParameter("@annual_salary_max", annual_salary_max);
                par[15] = new SqlParameter("@annual_salary_min", annual_salary_min);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "spResumeLibrary_InsertJobfeed", par);


            }
            catch (Exception ex)
            {
                throw ex;
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


        public static void PostLensaITJobs(string job_reference, string title, string State, string Zip, string City, string Description, string company, string url, string Jobtype, string Tag, string posted_at, string cpc)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);


            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[12];
                par[0] = new SqlParameter("@job_reference", job_reference);
                par[1] = new SqlParameter("@title", title);
                par[2] = new SqlParameter("@State", State);
                par[3] = new SqlParameter("@Zip", Zip);
                par[4] = new SqlParameter("@City", City);
                par[5] = new SqlParameter("@body", Description);
                par[6] = new SqlParameter("@company", company);
                par[7] = new SqlParameter("@url", url);
                par[8] = new SqlParameter("@Jobtype", Jobtype);
                par[9] = new SqlParameter("@category", Tag);
                par[10] = new SqlParameter("@posted_at", posted_at);
                par[11] = new SqlParameter("@cpc", cpc);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "LensaInsertJobFeed", par);


            }
            catch (Exception ex)
            {
                throw ex;
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

        public static void PostsampleJobs(string ID, string url, string title, string Cpc, string Description, string Date, string Category, string Country, string Latitude, string Longitute, string City, string State, string Company)
        {


            SqlConnection Sqlconn = new SqlConnection(ConStr);

            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[13];
                par[0] = new SqlParameter("@ID", ID);
                par[1] = new SqlParameter("@url", url);
                par[2] = new SqlParameter("@title", title);
                par[3] = new SqlParameter("@Cpc", Cpc);
                par[4] = new SqlParameter("@Description", Description);
                par[5] = new SqlParameter("@Date", Date);
                par[6] = new SqlParameter("@Category", Category);
                par[7] = new SqlParameter("@Country", Country);
                par[8] = new SqlParameter("@Latitude", Latitude);
                par[9] = new SqlParameter("@Longitute", Longitute);
                par[10] = new SqlParameter("@City", City);
                par[11] = new SqlParameter("@State", State);
                par[12] = new SqlParameter("@Company", Company);
                //@ID ,@url ,@title ,@Cpc ,@Description ,@Date ,@Category ,@Country ,@Latitude ,@Longitute ,@City ,@State ,@Company ,
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Sample_InsertJobFeed", par);


            }
            catch (Exception ex)
            {
                throw ex;
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

        public static void PostECJobs(string JobTitle, string PostedDate, string Referencenumber, string postalcode, string city, string state, string jobdescription, string company, string applyurl, string minexperience)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);



            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[10];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@PostedDate", PostedDate);
                par[2] = new SqlParameter("@Referencenumber", Referencenumber);
                par[3] = new SqlParameter("@postalcode", postalcode);
                par[4] = new SqlParameter("@city", city);
                par[5] = new SqlParameter("@state", state);
                par[6] = new SqlParameter("@jobdescription", jobdescription);
                par[7] = new SqlParameter("@company", company);
                par[8] = new SqlParameter("@applyurl", applyurl);
                par[9] = new SqlParameter("@minexperience", minexperience);


                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Insert_ECJob", par);


            }
            catch (Exception ex)
            {

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


        public static void PostJobg8Job(string JobTitle, string DisplayReference, string Referencenumber, string postalcode, string city, string state, string jobdescription, string company, string applyurl, string posteddate,string cpc,string category)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);



            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[10];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@DisplayReference", DisplayReference);
                par[2] = new SqlParameter("@Referencenumber", Referencenumber);
                par[3] = new SqlParameter("@postalcode", postalcode);
                par[4] = new SqlParameter("@city", city);
                par[5] = new SqlParameter("@state", state);
                par[6] = new SqlParameter("@jobdescription", jobdescription);
                par[7] = new SqlParameter("@company", company);
                par[8] = new SqlParameter("@applyurl", applyurl);
                par[9] = new SqlParameter("@posteddate", posteddate);
                par[10] = new SqlParameter("@cpc", cpc);
                par[10] = new SqlParameter("@category", category);
                 int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Jobg8_InsertJobfeed", par);


            }
            catch (Exception ex)
            {

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


        public static void PostJob2careerCPA(string JobTitle, string PostedDate, string Referencenumber, string postalcode, string city, string state, string jobdescription, string company, string applyurl)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);



            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[9];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@PostedDate", PostedDate);
                par[2] = new SqlParameter("@Referencenumber", Referencenumber);
                par[3] = new SqlParameter("@postalcode", postalcode);
                par[4] = new SqlParameter("@city", city);
                par[5] = new SqlParameter("@state", state);
                par[6] = new SqlParameter("@jobdescription", jobdescription);
                par[7] = new SqlParameter("@company", company);
                par[8] = new SqlParameter("@applyurl", applyurl);

                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "J2c_InsertPremiumFeed", par);


            }
            catch (Exception ex)
            {

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



        public static void PostAppCastJobs(string JobTitle, string posted_at, string job_reference, string Zip, string City, string State, string Description, string company, string url, string job_type, string category, string cpc_guarantee, string appcast_category, string cpc, string cpa)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);



            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[15];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@City", City);
                par[2] = new SqlParameter("@State", State);
                par[3] = new SqlParameter("@Zip", Zip);
                par[4] = new SqlParameter("@job_type", job_type);
                par[5] = new SqlParameter("@posted_at", posted_at);
                par[6] = new SqlParameter("@job_reference", job_reference);
                par[7] = new SqlParameter("@Description", Description);
                par[8] = new SqlParameter("@company", company);
                par[9] = new SqlParameter("@url", url);
                par[10] = new SqlParameter("@category", category);
                par[11] = new SqlParameter("@appcast_category", appcast_category);
                par[12] = new SqlParameter("@cpc_guarantee", cpc_guarantee);
                par[13] = new SqlParameter("@cpc", cpc);
                par[14] = new SqlParameter("@cpa", cpa);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Appcast_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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




        public static void PostAppCastNOnITJobs(string JobTitle, string posted_at, string job_reference, string Zip, string City, string State, string Description, string company, string url, string job_type, string category, string cpc)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);



            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[12];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@City", City);
                par[2] = new SqlParameter("@State", State);
                par[3] = new SqlParameter("@Zip", Zip);
                par[4] = new SqlParameter("@job_type", job_type);
                par[5] = new SqlParameter("@posted_at", posted_at);
                par[6] = new SqlParameter("@job_reference", job_reference);
                par[7] = new SqlParameter("@Description", Description);
                par[8] = new SqlParameter("@company", company);
                par[9] = new SqlParameter("@url", url);
                par[10] = new SqlParameter("@category", category);
                par[11] = new SqlParameter("@cpc", cpc);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Appcast_Non_IT_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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


        public static void PostAppCastCPCJobs(string JobTitle, string posted_at, string job_reference, string Zip, string City, string State, string Description, string company, string url, string job_type, string category, string cpc_guarantee, string appcast_category, string cpc, string cpa)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);



            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[15];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@City", City);
                par[2] = new SqlParameter("@State", State);
                par[3] = new SqlParameter("@Zip", Zip);
                par[4] = new SqlParameter("@job_type", job_type);
                par[5] = new SqlParameter("@posted_at", posted_at);
                par[6] = new SqlParameter("@job_reference", job_reference);
                par[7] = new SqlParameter("@Description", Description);
                par[8] = new SqlParameter("@company", company);
                par[9] = new SqlParameter("@url", url);
                par[10] = new SqlParameter("@category", category);
                par[11] = new SqlParameter("@appcast_category", appcast_category);
                par[12] = new SqlParameter("@cpc_guarantee", cpc_guarantee);
                par[13] = new SqlParameter("@cpc", cpc);
                par[14] = new SqlParameter("@cpa", "0.0");
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Appcastcpc_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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



        public static string GetLast(string source, int tail_length)
        {
            if (tail_length >= source.Length)
                return source;
            return source.Substring(source.Length - tail_length);
        }

        public static void PostRippleJobs(string JobTitle, string job_reference, string Zip, string City, string State, string Description, string company, string url, string cpc)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);


            cpc = cpc.Replace("USD ", "");
            job_reference = GetLast(job_reference, 17);
            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[9];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@City", City);
                par[2] = new SqlParameter("@State", State);
                par[3] = new SqlParameter("@Zip", Zip);
                par[4] = new SqlParameter("@job_reference", job_reference);
                par[5] = new SqlParameter("@Description", Description);
                par[6] = new SqlParameter("@company", company);
                par[7] = new SqlParameter("@url", url);
                par[8] = new SqlParameter("@cpc", cpc);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "RippleMedia_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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

        public static void PostIvvyJobs(string JobTitle, string job_reference, string Zip, string City, string State, string Description, string company, string url, string cpc, string posteddate, string location, string category, string job_type, string country)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);


            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[14];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@City", City);
                par[2] = new SqlParameter("@State", State);
                par[3] = new SqlParameter("@Zip", Zip);
                par[4] = new SqlParameter("@job_reference", job_reference);
                par[5] = new SqlParameter("@Description", Description);
                par[6] = new SqlParameter("@company", company);
                par[7] = new SqlParameter("@url", url);
                par[8] = new SqlParameter("@cpc", cpc);
                par[9] = new SqlParameter("@posteddate", posteddate);
                par[10] = new SqlParameter("@location", location);
                par[11] = new SqlParameter("@category", category);
                par[12] = new SqlParameter("@job_type", job_type);
                par[13] = new SqlParameter("@country", country);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Ivvy_InsertFeed", par);


            }
            catch (Exception ex)
            {

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
        public static void PostJobTargetJobs(string JobTitle, string job_reference, string Zip, string City, string State, string Description, string company, string url, string cpc, string posteddate)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);


            cpc = cpc.Replace("USD ", "");
            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[10];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@City", City);
                par[2] = new SqlParameter("@State", State);
                par[3] = new SqlParameter("@Zip", Zip);
                par[4] = new SqlParameter("@job_reference", job_reference);
                par[5] = new SqlParameter("@Description", Description);
                par[6] = new SqlParameter("@company", company);
                par[7] = new SqlParameter("@url", url);
                par[8] = new SqlParameter("@cpc", cpc);
                par[9] = new SqlParameter("@posteddate", posteddate);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Jobtarget_InsertFeed", par);


            }
            catch (Exception ex)
            {

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
        public static void PostNeuvooJobs(string JobTitle, string job_reference, string Zip, string City, string State, string Description, string company, string url, string cpc, string tag, string postedatte, string logourl)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);


            cpc = cpc.Replace("USD ", "");
            //job_reference = GetLast(job_reference, 17);
            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[12];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@City", City);
                par[2] = new SqlParameter("@State", State);
                par[3] = new SqlParameter("@Zip", Zip);
                par[4] = new SqlParameter("@job_reference", job_reference);
                par[5] = new SqlParameter("@Description", Description);
                par[6] = new SqlParameter("@company", company);
                par[7] = new SqlParameter("@url", url);
                par[8] = new SqlParameter("@cpc", cpc);
                par[9] = new SqlParameter("@tag", tag);
                par[10] = new SqlParameter("@PostedDate", postedatte);
                par[11] = new SqlParameter("@LogoUrl", logourl);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Neuvoo_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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


        public static void PostJ2cJobs(string title, DateTime data, string referencenumber, string url, string company, string city, string state, string zip, string Desription, string major_category0, string major_category1, string major_category2, string minor_category0, string minor_category1, string minor_category2, string ranking_value, DateTime updated)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);



            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[17];
                par[0] = new SqlParameter("@title", title);
                par[1] = new SqlParameter("@data", data);
                par[2] = new SqlParameter("@referencenumber", referencenumber);
                par[3] = new SqlParameter("@url", url);
                par[4] = new SqlParameter("@company", company);
                par[5] = new SqlParameter("@city", city);
                par[6] = new SqlParameter("@state", state);
                par[7] = new SqlParameter("@zip", zip);
                par[8] = new SqlParameter("@Desription", Desription);
                par[9] = new SqlParameter("@major_category0", major_category0);
                par[10] = new SqlParameter("@major_category1", major_category1);
                par[11] = new SqlParameter("@major_category2", major_category2);
                par[12] = new SqlParameter("@minor_category0", minor_category0);
                par[13] = new SqlParameter("@minor_category1", minor_category1);
                par[14] = new SqlParameter("@minor_category2", minor_category2);
                par[15] = new SqlParameter("@ranking_value", ranking_value);
                par[16] = new SqlParameter("@updated", updated);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "J2c_InsertJobFeed", par);

            }
            catch (Exception ex)
            {

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


        public static void PostJobg8nonITJobs(string Company, string SenderRef, string DisplayRef, string subclassification, string Position, string Description, string PostalCode, string ApplicationUrl, string DescriptionUrl, string ContactName, string startdate, string Duration, string Salarymin, string Salarymax, string SalaryAddition, string JobSource, string VideoUrl, string logourl, string Jobtype, string Sellprice, string Classification, string ADC1, string ADC2, string ADC3, string ADC4, string EmploymentType, string WorkHours, string State, string City)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);



            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[29];
                par[0] = new SqlParameter("@Company", Company);
                par[1] = new SqlParameter("@SenderRef", SenderRef);
                par[2] = new SqlParameter("@DisplayRef", DisplayRef);
                par[3] = new SqlParameter("@subclassification", subclassification);
                par[4] = new SqlParameter("@Position", Position);
                par[5] = new SqlParameter("@Description", Description);
                par[6] = new SqlParameter("@PostalCode", PostalCode);
                par[7] = new SqlParameter("@ApplicationUrl", ApplicationUrl);
                par[8] = new SqlParameter("@DescriptionUrl", DescriptionUrl);
                par[9] = new SqlParameter("@ContactName", ContactName);
                par[10] = new SqlParameter("@startdate", startdate);
                par[11] = new SqlParameter("@Duration", Duration);
                par[12] = new SqlParameter("@Salarymin", Salarymin);
                par[13] = new SqlParameter("@Salarymax", Salarymax);
                par[14] = new SqlParameter("@SalaryAddition", SalaryAddition);
                par[15] = new SqlParameter("@JobSource", JobSource);
                par[16] = new SqlParameter("@VideoUrl", VideoUrl);
                par[17] = new SqlParameter("@logourl", logourl);
                par[18] = new SqlParameter("@Jobtype", Jobtype);
                par[19] = new SqlParameter("@Sellprice", Sellprice);
                par[20] = new SqlParameter("@Classification", Classification);
                par[21] = new SqlParameter("@ADC1", ADC1);
                par[22] = new SqlParameter("@ADC2", ADC2);
                par[23] = new SqlParameter("@ADC3", ADC3);
                par[24] = new SqlParameter("@ADC4", ADC4);
                par[25] = new SqlParameter("@EmploymentType", EmploymentType);
                par[26] = new SqlParameter("@WorkHours", WorkHours);
                par[27] = new SqlParameter("@State", State);
                par[28] = new SqlParameter("@City", City);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Jobg8_non_IT_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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
        public static void PostCapitalMarketJobs(string JobTitle, string posted_at, string job_reference, string Zip, string City, string State, string Description, string company, string url, string job_type, string category, string cpc)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);



            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[12];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@City", City);
                par[2] = new SqlParameter("@State", State);
                par[3] = new SqlParameter("@Zip", Zip);
                par[4] = new SqlParameter("@job_type", job_type);
                par[5] = new SqlParameter("@posted_at", posted_at);
                par[6] = new SqlParameter("@job_reference", job_reference);
                par[7] = new SqlParameter("@Description", Description);
                par[8] = new SqlParameter("@company", company);
                par[9] = new SqlParameter("@url", url);
                par[10] = new SqlParameter("@category", category);
                par[11] = new SqlParameter("@cpc", cpc);

                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "CapitalMarkets_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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


        public static void PostJoveoJobs_old(string JobTitle, string job_reference, string Zip, string City, string State, string Description, string company, string url, string cpc, string cpa, string postedate, string advertiser, string Category)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);


            cpc = cpc.Replace("USD ", "");
            //job_reference = GetLast(job_reference, 17);
            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[13];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@City", City);
                par[2] = new SqlParameter("@State", State);
                par[3] = new SqlParameter("@Zip", Zip);
                par[4] = new SqlParameter("@job_reference", job_reference);
                par[5] = new SqlParameter("@Description", Description);
                par[6] = new SqlParameter("@company", company);
                par[7] = new SqlParameter("@url", url);
                par[8] = new SqlParameter("@cpc", cpc);
                par[9] = new SqlParameter("@Category", Category);
                par[10] = new SqlParameter("@posted_date", postedate);
                par[11] = new SqlParameter("@advertiser", advertiser);
                par[12] = new SqlParameter("@cpa", cpa);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "joveo_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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

        public static void PostRealMatchJobs(string JobTitle, string posted_at, string job_reference, string Zip, string City, string State, string Description, string company, string url, string category, string cpc, string Exp)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);



            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[12];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@City", City);
                par[2] = new SqlParameter("@State", State);
                par[3] = new SqlParameter("@Zip", Zip);
                par[4] = new SqlParameter("@posted_at", posted_at);
                par[5] = new SqlParameter("@job_reference", job_reference);
                par[6] = new SqlParameter("@Description", Description);
                par[7] = new SqlParameter("@company", company);
                par[8] = new SqlParameter("@url", url);
                par[9] = new SqlParameter("@category", category);
                par[10] = new SqlParameter("@cpc", cpc);
                par[11] = new SqlParameter("@Exp", Exp);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "RealMatch_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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

        //Blog Posting Team codes//
        public static void PostStartWireJobs(string JobTitle, string posted_at, string job_reference, string Zip, string City, string State, string Description, string company, string url, string job_type, string category, string cpc)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);



            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[12];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@City", City);
                par[2] = new SqlParameter("@State", State);
                par[3] = new SqlParameter("@Zip", Zip);
                par[4] = new SqlParameter("@job_type", job_type);
                par[5] = new SqlParameter("@posted_at", posted_at);
                par[6] = new SqlParameter("@job_reference", job_reference);
                par[7] = new SqlParameter("@body", Description);
                par[8] = new SqlParameter("@company", company);
                par[9] = new SqlParameter("@url", url);
                par[10] = new SqlParameter("@category", category);
                par[11] = new SqlParameter("@cpc", cpc);

                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "StartWire_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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


        public static void PostZipJobs(string JobTitle, string posted_at, string job_reference, string Zip, string City, string State, string Description, string company, string url, string cpc, string category = "")
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);



            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[12];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@City", City);
                par[2] = new SqlParameter("@State", State);
                par[3] = new SqlParameter("@Zip", Zip);
                par[4] = new SqlParameter("@job_type", "Full Time");
                par[5] = new SqlParameter("@posted_at", posted_at);
                par[6] = new SqlParameter("@job_reference", job_reference);
                par[7] = new SqlParameter("@body", Description);
                par[8] = new SqlParameter("@company", company);
                par[9] = new SqlParameter("@url", url);
                par[10] = new SqlParameter("@cpc", cpc);
                par[11] = new SqlParameter("@category", category);

                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Zip_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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
        public static void PostNextJobs(string JobTitle, string posted_at, string job_reference, string Zip, string City, string State, string Description, string company, string url)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);



            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[10];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@City", City);
                par[2] = new SqlParameter("@State", State);
                par[3] = new SqlParameter("@Zip", Zip);
                par[4] = new SqlParameter("@job_type", "Full Time");
                par[5] = new SqlParameter("@posted_at", posted_at);
                par[6] = new SqlParameter("@job_reference", job_reference);
                par[7] = new SqlParameter("@body", Description);
                par[8] = new SqlParameter("@company", company);
                par[9] = new SqlParameter("@url", url);

                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Next_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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


        public static void PostRecruitologyJobs(string JobTitle, string posted_at, string job_reference, string Zip, string City, string State, string Description, string company, string url, string job_type, string Skills, string cpc, string Qualification)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);



            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[13];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@City", City);
                par[2] = new SqlParameter("@State", State);
                par[3] = new SqlParameter("@Zip", Zip);
                par[4] = new SqlParameter("@job_type", job_type);
                par[5] = new SqlParameter("@posted_at", posted_at);
                par[6] = new SqlParameter("@job_reference", job_reference);
                par[7] = new SqlParameter("@Description", Description);
                par[8] = new SqlParameter("@company", company);
                par[9] = new SqlParameter("@url", url);
                par[10] = new SqlParameter("@Qualification", Qualification);
                par[11] = new SqlParameter("@Skills", Skills);
                par[12] = new SqlParameter("@cpc", cpc);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Recruitology_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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

        public static void PostRecruitologyNonITJobs(string JobTitle, string posted_at, string job_reference, string Zip, string City, string State, string Description, string company, string url, string job_type, string Skills, string cpc, string Qualification)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);



            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[13];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@City", City);
                par[2] = new SqlParameter("@State", State);
                par[3] = new SqlParameter("@Zip", Zip);
                par[4] = new SqlParameter("@job_type", job_type);
                par[5] = new SqlParameter("@posted_at", posted_at);
                par[6] = new SqlParameter("@job_reference", job_reference);
                par[7] = new SqlParameter("@Description", Description);
                par[8] = new SqlParameter("@company", company);
                par[9] = new SqlParameter("@url", url);
                par[10] = new SqlParameter("@Qualification", Qualification);
                par[11] = new SqlParameter("@Skills", Skills);
                par[12] = new SqlParameter("@cpc", cpc);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Recruitology_NonITJobs_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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

        public static void PostJob2meJobs(string JobTitle, string job_reference, string Zip, string City, string State, string Description, string company, string url, string cpc, string postedate, string Category)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);


            cpc = cpc.Replace("USD ", "");
            //job_reference = GetLast(job_reference, 17);
            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[11];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@City", City);
                par[2] = new SqlParameter("@State", State);
                par[3] = new SqlParameter("@Zip", Zip);
                par[4] = new SqlParameter("@job_reference", job_reference);
                par[5] = new SqlParameter("@Description", Description);
                par[6] = new SqlParameter("@company", company);
                par[7] = new SqlParameter("@url", url);
                par[8] = new SqlParameter("@cpc", cpc);
                par[9] = new SqlParameter("@Category", Category);
                par[10] = new SqlParameter("@posted_date", postedate);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Job2mejobs_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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

        public static void PostDicePTJobs(string Location, string title, string City, string State, string Zip, string country, string Jobtype, string posted_at, string job_reference, string company, string mobile_friendly_apply, string category, string html_jobs, string url, string body, string cpc)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);
            //@Location ,@Title ,@city ,@state ,@zip ,@country,@job_type ,@posted_at ,@job_reference ,@company ,@mobile_friendly_apply varchar(20),@category varchar(150),@html_jobs varchar(20),@url varchar(800),@body varchar(max),@cpc decimal(5,2)

            try
            {
                Sqlconn.Open();
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
                par[10] = new SqlParameter("@mobile_friendly_apply", mobile_friendly_apply);
                par[11] = new SqlParameter("@category", category);
                par[12] = new SqlParameter("@html_jobs", html_jobs);
                par[13] = new SqlParameter("@url", url);
                par[14] = new SqlParameter("@body", body);
                par[15] = new SqlParameter("@cpc", cpc);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "spDicePTInsertJobfeed", par);


            }
            catch (Exception ex)
            {
                throw ex;
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
        public static void Post_Jobs(string Location, string title, string City, string State, string Zip, string country, string Jobtype, string posted_at, string job_reference, string company, int Isremote, string category, string html_jobs, string url, string body, string cpc, SqlConnection Sqlconn)
        {
            // SqlConnection Sqlconn = new SqlConnection("Data source=209.59.189.133\\ITJOBCAFESERVER,1435;Initial Catalog=feeds;User Id=itjobcafe;Pwd=Chand@789!");
            //@Location ,@Title ,@city ,@state ,@zip ,@country,@job_type ,@posted_at ,@job_reference ,@company ,@mobile_friendly_apply varchar(20),@category varchar(150),@html_jobs varchar(20),@url varchar(800),@body varchar(max),@cpc decimal(5,2)


            try
            {
                // Sqlconn.Open();
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
                throw ex;
            }
            //finally
            //{
            //    if (Sqlconn != null)
            //    {
            //        if (Sqlconn.State.ToString() == "Open")
            //        {
            //            Sqlconn.Close();
            //        }
            //    }
            //}
        }



        public static void PostLinkupJobs(string Title, string description, string company_name, string onet_occupation_code, string location,string state, string Zip_code, string jobid, string url, string Created_date, string cpc,string jobCategory,string expdate, string jobtype)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);

            var location1 = location.Split(",");
            var city=location1[0];
            var state1=location1[1];

            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[11];
                par[0] = new SqlParameter("@Title", Title);
                par[1] = new SqlParameter("@description", description);
                par[2] = new SqlParameter("@company_name", company_name);
                par[3] = new SqlParameter("@onet_occupation_code", onet_occupation_code);
                par[4] = new SqlParameter("@City", city);
                par[5] = new SqlParameter("@State", state);
                par[6] = new SqlParameter("@Zip_code", Zip_code);
                par[7] = new SqlParameter("@jobid", jobid);
                par[8] = new SqlParameter("@Created_date", Created_date);
                par[9] = new SqlParameter("@Cpc", cpc);
                par[10] = new SqlParameter("@url", url);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Linkup_insertxmlFeed", par);


            }
            catch (Exception ex)
            {

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


        public async static Task ExecuteLongRunningStoredProcedure()
        {
            Console.WriteLine("Procedure Executed start");

            using (SqlConnection connection = new SqlConnection("Data source=209.59.189.133\\ITJOBCAFESERVER,1435;Initial Catalog=FEEDS;User Id=itjobcafe;Pwd=Chand@789!"))
            {
                using (SqlCommand command = new SqlCommand("JOBPROCES_ASPENTECH", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;

                    // Set timeout to 0 for no timeout, or specify seconds (e.g., 600 for 10 minutes)
                    command.CommandTimeout = 0; // or 3600 for 1 hour                           

                    await connection.OpenAsync();

                    // Or for non-query (INSERT/UPDATE/DELETE):
                    int rowsAffected = await command.ExecuteNonQueryAsync();


                }
            }
            Console.WriteLine("Procedure Executed completed");
        }
        public static void PostZippiaJobs(string Title, string description, string company_name, string onet_occupation_code, string City, string State, string Zip_code, string jobid, string url, string Created_date, string cpc)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);



            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[11];
                par[0] = new SqlParameter("@Title", Title);
                par[1] = new SqlParameter("@description", description);
                par[2] = new SqlParameter("@company_name", company_name);
                par[3] = new SqlParameter("@onet_occupation_code", onet_occupation_code);
                par[4] = new SqlParameter("@City", City);
                par[5] = new SqlParameter("@State", State);
                par[6] = new SqlParameter("@Zip_code", Zip_code);
                par[7] = new SqlParameter("@jobid", jobid);
                par[8] = new SqlParameter("@Created_date", Created_date);
                par[9] = new SqlParameter("@Cpc", cpc);
                par[10] = new SqlParameter("@url", url);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Zippia_insertxmlFeed", par);


            }
            catch (Exception ex)
            {

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

        public static void PostITJCJobs(string Title, string description, string company_name, string City, string State, string Zip_code, string jobid, string url, string Created_date, string skills)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);



            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[10];
                par[0] = new SqlParameter("@Title", Title);
                par[1] = new SqlParameter("@description", description);
                par[2] = new SqlParameter("@company_name", company_name);
                par[3] = new SqlParameter("@City", City);
                par[4] = new SqlParameter("@State", State);
                par[5] = new SqlParameter("@Zip_code", Zip_code);
                par[6] = new SqlParameter("@jobid", jobid);
                par[7] = new SqlParameter("@Created_date", Created_date);
                par[8] = new SqlParameter("@url", url);
                par[9] = new SqlParameter("@skills", skills);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "ITJC_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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

        public static void PostJoveoJobs(string company, string advertiser, string title, string referenceNumber, string City, string State, string postalcode, string Description, string Country, string url, string Category, string CPA, string cpc, string dateposted)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);
            cpc = cpc.Replace("USD ", "");
            dateposted = dateposted.Replace(" UTC", "");

            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[14];
                par[0] = new SqlParameter("@company", company);
                par[1] = new SqlParameter("@advertiser", advertiser);
                par[2] = new SqlParameter("@title", title);
                par[3] = new SqlParameter("@referenceNumber", referenceNumber);
                par[4] = new SqlParameter("@city", City);
                par[5] = new SqlParameter("@State", State);
                par[6] = new SqlParameter("@postalcode", postalcode);
                par[7] = new SqlParameter("@Description", Description);
                par[8] = new SqlParameter("@Country", Country);
                par[9] = new SqlParameter("@url", url);
                par[10] = new SqlParameter("@Category", Category);
                par[11] = new SqlParameter("@cpc", cpc);
                par[12] = new SqlParameter("@CPA", "0");
                par[13] = new SqlParameter("@dateposted", dateposted);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Joveo_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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

        public static void PostWhatJobs(string company, string advertiser, string title, string referenceNumber, string City, string State, string postalcode, string Description, string Country, string url, string Category, string CPA, string cpc, string dateposted)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);
            cpc = cpc.Replace("USD ", "");
            // dateposted = dateposted.Replace(" UTC", "");

            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[14];
                par[0] = new SqlParameter("@company", company);
                par[1] = new SqlParameter("@advertiser", advertiser);
                par[2] = new SqlParameter("@title", title);
                par[3] = new SqlParameter("@referenceNumber", referenceNumber);
                par[4] = new SqlParameter("@city", City);
                par[5] = new SqlParameter("@State", State);
                par[6] = new SqlParameter("@postalcode", postalcode);
                par[7] = new SqlParameter("@Description", Description);
                par[8] = new SqlParameter("@Country", Country);
                par[9] = new SqlParameter("@url", url);
                par[10] = new SqlParameter("@Category", Category);
                par[11] = new SqlParameter("@cpc", cpc);
                par[12] = new SqlParameter("@CPA", "0");
                par[13] = new SqlParameter("@dateposted", dateposted);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "WhatJobs_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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


        public static void PostEquestJobs(string Title, string State, string City, string PostalCode, string FunctionName, string Industry, string Skills, string Description, string Telecommutepercentage, string TravelPercentage, string Version, string RequisitionNumber, string TinternalID, string InsertionOrder, string JObBatch_id, string Company, string Division, string Department, string CDescription, string InternalID, string PublisherId, string Url, string EMail, string Action, string DateModified, string DateCreated)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);
            State = State.Replace("US- ", "");

            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[26];
                par[0] = new SqlParameter("@Title", Title);
                par[1] = new SqlParameter("@State", State);
                par[2] = new SqlParameter("@City", City);
                par[3] = new SqlParameter("@PostalCode", PostalCode);
                par[4] = new SqlParameter("@FunctionName", FunctionName);
                par[5] = new SqlParameter("@Industry", Industry);
                par[6] = new SqlParameter("@Skills", Skills);
                par[7] = new SqlParameter("@Description", Description);
                par[8] = new SqlParameter("@Telecommutepercentage", Telecommutepercentage);
                par[9] = new SqlParameter("@TravelPercentage", TravelPercentage);
                par[10] = new SqlParameter("@Version", Version);
                par[11] = new SqlParameter("@RequisitionNumber", RequisitionNumber);
                par[12] = new SqlParameter("@TinternalID", TinternalID);
                par[13] = new SqlParameter("@InsertionOrder", InsertionOrder);
                par[14] = new SqlParameter("@JObBatch_id", JObBatch_id);
                par[15] = new SqlParameter("@Company", Company);
                par[16] = new SqlParameter("@Division", Division);
                par[17] = new SqlParameter("@Department", Department);
                par[18] = new SqlParameter("@CDescription", CDescription);
                par[19] = new SqlParameter("@InternalID", InternalID);
                par[20] = new SqlParameter("@PublisherId", PublisherId);
                par[21] = new SqlParameter("@Url", Url);
                par[22] = new SqlParameter("@EMail", EMail);
                par[23] = new SqlParameter("@Action", Action);
                par[24] = new SqlParameter("@DateModified", DateModified);
                par[25] = new SqlParameter("@DateCreated", DateCreated);

                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Equest_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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

        public static void PostClickTraderJobs(string JobTitle, string posted_at, string job_reference, string Zip, string City, string State, string Description, string company, string url, string job_type, string category, string cpc_guarantee, string appcast_category, string cpc, string cpa)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);



            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[15];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@City", City);
                par[2] = new SqlParameter("@State", State);
                par[3] = new SqlParameter("@Zip", Zip);
                par[4] = new SqlParameter("@job_type", job_type);
                par[5] = new SqlParameter("@posted_at", posted_at);
                par[6] = new SqlParameter("@job_reference", job_reference);
                par[7] = new SqlParameter("@Description", Description);
                par[8] = new SqlParameter("@company", company);
                par[9] = new SqlParameter("@url", url);
                par[10] = new SqlParameter("@category", category);
                par[11] = new SqlParameter("@appcast_category", appcast_category);
                par[12] = new SqlParameter("@cpc_guarantee", cpc_guarantee);
                par[13] = new SqlParameter("@cpc", cpc);
                par[14] = new SqlParameter("@cpa", "0.0");
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "ClickTrader_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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


        public static void PostCRBJobs(string JobTitle, string posted_at, string job_reference, string Zip, string City, string State, string Description, string company, string url, string job_type, string category, string cpc_guarantee, string appcast_category, string cpc, string cpa)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);



            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[15];
                par[0] = new SqlParameter("@JobTitle", JobTitle);
                par[1] = new SqlParameter("@City", City);
                par[2] = new SqlParameter("@State", State);
                par[3] = new SqlParameter("@Zip", Zip);
                par[4] = new SqlParameter("@job_type", job_type);
                par[5] = new SqlParameter("@posted_at", posted_at);
                par[6] = new SqlParameter("@job_reference", job_reference);
                par[7] = new SqlParameter("@Description", Description);
                par[8] = new SqlParameter("@company", company);
                par[9] = new SqlParameter("@url", url);
                par[10] = new SqlParameter("@category", category);
                par[11] = new SqlParameter("@appcast_category", appcast_category);
                par[12] = new SqlParameter("@cpc_guarantee", cpc_guarantee);
                par[13] = new SqlParameter("@cpc", cpc);
                par[14] = new SqlParameter("@cpa", "0.0");
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "CRB_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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

        public static void PostjujuJobs(string JobTitle, string job_reference, string location, string Description, string company, string url, string Category, string Postedate, string cpc)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);

            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[9];
                par[0] = new SqlParameter("@jobid", job_reference);
                par[1] = new SqlParameter("@title", JobTitle);
                par[2] = new SqlParameter("@description", Description);
                par[3] = new SqlParameter("@company", company);
                par[4] = new SqlParameter("@location", location);
                par[5] = new SqlParameter("@Created_date", Postedate);
                par[6] = new SqlParameter("@cpc", cpc);
                par[7] = new SqlParameter("@url", url);
                par[8] = new SqlParameter("@Category", Category);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "juju_insertxmlFeed", par);


            }
            catch (Exception ex)
            {

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
        public static void PostFireBrickJobs(string title, string job_reference, string company, string city, string Location, string state, string zip, string Category, string job_type, string url, double cpc, string Description, string Posted_at)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);

            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[13];
                par[0] = new SqlParameter("@title", title);
                par[1] = new SqlParameter("@job_reference", job_reference);
                par[2] = new SqlParameter("@company", company);
                par[3] = new SqlParameter("@city", city);
                par[4] = new SqlParameter("@Location", Location);
                par[5] = new SqlParameter("@state", state);
                par[6] = new SqlParameter("@zip", zip);
                par[7] = new SqlParameter("@Category", Category);
                par[8] = new SqlParameter("@job_type", job_type);
                par[9] = new SqlParameter("@cpc", cpc);
                par[10] = new SqlParameter("@Description", Description);
                par[11] = new SqlParameter("@Posted_at", Posted_at);
                par[12] = new SqlParameter("@url", url);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Firebrick_InsertJobFeed", par);


            }
            catch (Exception ex)
            {

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


        public static void PostJobGetJobs(string Location, string title, string City, string State, string Zip, string country, string Jobtype, string posted_at, string job_reference, string company, string mobile_friendly_apply, string category, string html_jobs, string url, string body, string cpc)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);
            //@Location ,@Title ,@city ,@state ,@zip ,@country,@job_type ,@posted_at ,@job_reference ,@company ,@mobile_friendly_apply varchar(20),@category varchar(150),@html_jobs varchar(20),@url varchar(800),@body varchar(max),@cpc decimal(5,2)

            try
            {
                Sqlconn.Open();
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
                par[10] = new SqlParameter("@mobile_friendly_apply", mobile_friendly_apply);
                par[11] = new SqlParameter("@category", category);
                par[12] = new SqlParameter("@html_jobs", html_jobs);
                par[13] = new SqlParameter("@url", url);
                par[14] = new SqlParameter("@body", body);
                par[15] = new SqlParameter("@cpc", cpc);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "spJobGetInsertJobfeed", par);


            }
            catch (Exception ex)
            {
                throw ex;
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

        public static void PostCollegeRecruiterJobs(string Location, string title, string City, string State, string Zip, string country, string Jobtype, string posted_at, string job_reference, string company, string mobile_friendly_apply, string category, string html_jobs, string url, string body, string cpc)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);
            //@Location ,@Title ,@city ,@state ,@zip ,@country,@job_type ,@posted_at ,@job_reference ,@company ,@mobile_friendly_apply varchar(20),@category varchar(150),@html_jobs varchar(20),@url varchar(800),@body varchar(max),@cpc decimal(5,2)

            try
            {
                Sqlconn.Open();
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
                par[10] = new SqlParameter("@mobile_friendly_apply", mobile_friendly_apply);
                par[11] = new SqlParameter("@category", category);
                par[12] = new SqlParameter("@html_jobs", html_jobs);
                par[13] = new SqlParameter("@url", url);
                par[14] = new SqlParameter("@body", body);
                par[15] = new SqlParameter("@cpc", cpc);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "spCollegeRecruiterInsertJobfeed", par);


            }
            catch (Exception ex)
            {
                throw ex;
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


        public static void PostJoveoJobsCPA(string company, string advertiser, string title, string referenceNumber, string City, string State, string postalcode, string Description, string Country, string url, string Category, string CPA, string cpc, string dateposted)
        {
            SqlConnection Sqlconn = new SqlConnection(ConStr);
            CPA = CPA.Replace("USD ", "");
            dateposted = dateposted.Replace(" UTC", "");

            try
            {
                Sqlconn.Open();
                SqlParameter[] par = new SqlParameter[14];
                par[0] = new SqlParameter("@company", company);
                par[1] = new SqlParameter("@advertiser", advertiser);
                par[2] = new SqlParameter("@title", title);
                par[3] = new SqlParameter("@referenceNumber", referenceNumber);
                par[4] = new SqlParameter("@city", City);
                par[5] = new SqlParameter("@State", State);
                par[6] = new SqlParameter("@postalcode", postalcode);
                par[7] = new SqlParameter("@Description", Description);
                par[8] = new SqlParameter("@Country", Country);
                par[9] = new SqlParameter("@url", url);
                par[10] = new SqlParameter("@Category", Category);
                par[11] = new SqlParameter("@cpc", 0.0);
                par[12] = new SqlParameter("@CPA", CPA);
                par[13] = new SqlParameter("@dateposted", dateposted);
                int i = SqlHelper.ExecuteNonQuery(Sqlconn, "Joveo_InsertJobFeedcpa", par);


            }
            catch (Exception ex)
            {

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

        public static void DeleteLinkupJobs()
        {
            //string Sqlconn = "Data source = 209.59.188.194,16999; Initial Catalog = Feeds; User Id = winger; Pwd = Soft@123";
             string Sqlconn = "Data source=209.59.189.133\\ITJOBCAFESERVER,1435;Initial Catalog=Feeds;User Id=itjobcafe;Pwd=Chand@789!";
     


            try
            {


                int i = SqlHelper.ExecuteNonQuery(Sqlconn, System.Data.CommandType.Text, "Truncate table temp_tbl_Linkupjobs");


            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {

            }
        }


        public static void DeleteZippiaJobs()
        {
            //string Sqlconn = "Data source = 209.59.188.194,16999; Initial Catalog = Feeds; User Id = winger; Pwd = Soft@123";
            string Sqlconn = "Data source=209.59.189.133\\ITJOBCAFESERVER,1435;Initial Catalog=Feeds;User Id=itjobcafe;Pwd=Chand@789!";



            try
            {


                int i = SqlHelper.ExecuteNonQuery(Sqlconn, System.Data.CommandType.Text, "Truncate table temp_tbl_Zippiajobs");


            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {

            }
        }

        public static void DeleteJovioCPAJobs()
        {
            string Sqlconn = "Data source = 209.59.189.133\\ITJOBCAFESERVER,1435;Initial Catalog=Feeds;User Id=itjobcafe;Pwd=Chand@789!";           


            try
            {


                int i = SqlHelper.ExecuteNonQuery(Sqlconn, System.Data.CommandType.Text, "Truncate table temp_tbl_joveojobsCPA");


            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {

            }
        }

        public static void DeleteJovioCPcJobs()
        {
            string Sqlconn = "Data source = 209.59.189.133\\ITJOBCAFESERVER,1435;Initial Catalog=Feeds;User Id=itjobcafe;Pwd=Chand@789!";


            try
            {


                int i = SqlHelper.ExecuteNonQuery(Sqlconn, System.Data.CommandType.Text, "Truncate table temp_tbl_joveojobs");


            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {

            }
        }


    }

    


}

