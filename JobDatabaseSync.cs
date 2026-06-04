using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace LinkupFeed
{
    internal static class JobDatabaseSync
    {
        public const string ConnectionStringEnvVar = "ITJC_SCRAPPER_CONNECTION_STRING";
        private const string TableName = "dbo.temp_tbl_Scrap_jobs";

        public static ExistingKeys LoadExistingKeys(string connectionString)
        {
            var keys = new ExistingKeys();

            using var conn = new SqlConnection(connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
SELECT job_reference, url, Title, company, Location
FROM {TableName}
WHERE NULLIF(LTRIM(RTRIM(job_reference)), '') IS NOT NULL
   OR NULLIF(LTRIM(RTRIM(url)), '') IS NOT NULL
   OR NULLIF(LTRIM(RTRIM(Title)), '') IS NOT NULL";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                AddIfNotEmpty(keys.References, Normalize(reader.IsDBNull(0) ? null : reader.GetString(0)));
                AddIfNotEmpty(keys.Urls, Normalize(reader.IsDBNull(1) ? null : reader.GetString(1)));
                AddIfNotEmpty(
                    keys.Fingerprints,
                    Fingerprint(
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4)));
            }

            return keys;
        }

        public static DedupeResult RemoveDuplicates(IEnumerable<ScrapedJob> jobs, ExistingKeys existing)
        {
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenFingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var newJobs = new List<ScrapedJob>();
            int dbDuplicates = 0;
            int batchDuplicates = 0;

            foreach (var job in jobs)
            {
                var url = Normalize(job.JobUrl);
                var externalId = Normalize(job.ExternalId);
                var fingerprint = Fingerprint(job.Title, job.Company, job.Location);

                bool duplicateInDb =
                    (!string.IsNullOrEmpty(url) && existing.Urls.Contains(url)) ||
                    (!string.IsNullOrEmpty(externalId) && existing.References.Contains(externalId)) ||
                    (!string.IsNullOrEmpty(fingerprint) && existing.Fingerprints.Contains(fingerprint));

                if (duplicateInDb)
                {
                    dbDuplicates++;
                    continue;
                }

                bool duplicateInBatch =
                    (!string.IsNullOrEmpty(url) && !seenUrls.Add(url)) ||
                    (!string.IsNullOrEmpty(externalId) && !seenRefs.Add(externalId)) ||
                    (!string.IsNullOrEmpty(fingerprint) && !seenFingerprints.Add(fingerprint));

                if (duplicateInBatch)
                {
                    batchDuplicates++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(job.ExternalId))
                {
                    job.ExternalId = Guid.NewGuid().ToString();
                }

                newJobs.Add(job);
            }

            return new DedupeResult(newJobs, dbDuplicates, batchDuplicates);
        }

        public static void InsertJobs(string connectionString, IEnumerable<ScrapedJob> jobs, string logPrefix)
        {
            var jobList = jobs.ToList();
            if (jobList.Count == 0)
            {
                Console.WriteLine($"{logPrefix} No new jobs to insert.");
                return;
            }

            using var conn = new SqlConnection(connectionString);
            conn.Open();

            int inserted = 0;
            int failed = 0;

            foreach (var job in jobList)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(job.ExternalId))
                    {
                        job.ExternalId = Guid.NewGuid().ToString();
                    }

                    FeedInsert.Post_Jobs(
                        job.Location,
                        job.Title,
                        LocationSplitter.CityOf(job.Location),
                        LocationSplitter.StateOf(job.Location),
                        "",
                        "USA",
                        job.JobType,
                        job.DatePosted?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                        job.ExternalId,
                        job.Company,
                        job.IsRemote ? 1 : 0,
                        job.Category,
                        "",
                        job.JobUrl,
                        job.Description,
                        "",
                        conn);

                    inserted++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine($"{logPrefix} Insert failed ExternalId={job.ExternalId} Company={job.Company}: {ex.Message}");
                }
            }

            Console.WriteLine($"{logPrefix} Database insert complete. Inserted={inserted}, Failed={failed}");
        }

        private static void AddIfNotEmpty(HashSet<string> values, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                values.Add(value);
            }
        }

        private static string Fingerprint(string title, string company, string location)
        {
            var normalizedTitle = Normalize(title);
            var normalizedCompany = Normalize(company);
            var normalizedLocation = Normalize(location);

            if (string.IsNullOrEmpty(normalizedTitle) || string.IsNullOrEmpty(normalizedCompany))
            {
                return "";
            }

            return $"{normalizedTitle}|{normalizedCompany}|{normalizedLocation}";
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? ""
                : value.Trim().ToLowerInvariant();
        }

        internal sealed class ExistingKeys
        {
            public HashSet<string> Urls { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> References { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Fingerprints { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        internal sealed class DedupeResult
        {
            public DedupeResult(List<ScrapedJob> newJobs, int dbDuplicates, int batchDuplicates)
            {
                NewJobs = newJobs;
                DbDuplicates = dbDuplicates;
                BatchDuplicates = batchDuplicates;
            }

            public List<ScrapedJob> NewJobs { get; }
            public int DbDuplicates { get; }
            public int BatchDuplicates { get; }
        }
    }
}
