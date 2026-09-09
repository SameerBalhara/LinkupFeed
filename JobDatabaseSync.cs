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
        public const string TargetTableEnvVar = "ITJC_SCRAPPER_TARGET_TABLE";
        private const string DefaultTableName = "dbo.temp_tbl_Scrap_jobs";
        private static string TableName => ResolveTargetTableName();
        private const int DuplicateUpdateBatchSize = 5000;

        public static string CurrentTargetTableName => TableName;

        public static void EnsureTargetTableExists(string connectionString, string logPrefix)
        {
            if (Same(TableName, DefaultTableName))
            {
                return;
            }

            using var conn = new SqlConnection(connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 120;
            cmd.CommandText = $@"
IF OBJECT_ID('{TableName}', 'U') IS NULL
BEGIN
    SELECT TOP (0) *
    INTO {TableName}
    FROM {DefaultTableName};
END";
            cmd.ExecuteNonQuery();

            EnsureItClassificationColumns(conn);
            Console.WriteLine($"{logPrefix} Target table ready: {TableName}");
        }

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
                var hasExternalId = !string.IsNullOrEmpty(externalId);

                bool duplicateInDb =
                    (hasExternalId && existing.References.Contains(externalId)) ||
                    (!hasExternalId && !string.IsNullOrEmpty(url) && existing.Urls.Contains(url)) ||
                    (!string.IsNullOrEmpty(fingerprint) && existing.Fingerprints.Contains(fingerprint));

                if (duplicateInDb)
                {
                    dbDuplicates++;
                    dbDuplicateJobs.Add(job);
                    continue;
                }

                bool duplicateInBatch =
                    (hasExternalId
                        ? !seenRefs.Add(externalId)
                        : (!string.IsNullOrEmpty(url) && !seenUrls.Add(url))) ||
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
                NormalizeCategory(job);
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
                    NormalizeCategory(job);
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

        public static void BackfillLocationParts(string connectionString, string logPrefix)
        {
            var rows = new List<LocationPartUpdate>();

            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $@"
SELECT job_reference, url, Location, city, state
FROM {TableName}
WHERE NULLIF(LTRIM(RTRIM(Location)), '') IS NOT NULL";
                    cmd.CommandTimeout = 120;

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var row = new LocationPartUpdate
                        {
                            Reference = ReadString(reader, 0),
                            Url = ReadString(reader, 1),
                            Location = ReadString(reader, 2),
                            OldCity = ReadString(reader, 3),
                            OldState = ReadString(reader, 4)
                        };

                        LocationSplitter.Split(row.Location, out var city, out var state);
                        row.City = city;
                        row.State = state;

                        if (!Same(row.OldCity, row.City) || !Same(row.OldState, row.State))
                        {
                            rows.Add(row);
                        }
                    }
                }

                var distinctRows = rows
                    .GroupBy(row => $"{Normalize(row.Reference)}|{Normalize(row.Url)}|{Normalize(row.Location)}", StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();

                var updated = 0;
                var failed = 0;

                foreach (var row in distinctRows)
                {
                    try
                    {
                        using var update = conn.CreateCommand();
                        update.CommandText = $@"
UPDATE {TableName}
SET city = @city,
    state = @state
WHERE ISNULL(job_reference, '') = ISNULL(@job_reference, '')
  AND ISNULL(url, '') = ISNULL(@url, '')
  AND ISNULL(Location, '') = ISNULL(@Location, '')";
                        AddString(update, "@city", row.City, 200);
                        AddString(update, "@state", row.State, 50);
                        AddString(update, "@job_reference", row.Reference, 200);
                        AddString(update, "@url", row.Url, 800);
                        AddString(update, "@Location", row.Location, 500);
                        updated += update.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Console.WriteLine($"{logPrefix} Location backfill failed Reference={row.Reference}: {ex.Message}");
                    }
                }

                Console.WriteLine($"{logPrefix} Location backfill complete. CandidateRows={rows.Count}, DistinctUpdates={distinctRows.Count}, UpdatedRows={updated}, Failed={failed}");
            }
        }

        public static void BackfillMultiLocationVariants(string connectionString, string logPrefix)
        {
            var rows = new List<MultiLocationRow>();

            using var conn = new SqlConnection(connectionString);
            conn.Open();
            EnsureItClassificationColumns(conn);

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
SELECT job_reference, Location, Title, city, state, country, job_type, posted_at,
       company, Isremote, category, url, description, IsIT, ITScore
FROM {TableName}
WHERE
    (
        Location LIKE '%;%'
        OR Location LIKE '%US/[A-Z][A-Z]/%'
        OR city LIKE '%;%'
        OR city LIKE '%|%'
        OR LEN(ISNULL(city, '')) > 50
    )
    AND LOWER(LTRIM(RTRIM(job_reference))) NOT LIKE '%:l[0-9a-z][0-9a-z][0-9a-z][0-9a-z]'";
                cmd.CommandTimeout = 120;

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    rows.Add(new MultiLocationRow
                    {
                        Reference = ReadString(reader, 0),
                        Location = ReadString(reader, 1),
                        Title = ReadString(reader, 2),
                        City = ReadString(reader, 3),
                        State = ReadString(reader, 4),
                        Country = ReadString(reader, 5),
                        JobType = ReadString(reader, 6),
                        PostedAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                        Company = ReadString(reader, 8),
                        IsRemote = !reader.IsDBNull(9) && Convert.ToInt32(reader.GetValue(9)) == 1,
                        Category = ReadString(reader, 10),
                        Url = ReadString(reader, 11),
                        Description = ReadString(reader, 12),
                        IsIT = !reader.IsDBNull(13) && reader.GetBoolean(13),
                        ITScore = reader.IsDBNull(14) ? 0 : Convert.ToInt32(reader.GetValue(14))
                    });
                }
            }

            var insertedVariants = 0;
            var existingVariants = 0;
            var deletedOriginals = 0;
            var updatedOriginals = 0;
            var skippedRows = 0;
            var failedRows = 0;
            Console.WriteLine($"{logPrefix} Candidate rows loaded: {rows.Count}");
            var existingReferences = LoadExistingReferences(conn);
            Console.WriteLine($"{logPrefix} Existing references loaded: {existingReferences.Count}");

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
                try
                {
                    var sourceLocation = SourceLocationForExpansion(row);
                    if (string.IsNullOrWhiteSpace(sourceLocation) ||
                        (!sourceLocation.Contains(";") && !sourceLocation.Contains("|")))
                    {
                        if (row.City?.Length > 50)
                        {
                            var canonical = new ScrapedJob
                            {
                                ExternalId = row.Reference,
                                Location = sourceLocation,
                                Title = row.Title,
                                Company = row.Company,
                                JobType = row.JobType,
                                DatePosted = row.PostedAt,
                                IsRemote = row.IsRemote,
                                Category = row.Category,
                                JobUrl = row.Url,
                                Description = row.Description,
                                IsIT = row.IsIT,
                                ITScore = row.ITScore
                            };

                            UpdateOriginalLocation(conn, row, canonical);
                            updatedOriginals++;
                            continue;
                        }

                        skippedRows++;
                        continue;
                    }

                    var sourceJob = new ScrapedJob
                    {
                        ExternalId = row.Reference,
                        Location = sourceLocation,
                        Title = row.Title,
                        Company = row.Company,
                        JobType = row.JobType,
                        DatePosted = row.PostedAt,
                        IsRemote = row.IsRemote,
                        Category = row.Category,
                        JobUrl = row.Url,
                        Description = row.Description,
                        IsIT = row.IsIT,
                        ITScore = row.ITScore
                    };

                    var variants = JobLocationExpander
                        .ExpandForDatabase(new[] { sourceJob }, out _, out _)
                        .ToList();

                    if (variants.Count == 0)
                    {
                        skippedRows++;
                        continue;
                    }

                    if (variants.Count == 1 && !variants[0].ExternalId.Contains(":L", StringComparison.OrdinalIgnoreCase))
                    {
                        UpdateOriginalLocation(conn, row, variants[0]);
                        updatedOriginals++;
                        continue;
                    }

                    var rowInserted = 0;
                    var rowExisting = 0;
                    var rowFailed = false;

                    foreach (var variant in variants)
                    {
                        if (string.IsNullOrWhiteSpace(variant.ExternalId))
                        {
                            rowFailed = true;
                            continue;
                        }

                        var normalizedVariantReference = Normalize(variant.ExternalId);
                        if (existingReferences.Contains(normalizedVariantReference))
                        {
                            rowExisting++;
                            continue;
                        }

                        NormalizeCategory(variant);
                        InsertJob(conn, variant);
                        existingReferences.Add(normalizedVariantReference);
                        rowInserted++;
                    }

                    insertedVariants += rowInserted;
                    existingVariants += rowExisting;

                    if (!rowFailed && rowInserted + rowExisting == variants.Count)
                    {
                        deletedOriginals += DeleteOriginalMultiLocationRow(conn, row);
                    }
                    else
                    {
                        failedRows++;
                    }
                }
                catch (Exception ex)
                {
                    failedRows++;
                    Console.WriteLine($"{logPrefix} Failed Reference={row.Reference} Company={row.Company}: {ex.Message}");
                }

                if ((rowIndex + 1) % 250 == 0)
                {
                    Console.WriteLine(
                        $"{logPrefix} Progress {rowIndex + 1}/{rows.Count}. InsertedVariants={insertedVariants}, DeletedOriginalRows={deletedOriginals}, UpdatedSingleLocationRows={updatedOriginals}, SkippedRows={skippedRows}, FailedRows={failedRows}");
                }
            }

            Console.WriteLine(
                $"{logPrefix} Complete. CandidateRows={rows.Count}, InsertedVariants={insertedVariants}, ExistingVariants={existingVariants}, DeletedOriginalRows={deletedOriginals}, UpdatedSingleLocationRows={updatedOriginals}, SkippedRows={skippedRows}, FailedRows={failedRows}");
        }

        public static void CleanupCityProductionBlockers(string connectionString, string logPrefix)
        {
            var rows = new List<MultiLocationRow>();

            using var conn = new SqlConnection(connectionString);
            conn.Open();
            EnsureItClassificationColumns(conn);

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
SELECT job_reference, Location, Title, city, state, country, job_type, posted_at,
       company, Isremote, category, url, description, IsIT, ITScore
FROM {TableName}
WHERE city LIKE '%|%'
   OR city LIKE '%;%'
   OR LEN(ISNULL(city, '')) > 50
   OR city LIKE 'US Remote%'";
                cmd.CommandTimeout = 120;

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    rows.Add(new MultiLocationRow
                    {
                        Reference = ReadString(reader, 0),
                        Location = ReadString(reader, 1),
                        Title = ReadString(reader, 2),
                        City = ReadString(reader, 3),
                        State = ReadString(reader, 4),
                        Country = ReadString(reader, 5),
                        JobType = ReadString(reader, 6),
                        PostedAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                        Company = ReadString(reader, 8),
                        IsRemote = !reader.IsDBNull(9) && Convert.ToInt32(reader.GetValue(9)) == 1,
                        Category = ReadString(reader, 10),
                        Url = ReadString(reader, 11),
                        Description = ReadString(reader, 12),
                        IsIT = !reader.IsDBNull(13) && reader.GetBoolean(13),
                        ITScore = reader.IsDBNull(14) ? 0 : Convert.ToInt32(reader.GetValue(14))
                    });
                }
            }

            Console.WriteLine($"{logPrefix} Candidate rows loaded: {rows.Count}");
            var existingReferences = LoadExistingReferences(conn);

            var insertedVariants = 0;
            var existingVariants = 0;
            var deletedOriginals = 0;
            var updatedOriginals = 0;
            var nulledRows = 0;
            var failedRows = 0;

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
                try
                {
                    var sourceLocation = SourceLocationForCityCleanup(row);
                    var sourceJob = new ScrapedJob
                    {
                        ExternalId = row.Reference,
                        Location = sourceLocation,
                        Title = row.Title,
                        Company = row.Company,
                        JobType = row.JobType,
                        DatePosted = row.PostedAt,
                        IsRemote = row.IsRemote,
                        Category = row.Category,
                        JobUrl = row.Url,
                        Description = row.Description,
                        IsIT = row.IsIT,
                        ITScore = row.ITScore
                    };

                    var variants = JobLocationExpander
                        .ExpandForDatabase(new[] { sourceJob }, out _, out _)
                        .Where(HasProductionSafeCity)
                        .ToList();

                    if (variants.Count == 0)
                    {
                        NullOriginalCityState(conn, row);
                        nulledRows++;
                    }
                    else if (variants.Count == 1)
                    {
                        UpdateOriginalLocation(conn, row, variants[0]);
                        updatedOriginals++;
                    }
                    else
                    {
                        var rowInserted = 0;
                        var rowExisting = 0;

                        foreach (var variant in variants)
                        {
                            var normalizedVariantReference = Normalize(variant.ExternalId);
                            if (existingReferences.Contains(normalizedVariantReference))
                            {
                                rowExisting++;
                                continue;
                            }

                            NormalizeCategory(variant);
                            InsertJob(conn, variant);
                            existingReferences.Add(normalizedVariantReference);
                            rowInserted++;
                        }

                        insertedVariants += rowInserted;
                        existingVariants += rowExisting;

                        if (rowInserted + rowExisting == variants.Count)
                        {
                            deletedOriginals += DeleteOriginalMultiLocationRow(conn, row);
                        }
                    }
                }
                catch (Exception ex)
                {
                    failedRows++;
                    Console.WriteLine($"{logPrefix} Failed Reference={row.Reference} Company={row.Company}: {ex.Message}");
                }

                if ((rowIndex + 1) % 250 == 0)
                {
                    Console.WriteLine(
                        $"{logPrefix} Progress {rowIndex + 1}/{rows.Count}. InsertedVariants={insertedVariants}, ExistingVariants={existingVariants}, DeletedOriginalRows={deletedOriginals}, UpdatedOriginalRows={updatedOriginals}, NulledRows={nulledRows}, FailedRows={failedRows}");
                }
            }

            Console.WriteLine(
                $"{logPrefix} Complete. CandidateRows={rows.Count}, InsertedVariants={insertedVariants}, ExistingVariants={existingVariants}, DeletedOriginalRows={deletedOriginals}, UpdatedOriginalRows={updatedOriginals}, NulledRows={nulledRows}, FailedRows={failedRows}");
        }

        public static void BackfillNormalizedCategories(string connectionString, string logPrefix)
        {
            var rows = new List<CategoryUpdate>();

            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $@"
SELECT job_reference, url, Title, company, Location, category, description
FROM {TableName}
WHERE NULLIF(LTRIM(RTRIM(category)), '') IS NOT NULL";
                    cmd.CommandTimeout = 180;

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var row = new CategoryUpdate
                        {
                            Reference = ReadString(reader, 0),
                            Url = ReadString(reader, 1),
                            Title = ReadString(reader, 2),
                            Company = ReadString(reader, 3),
                            Location = ReadString(reader, 4),
                            OldCategory = ReadString(reader, 5),
                            Description = ReadString(reader, 6)
                        };

                        row.Category = JobCategoryMapper.Normalize(row.OldCategory, row.Title, row.Description);
                        if (!Same(row.OldCategory, row.Category))
                        {
                            rows.Add(row);
                        }
                    }
                }

                if (rows.Count == 0)
                {
                    Console.WriteLine($"{logPrefix} No categories need normalization.");
                    return;
                }

                CreateCategoryUpdateTable(conn);
                BulkCopyCategoryUpdates(conn, rows);

                var updated = UpdateCategoriesFromStaging(conn);
                var stillOverLimit = CountCategoriesOverLimit(conn);
                Console.WriteLine($"{logPrefix} Category backfill complete. CandidateRows={rows.Count}, UpdatedRows={updated}, RemainingOver30={stillOverLimit}");
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

        private static void NormalizeCategory(ScrapedJob job)
        {
            if (job == null) return;
            job.Category = JobCategoryMapper.Normalize(job.Category, job.Title, job.Description);
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
    category varchar(30) NULL,
    url varchar(800) NULL,
    description varchar(max) NULL,
    IsIT bit NULL,
    ITScore int NULL,
    last_seen datetime NOT NULL,
    normalized_reference varchar(200) NOT NULL,
    normalized_url varchar(800) NOT NULL,
    normalized_title varchar(500) NOT NULL,
    normalized_company varchar(200) NOT NULL,
    normalized_location varchar(500) NOT NULL,
    processed bit NOT NULL DEFAULT 0
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
                row["category"] = Clean(job.Category, 30);
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
            PrepareDuplicateUpdateStaging(conn);

            var updated = 0;
            updated += UpdateExistingJobsFromStagingBatch(
                conn,
                "source.normalized_reference <> ''",
                "target.job_reference = source.normalized_reference");

            updated += UpdateExistingJobsFromStagingBatch(
                conn,
                "source.normalized_reference = '' AND source.normalized_url <> ''",
                "target.url = source.normalized_url");

            updated += UpdateExistingJobsFromStagingBatch(
                conn,
                @"source.normalized_reference = ''
                  AND source.normalized_url = ''
                  AND source.normalized_title <> ''
                  AND source.normalized_company <> ''",
                @"target.Title = source.normalized_title
                  AND target.company = source.normalized_company
                  AND ISNULL(target.Location, '') = source.normalized_location");

            return updated;
        }

        private static void PrepareDuplicateUpdateStaging(SqlConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 240;
            cmd.CommandText = @"
;WITH Ranked AS
(
    SELECT RowId,
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
DELETE FROM Ranked
WHERE rn > 1;

CREATE INDEX IX_JobDuplicateUpdates_Reference
    ON #JobDuplicateUpdates(processed, normalized_reference, RowId);

CREATE INDEX IX_JobDuplicateUpdates_Url
    ON #JobDuplicateUpdates(processed, normalized_url, RowId);

CREATE INDEX IX_JobDuplicateUpdates_Fingerprint
    ON #JobDuplicateUpdates(processed, normalized_title, normalized_company, normalized_location, RowId);

CREATE TABLE #DuplicateUpdateBatch
(
    RowId int NOT NULL PRIMARY KEY
);";

            cmd.ExecuteNonQuery();
        }

        private static int UpdateExistingJobsFromStagingBatch(SqlConnection conn, string sourceFilter, string joinCondition)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 0;
            cmd.Parameters.Add("@BatchSize", SqlDbType.Int).Value = DuplicateUpdateBatchSize;
            cmd.CommandText = $@"
DECLARE @updated int = 0;
DECLARE @rows int = 1;

WHILE @rows > 0
BEGIN
    DELETE FROM #DuplicateUpdateBatch;

    INSERT INTO #DuplicateUpdateBatch(RowId)
    SELECT TOP (@BatchSize) source.RowId
    FROM #JobDuplicateUpdates source
    WHERE source.processed = 0
      AND {sourceFilter}
    ORDER BY source.RowId;

    SET @rows = @@ROWCOUNT;
    IF @rows = 0 BREAK;

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
INNER JOIN #JobDuplicateUpdates source
    ON {joinCondition}
INNER JOIN #DuplicateUpdateBatch batch
    ON batch.RowId = source.RowId;

    SET @updated += @@ROWCOUNT;

    UPDATE source
    SET processed = 1
    FROM #JobDuplicateUpdates source
    INNER JOIN #DuplicateUpdateBatch batch
        ON batch.RowId = source.RowId;
END;

SELECT @updated;";

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        private static void CreateCategoryUpdateTable(SqlConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE #JobCategoryUpdates
(
    RowId int IDENTITY(1,1) NOT NULL,
    category varchar(30) NULL,
    normalized_reference varchar(200) NOT NULL,
    normalized_url varchar(800) NOT NULL,
    normalized_title varchar(500) NOT NULL,
    normalized_company varchar(200) NOT NULL,
    normalized_location varchar(500) NOT NULL,
    normalized_old_category varchar(150) NOT NULL
);";
            cmd.ExecuteNonQuery();
        }

        private static void BulkCopyCategoryUpdates(SqlConnection conn, List<CategoryUpdate> rows)
        {
            var table = new DataTable();
            table.Columns.Add("category", typeof(string));
            table.Columns.Add("normalized_reference", typeof(string));
            table.Columns.Add("normalized_url", typeof(string));
            table.Columns.Add("normalized_title", typeof(string));
            table.Columns.Add("normalized_company", typeof(string));
            table.Columns.Add("normalized_location", typeof(string));
            table.Columns.Add("normalized_old_category", typeof(string));

            foreach (var row in rows)
            {
                var dataRow = table.NewRow();
                dataRow["category"] = Clean(row.Category, 30);
                dataRow["normalized_reference"] = Truncate(Normalize(row.Reference), 200);
                dataRow["normalized_url"] = Truncate(Normalize(row.Url), 800);
                dataRow["normalized_title"] = Truncate(Normalize(row.Title), 500);
                dataRow["normalized_company"] = Truncate(Normalize(row.Company), 200);
                dataRow["normalized_location"] = Truncate(Normalize(row.Location), 500);
                dataRow["normalized_old_category"] = Truncate(Normalize(row.OldCategory), 150);
                table.Rows.Add(dataRow);
            }

            using var bulk = new SqlBulkCopy(conn)
            {
                DestinationTableName = "#JobCategoryUpdates",
                BulkCopyTimeout = 240
            };

            foreach (DataColumn column in table.Columns)
            {
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            }

            bulk.WriteToServer(table);
        }

        private static int UpdateCategoriesFromStaging(SqlConnection conn)
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
                normalized_location,
                normalized_old_category
            ORDER BY RowId DESC
        ) AS rn
    FROM #JobCategoryUpdates
)
UPDATE target
SET category = source.category
FROM {TableName} target
INNER JOIN SourceRows source
    ON source.rn = 1
    AND LOWER(LTRIM(RTRIM(ISNULL(target.category, '')))) = source.normalized_old_category
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

        private static int CountCategoriesOverLimit(SqlConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
SELECT COUNT(*)
FROM {TableName}
WHERE LEN(category) > 30";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        private static void AddItClassification(SqlCommand cmd, ScrapedJob job)
        {
            cmd.Parameters.Add("@IsIT", SqlDbType.Bit).Value = job.IsIT;
            cmd.Parameters.Add("@ITScore", SqlDbType.Int).Value = job.ITScore;
        }

        private static string ReadString(SqlDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);
        }

        private static bool Same(string left, string right)
        {
            return string.Equals((left ?? "").Trim(), (right ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveTargetTableName()
        {
            var configured = Environment.GetEnvironmentVariable(TargetTableEnvVar);
            if (string.IsNullOrWhiteSpace(configured))
            {
                return DefaultTableName;
            }

            var parts = configured.Trim().Split('.');
            if (parts.Length == 1)
            {
                return $"dbo.{ValidateSqlIdentifier(parts[0], TargetTableEnvVar)}";
            }

            if (parts.Length == 2)
            {
                return $"{ValidateSqlIdentifier(parts[0], TargetTableEnvVar)}.{ValidateSqlIdentifier(parts[1], TargetTableEnvVar)}";
            }

            throw new InvalidOperationException($"{TargetTableEnvVar} must be a table name like temp_tbl_Scrap_jobs_compare or dbo.temp_tbl_Scrap_jobs_compare.");
        }

        private static string ValidateSqlIdentifier(string value, string settingName)
        {
            value = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value) ||
                value.Length > 128 ||
                !value.All(ch => char.IsLetterOrDigit(ch) || ch == '_'))
            {
                throw new InvalidOperationException($"{settingName} contains an invalid SQL identifier: {value}");
            }

            return value;
        }

        private static void AddString(SqlCommand cmd, string name, string value, int maxLength)
        {
            var parameter = maxLength == -1
                ? cmd.Parameters.Add(name, SqlDbType.VarChar, -1)
                : cmd.Parameters.Add(name, SqlDbType.VarChar, maxLength);

            parameter.Value = Clean(value, maxLength);
        }

        private static HashSet<string> LoadExistingReferences(SqlConnection conn)
        {
            var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
SELECT job_reference
FROM {TableName}
WHERE NULLIF(LTRIM(RTRIM(job_reference)), '') IS NOT NULL";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                AddIfNotEmpty(references, Normalize(reader.IsDBNull(0) ? null : reader.GetString(0)));
            }

            return references;
        }

        private static string SourceLocationForExpansion(MultiLocationRow row)
        {
            if (!string.IsNullOrWhiteSpace(row.Location) && row.Location.Contains(";"))
            {
                return row.Location;
            }

            if (!string.IsNullOrWhiteSpace(row.City) && row.City.Contains(";"))
            {
                return row.City;
            }

            if (!string.IsNullOrWhiteSpace(row.City) && row.City.Contains("|"))
            {
                return row.City;
            }

            if (!string.IsNullOrWhiteSpace(row.Location) &&
                row.Location.IndexOf("US/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return row.Location;
            }

            return row.Location;
        }

        private static string SourceLocationForCityCleanup(MultiLocationRow row)
        {
            if (!string.IsNullOrWhiteSpace(row.Location) &&
                (row.Location.Contains("|") || row.Location.Contains(";") || row.Location.Length > 50))
            {
                return row.Location;
            }

            return string.IsNullOrWhiteSpace(row.City) ? row.Location : row.City;
        }

        private static bool HasProductionSafeCity(ScrapedJob job)
        {
            var city = LocationSplitter.CityOf(job.Location);
            return string.IsNullOrWhiteSpace(city) ||
                   (city.Length <= 50 && !city.Contains("|") && !city.Contains(";"));
        }

        private static void UpdateOriginalLocation(SqlConnection conn, MultiLocationRow row, ScrapedJob canonical)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
UPDATE {TableName}
SET Location = @Location,
    city = @city,
    state = @state
WHERE LOWER(LTRIM(RTRIM(job_reference))) = @reference
  AND ISNULL(Location, '') = ISNULL(@oldLocation, '')
  AND ISNULL(city, '') = ISNULL(@oldCity, '')";
            AddString(cmd, "@Location", canonical.Location, 500);
            AddString(cmd, "@city", LocationSplitter.CityOf(canonical.Location), 200);
            AddString(cmd, "@state", LocationSplitter.StateOf(canonical.Location), 50);
            AddNormalized(cmd, "@reference", row.Reference, 200);
            AddString(cmd, "@oldLocation", row.Location, 500);
            AddString(cmd, "@oldCity", row.City, 200);
            cmd.ExecuteNonQuery();
        }

        private static void NullOriginalCityState(SqlConnection conn, MultiLocationRow row)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
UPDATE {TableName}
SET city = NULL,
    state = NULL
WHERE LOWER(LTRIM(RTRIM(job_reference))) = @reference
  AND ISNULL(city, '') = ISNULL(@oldCity, '')";
            AddNormalized(cmd, "@reference", row.Reference, 200);
            AddString(cmd, "@oldCity", row.City, 200);
            cmd.ExecuteNonQuery();
        }

        private static int DeleteOriginalMultiLocationRow(SqlConnection conn, MultiLocationRow row)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
DELETE FROM {TableName}
WHERE LOWER(LTRIM(RTRIM(job_reference))) = @reference
  AND ISNULL(Location, '') = ISNULL(@oldLocation, '')
  AND ISNULL(city, '') = ISNULL(@oldCity, '')";
            AddNormalized(cmd, "@reference", row.Reference, 200);
            AddString(cmd, "@oldLocation", row.Location, 500);
            AddString(cmd, "@oldCity", row.City, 200);
            return cmd.ExecuteNonQuery();
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

        private sealed class LocationPartUpdate
        {
            public string Reference { get; set; }
            public string Url { get; set; }
            public string Location { get; set; }
            public string OldCity { get; set; }
            public string OldState { get; set; }
            public string City { get; set; }
            public string State { get; set; }
        }

        private sealed class MultiLocationRow
        {
            public string Reference { get; set; }
            public string Location { get; set; }
            public string Title { get; set; }
            public string City { get; set; }
            public string State { get; set; }
            public string Country { get; set; }
            public string JobType { get; set; }
            public DateTime? PostedAt { get; set; }
            public string Company { get; set; }
            public bool IsRemote { get; set; }
            public string Category { get; set; }
            public string Url { get; set; }
            public string Description { get; set; }
            public bool IsIT { get; set; }
            public int ITScore { get; set; }
        }

        private sealed class CategoryUpdate
        {
            public string Reference { get; set; }
            public string Url { get; set; }
            public string Title { get; set; }
            public string Company { get; set; }
            public string Location { get; set; }
            public string OldCategory { get; set; }
            public string Category { get; set; }
            public string Description { get; set; }
        }
    }
}
