using System;
using System.Collections.Generic;
using System.Data;
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
            var dbDuplicateJobs = new List<ScrapedJob>();
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
                    dbDuplicateJobs.Add(job);
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

            return new DedupeResult(newJobs, dbDuplicateJobs, dbDuplicates, batchDuplicates);
        }

        public static void UpdateExistingJobs(string connectionString, IEnumerable<ScrapedJob> jobs, string logPrefix)
        {
            var jobList = jobs.ToList();
            if (jobList.Count == 0)
            {
                Console.WriteLine($"{logPrefix} No existing duplicate jobs to update.");
                return;
            }

            using var conn = new SqlConnection(connectionString);
            conn.Open();
            EnsureItClassificationColumns(conn);

            foreach (var job in jobList)
            {
                ItJobFilter.Apply(job);
            }

            CreateDuplicateUpdateTable(conn);
            BulkCopyDuplicateUpdates(conn, jobList);

            var updated = UpdateExistingJobsFromStaging(conn);
            var unmatched = Math.Max(0, jobList.Count - updated);
            Console.WriteLine($"{logPrefix} Database duplicate refresh complete. Staged={jobList.Count}, UpdatedRows={updated}, ApproxUnmatchedDuplicates={unmatched}, Failed=0");
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
            EnsureItClassificationColumns(conn);

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

                    ItJobFilter.Apply(job);
                    InsertJob(conn, job);

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

        public static void BackfillMissingItClassification(string connectionString, string logPrefix)
        {
            var rows = new List<ExistingJobForClassification>();

            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();
                EnsureItClassificationColumns(conn);

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $@"
SELECT job_reference, url, Title, category, description
FROM {TableName}
WHERE IsIT IS NULL OR ITScore IS NULL";
                    cmd.CommandTimeout = 120;

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        rows.Add(new ExistingJobForClassification
                        {
                            Reference = reader.IsDBNull(0) ? "" : reader.GetString(0),
                            Url = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            Title = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            Category = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            Description = reader.IsDBNull(4) ? "" : reader.GetString(4)
                        });
                    }
                }

                if (rows.Count == 0)
                {
                    Console.WriteLine($"{logPrefix} No missing IT classifications to backfill.");
                    return;
                }

                int updated = 0;
                int failed = 0;

                foreach (var row in rows)
                {
                    try
                    {
                        var classification = ItJobFilter.Classify(row.Title, row.Category, row.Description);
                        using var update = conn.CreateCommand();
                        update.CommandText = $@"
UPDATE {TableName}
SET IsIT = @IsIT,
    ITScore = @ITScore
WHERE
    (@normalized_reference <> '' AND LOWER(LTRIM(RTRIM(job_reference))) = @normalized_reference)
    OR (@normalized_url <> '' AND LOWER(LTRIM(RTRIM(url))) = @normalized_url)";

                        update.Parameters.Add("@IsIT", SqlDbType.Bit).Value = classification.IsIT;
                        update.Parameters.Add("@ITScore", SqlDbType.Int).Value = classification.Score;
                        AddNormalized(update, "@normalized_reference", row.Reference, 200);
                        AddNormalized(update, "@normalized_url", row.Url, 800);
                        updated += update.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Console.WriteLine($"{logPrefix} IT backfill failed Reference={row.Reference}: {ex.Message}");
                    }
                }

                Console.WriteLine($"{logPrefix} IT classification backfill complete. CandidateRows={rows.Count}, UpdatedRows={updated}, Failed={failed}");
            }
        }

        private static void InsertJob(SqlConnection conn, ScrapedJob job)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
INSERT INTO {TableName}
(
    job_reference,
    Location,
    Title,
    city,
    state,
    zip,
    country,
    job_type,
    posted_at,
    company,
    Isremote,
    category,
    url,
    description,
    cpc,
    inserteddate,
    IsIT,
    ITScore
)
VALUES
(
    @job_reference,
    @Location,
    @Title,
    @city,
    @state,
    @zip,
    @country,
    @job_type,
    @posted_at,
    @company,
    @Isremote,
    @category,
    @url,
    @description,
    @cpc,
    @inserteddate,
    @IsIT,
    @ITScore
)";

            AddString(cmd, "@job_reference", job.ExternalId, 200);
            AddString(cmd, "@Location", job.Location, 500);
            AddString(cmd, "@Title", job.Title, 500);
            AddString(cmd, "@city", LocationSplitter.CityOf(job.Location), 200);
            AddString(cmd, "@state", LocationSplitter.StateOf(job.Location), 50);
            AddString(cmd, "@zip", "", 20);
            AddString(cmd, "@country", "USA", 50);
            AddString(cmd, "@job_type", job.JobType, 200);
            AddDateTime(cmd, "@posted_at", job.DatePosted);
            AddString(cmd, "@company", job.Company, 200);
            cmd.Parameters.Add("@Isremote", SqlDbType.Int).Value = job.IsRemote ? 1 : 0;
            AddString(cmd, "@category", job.Category, 150);
            AddString(cmd, "@url", job.JobUrl, 800);
            AddString(cmd, "@description", job.Description, -1);
            var cpc = cmd.Parameters.Add("@cpc", SqlDbType.Decimal);
            cpc.Precision = 18;
            cpc.Scale = 2;
            cpc.Value = 0m;
            cmd.Parameters.Add("@inserteddate", SqlDbType.DateTime).Value = DateTime.Now;
            AddItClassification(cmd, job);

            cmd.ExecuteNonQuery();
        }

        private static void EnsureItClassificationColumns(SqlConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
IF COL_LENGTH('{TableName}', 'IsIT') IS NULL
    ALTER TABLE {TableName} ADD IsIT bit NULL;

IF COL_LENGTH('{TableName}', 'ITScore') IS NULL
    ALTER TABLE {TableName} ADD ITScore int NULL;";

            cmd.ExecuteNonQuery();
        }

        private static void CreateDuplicateUpdateTable(SqlConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE #JobDuplicateUpdates
(
    RowId int IDENTITY(1,1) NOT NULL,
    Location varchar(500) NULL,
    Title varchar(500) NULL,
    city varchar(200) NULL,
    state varchar(50) NULL,
    country varchar(50) NULL,
    job_type varchar(200) NULL,
    posted_at datetime NULL,
    company varchar(200) NULL,
    Isremote int NULL,
    category varchar(150) NULL,
    url varchar(800) NULL,
    description varchar(max) NULL,
    IsIT bit NULL,
    ITScore int NULL,
    last_seen datetime NOT NULL,
    normalized_reference varchar(200) NOT NULL,
    normalized_url varchar(800) NOT NULL,
    normalized_title varchar(500) NOT NULL,
    normalized_company varchar(200) NOT NULL,
    normalized_location varchar(500) NOT NULL
);";
            cmd.ExecuteNonQuery();
        }

        private static void BulkCopyDuplicateUpdates(SqlConnection conn, List<ScrapedJob> jobs)
        {
            var table = new DataTable();
            table.Columns.Add("Location", typeof(string));
            table.Columns.Add("Title", typeof(string));
            table.Columns.Add("city", typeof(string));
            table.Columns.Add("state", typeof(string));
            table.Columns.Add("country", typeof(string));
            table.Columns.Add("job_type", typeof(string));
            table.Columns.Add("posted_at", typeof(DateTime));
            table.Columns.Add("company", typeof(string));
            table.Columns.Add("Isremote", typeof(int));
            table.Columns.Add("category", typeof(string));
            table.Columns.Add("url", typeof(string));
            table.Columns.Add("description", typeof(string));
            table.Columns.Add("IsIT", typeof(bool));
            table.Columns.Add("ITScore", typeof(int));
            table.Columns.Add("last_seen", typeof(DateTime));
            table.Columns.Add("normalized_reference", typeof(string));
            table.Columns.Add("normalized_url", typeof(string));
            table.Columns.Add("normalized_title", typeof(string));
            table.Columns.Add("normalized_company", typeof(string));
            table.Columns.Add("normalized_location", typeof(string));

            foreach (var job in jobs)
            {
                var row = table.NewRow();
                row["Location"] = Clean(job.Location, 500);
                row["Title"] = Clean(job.Title, 500);
                row["city"] = Clean(LocationSplitter.CityOf(job.Location), 200);
                row["state"] = Clean(LocationSplitter.StateOf(job.Location), 50);
                row["country"] = "USA";
                row["job_type"] = Clean(job.JobType, 200);
                row["posted_at"] = job.DatePosted.HasValue ? job.DatePosted.Value : DBNull.Value;
                row["company"] = Clean(job.Company, 200);
                row["Isremote"] = job.IsRemote ? 1 : 0;
                row["category"] = Clean(job.Category, 150);
                row["url"] = Clean(job.JobUrl, 800);
                row["description"] = Clean(job.Description, -1);
                row["IsIT"] = job.IsIT;
                row["ITScore"] = job.ITScore;
                row["last_seen"] = DateTime.Now;
                row["normalized_reference"] = Truncate(Normalize(job.ExternalId), 200);
                row["normalized_url"] = Truncate(Normalize(job.JobUrl), 800);
                row["normalized_title"] = Truncate(Normalize(job.Title), 500);
                row["normalized_company"] = Truncate(Normalize(job.Company), 200);
                row["normalized_location"] = Truncate(Normalize(job.Location), 500);
                table.Rows.Add(row);
            }

            using var bulk = new SqlBulkCopy(conn)
            {
                DestinationTableName = "#JobDuplicateUpdates",
                BulkCopyTimeout = 240
            };

            foreach (DataColumn column in table.Columns)
            {
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            }

            bulk.WriteToServer(table);
        }

        private static int UpdateExistingJobsFromStaging(SqlConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 240;
            cmd.CommandText = $@"
;WITH SourceRows AS
(
    SELECT *,
        ROW_NUMBER() OVER
        (
            PARTITION BY
                normalized_reference,
                normalized_url,
                normalized_title,
                normalized_company,
                normalized_location
            ORDER BY RowId DESC
        ) AS rn
    FROM #JobDuplicateUpdates
)
UPDATE target
SET
    Location = COALESCE(source.Location, target.Location),
    Title = COALESCE(source.Title, target.Title),
    city = COALESCE(source.city, target.city),
    state = COALESCE(source.state, target.state),
    country = COALESCE(source.country, target.country),
    job_type = COALESCE(source.job_type, target.job_type),
    posted_at = COALESCE(source.posted_at, target.posted_at),
    company = COALESCE(source.company, target.company),
    Isremote = source.Isremote,
    category = COALESCE(source.category, target.category),
    url = COALESCE(source.url, target.url),
    description = COALESCE(source.description, target.description),
    IsIT = source.IsIT,
    ITScore = source.ITScore,
    inserteddate = source.last_seen
FROM {TableName} target
INNER JOIN SourceRows source
    ON source.rn = 1
    AND
    (
        (source.normalized_reference <> '' AND LOWER(LTRIM(RTRIM(target.job_reference))) = source.normalized_reference)
        OR (source.normalized_url <> '' AND LOWER(LTRIM(RTRIM(target.url))) = source.normalized_url)
        OR (
            source.normalized_title <> ''
            AND source.normalized_company <> ''
            AND LOWER(LTRIM(RTRIM(target.Title))) = source.normalized_title
            AND LOWER(LTRIM(RTRIM(target.company))) = source.normalized_company
            AND ISNULL(LOWER(LTRIM(RTRIM(target.Location))), '') = source.normalized_location
        )
    );";

            return cmd.ExecuteNonQuery();
        }

        private static void AddItClassification(SqlCommand cmd, ScrapedJob job)
        {
            cmd.Parameters.Add("@IsIT", SqlDbType.Bit).Value = job.IsIT;
            cmd.Parameters.Add("@ITScore", SqlDbType.Int).Value = job.ITScore;
        }

        private static void AddString(SqlCommand cmd, string name, string value, int maxLength)
        {
            var parameter = maxLength == -1
                ? cmd.Parameters.Add(name, SqlDbType.VarChar, -1)
                : cmd.Parameters.Add(name, SqlDbType.VarChar, maxLength);

            parameter.Value = Clean(value, maxLength);
        }

        private static void AddDateTime(SqlCommand cmd, string name, DateTime? value)
        {
            cmd.Parameters.Add(name, SqlDbType.DateTime).Value =
                value.HasValue ? value.Value : DBNull.Value;
        }

        private static void AddNormalized(SqlCommand cmd, string name, string value, int maxLength)
        {
            var parameter = cmd.Parameters.Add(name, SqlDbType.VarChar, maxLength);
            parameter.Value = Normalize(value);
        }

        private static void AddIfNotEmpty(HashSet<string> values, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                values.Add(value);
            }
        }

        private static object Clean(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DBNull.Value;
            }

            value = value.Trim();
            if (maxLength > -1 && value.Length > maxLength)
            {
                value = value.Substring(0, maxLength);
            }

            return value;
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

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value ?? "";
            }

            return value.Substring(0, maxLength);
        }

        internal sealed class ExistingKeys
        {
            public HashSet<string> Urls { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> References { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Fingerprints { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        internal sealed class DedupeResult
        {
            public DedupeResult(List<ScrapedJob> newJobs, List<ScrapedJob> dbDuplicateJobs, int dbDuplicates, int batchDuplicates)
            {
                NewJobs = newJobs;
                DbDuplicateJobs = dbDuplicateJobs;
                DbDuplicates = dbDuplicates;
                BatchDuplicates = batchDuplicates;
            }

            public List<ScrapedJob> NewJobs { get; }
            public List<ScrapedJob> DbDuplicateJobs { get; }
            public int DbDuplicates { get; }
            public int BatchDuplicates { get; }
        }

        private sealed class ExistingJobForClassification
        {
            public string Reference { get; set; }
            public string Url { get; set; }
            public string Title { get; set; }
            public string Category { get; set; }
            public string Description { get; set; }
        }
    }
}
