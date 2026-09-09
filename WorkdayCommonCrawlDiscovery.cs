using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LinkupFeed
{
    internal static class WorkdayCommonCrawlDiscovery
    {
        private static readonly Regex WorkdayDomainPattern = new Regex(
            @"^(?<tenant>[^.]+)\.(?<server>wd\d+)\.myworkdayjobs\.com$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private const int JobPageSize = 20;
        private const int DetailWorkers = 3;
        private static readonly string[] DiscoveryFields =
        {
            "source_row",
            "original_domain",
            "domain",
            "tenant",
            "wd_server",
            "site",
            "api_url",
            "careers_url",
            "status_code",
            "final_url",
            "validated",
            "total_jobs",
            "sample_title",
            "sample_external_path",
            "sample_job_url",
            "sample_still_available",
            "known_from_jobsites",
            "commoncrawl_index",
            "commoncrawl_url",
            "error",
            "candidate_count",
            "attempted_sites",
            "discovery_notes",
            "elapsed_seconds"
        };

        private static readonly string[] DefaultSlicePatterns =
        {
            "*.myworkdayjobs.com/*/job/*",
            "*.myworkdayjobs.com/en-US/*/job/*",
            "*.wd1.myworkdayjobs.com/*/job/*",
            "*.wd3.myworkdayjobs.com/*/job/*",
            "*.wd501.myworkdayjobs.com/*/job/*",
            "*.wd502.myworkdayjobs.com/*/job/*",
            "*.wd503.myworkdayjobs.com/*/job/*"
        };

        private static readonly HttpClient _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
            DefaultRequestHeaders =
            {
                { "Accept", "application/json, text/html;q=0.9, */*;q=0.8" },
                { "Accept-Language", "en-US,en;q=0.9" },
                { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" }
            }
        };

        public static async Task RunAsync(
            string inputCsv,
            string outputCsv,
            string indexId,
            int maxValidatedUrls,
            int cdxLimit,
            bool skipKnown,
            int pages,
            int? pageSize,
            string commonCrawlUrlPattern,
            int cdxDelayMs,
            int validationDelayMs,
            string skipUrlCsv)
        {
            inputCsv = string.IsNullOrWhiteSpace(inputCsv) ? Path.Combine("input", "JobSites.csv") : inputCsv;
            outputCsv = string.IsNullOrWhiteSpace(outputCsv)
                ? Path.Combine("outputs", "workday_commoncrawl", "workday_commoncrawl_valid_latest.csv")
                : outputCsv;
            if (maxValidatedUrls <= 0) maxValidatedUrls = 10;
            if (cdxLimit <= 0) cdxLimit = 500;
            if (pages <= 0) pages = 1;
            var requestedPageSize = pageSize.GetValueOrDefault(cdxLimit);
            if (requestedPageSize <= 0) requestedPageSize = cdxLimit;
            commonCrawlUrlPattern = string.IsNullOrWhiteSpace(commonCrawlUrlPattern)
                ? "*.myworkdayjobs.com/*/job/*"
                : commonCrawlUrlPattern.Trim();
            if (cdxDelayMs < 0) cdxDelayMs = 0;
            if (validationDelayMs < 0) validationDelayMs = 0;

            Directory.CreateDirectory(Path.GetDirectoryName(outputCsv) ?? ".");

            var knownDomains = LoadKnownDomains(inputCsv);
            var index = await ResolveIndexAsync(indexId);
            Console.WriteLine($"[Workday-CC] Common Crawl index: {index.Id}");
            Console.WriteLine($"[Workday-CC] Query limit: {cdxLimit}; pages: {pages}; page size: {requestedPageSize}; validating up to {maxValidatedUrls} Workday API URLs");
            Console.WriteLine($"[Workday-CC] URL pattern: {commonCrawlUrlPattern}");
            Console.WriteLine($"[Workday-CC] CDX delay: {cdxDelayMs}ms; validation delay: {validationDelayMs}ms");
            Console.WriteLine($"[Workday-CC] Skip domains already present in JobSites.csv: {skipKnown}");
            var skippedKeys = LoadDomainSiteKeys(skipUrlCsv);
            if (skippedKeys.Count > 0)
            {
                Console.WriteLine($"[Workday-CC] Skipping {skippedKeys.Count} domain/site pairs from {skipUrlCsv}");
            }

            var cdxUrls = await FetchCdxUrlsAsync(index.CdxApi, commonCrawlUrlPattern, cdxLimit, pages, requestedPageSize, cdxDelayMs);
            var candidates = cdxUrls
                .Select(ParseCandidate)
                .Where(c => c != null)
                .GroupBy(c => $"{c.Domain}|{c.Site}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            var allCandidateCount = candidates.Count;
            var knownCandidateCount = candidates.Count(c => knownDomains.Contains(c.Domain));
            if (skipKnown)
            {
                candidates = candidates
                    .Where(c => !knownDomains.Contains(c.Domain))
                    .ToList();
            }
            if (skippedKeys.Count > 0)
            {
                candidates = candidates
                    .Where(c => !skippedKeys.Contains($"{c.Domain}|{c.Site}"))
                    .ToList();
            }

            Console.WriteLine($"[Workday-CC] CDX URLs returned: {cdxUrls.Count}");
            Console.WriteLine($"[Workday-CC] Parsed distinct Workday tenant/site candidates: {allCandidateCount}");
            Console.WriteLine($"[Workday-CC] Candidates already present in JobSites.csv: {knownCandidateCount}");
            Console.WriteLine($"[Workday-CC] Candidates selected for validation: {candidates.Count}");

            var rows = new List<Dictionary<string, string>>();
            foreach (var candidate in candidates)
            {
                if (rows.Count >= maxValidatedUrls) break;

                var row = await ValidateCandidateAsync(candidate, index.Id, knownDomains.Contains(candidate.Domain));
                if (row != null)
                {
                    rows.Add(row);
                    Console.WriteLine(
                        $"[Workday-CC] valid {rows.Count}/{maxValidatedUrls}: {candidate.Domain}/{candidate.Site} -> {row["total_jobs"]} jobs; live={row["sample_still_available"]}; sample={row["sample_title"]}");
                }

                if (validationDelayMs > 0)
                {
                    await Task.Delay(validationDelayMs);
                }
            }

            WriteCsv(outputCsv, rows, DiscoveryFields);

            var totalJobs = rows.Sum(r => int.TryParse(r.GetValueOrDefault("total_jobs"), out var count) ? count : 0);
            var liveSamples = rows.Count(r => string.Equals(r.GetValueOrDefault("sample_still_available"), "True", StringComparison.OrdinalIgnoreCase));
            var known = rows.Count(r => string.Equals(r.GetValueOrDefault("known_from_jobsites"), "True", StringComparison.OrdinalIgnoreCase));

            Console.WriteLine($"[Workday-CC] Output: {Path.GetFullPath(outputCsv)}");
            Console.WriteLine($"[Workday-CC] Validated URLs: {rows.Count}");
            Console.WriteLine($"[Workday-CC] Total jobs across validated URLs: {totalJobs}");
            Console.WriteLine($"[Workday-CC] Sample jobs still available: {liveSamples}/{rows.Count}");
            Console.WriteLine($"[Workday-CC] Domains already present in JobSites.csv: {known}/{rows.Count}");
        }

        public static async Task RunSlicesAsync(
            string inputCsv,
            string outputCsv,
            string indexIds,
            string patternsArg,
            int maxValidatedUrls,
            int cdxLimitPerSlice,
            bool skipKnown,
            int pagesPerSlice,
            int? pageSize,
            int cdxDelayMs,
            int validationDelayMs,
            int sliceDelayMs,
            string skipUrlCsv)
        {
            inputCsv = string.IsNullOrWhiteSpace(inputCsv) ? Path.Combine("input", "JobSites.csv") : inputCsv;
            outputCsv = string.IsNullOrWhiteSpace(outputCsv)
                ? Path.Combine("outputs", "workday_commoncrawl", "workday_commoncrawl_valid_sliced_latest.csv")
                : outputCsv;
            if (maxValidatedUrls <= 0) maxValidatedUrls = 100;
            if (cdxLimitPerSlice <= 0) cdxLimitPerSlice = 1000;
            if (pagesPerSlice <= 0) pagesPerSlice = 1;
            var requestedPageSize = pageSize.GetValueOrDefault(cdxLimitPerSlice);
            if (requestedPageSize <= 0) requestedPageSize = cdxLimitPerSlice;
            if (cdxDelayMs < 0) cdxDelayMs = 0;
            if (validationDelayMs < 0) validationDelayMs = 0;
            if (sliceDelayMs < 0) sliceDelayMs = 0;

            Directory.CreateDirectory(Path.GetDirectoryName(outputCsv) ?? ".");

            var knownDomains = LoadKnownDomains(inputCsv);
            var indexes = new List<CommonCrawlIndex>();
            var indexList = SplitList(indexIds);
            if (indexList.Count == 0)
            {
                indexes.Add(await ResolveIndexAsync(null));
            }
            else
            {
                foreach (var indexId in indexList)
                {
                    indexes.Add(await ResolveIndexAsync(indexId));
                }
            }

            var patterns = SplitList(patternsArg);
            if (patterns.Count == 0) patterns.AddRange(DefaultSlicePatterns);

            Console.WriteLine($"[Workday-CC-Slices] Indexes: {string.Join(", ", indexes.Select(i => i.Id))}");
            Console.WriteLine($"[Workday-CC-Slices] Patterns: {patterns.Count}");
            Console.WriteLine($"[Workday-CC-Slices] Per-slice CDX limit: {cdxLimitPerSlice}; pages: {pagesPerSlice}; page size: {requestedPageSize}");
            Console.WriteLine($"[Workday-CC-Slices] Target validated URLs: {maxValidatedUrls}; skip known: {skipKnown}");
            Console.WriteLine($"[Workday-CC-Slices] Delays: CDX={cdxDelayMs}ms; validation={validationDelayMs}ms; slice={sliceDelayMs}ms");

            var rows = new List<Dictionary<string, string>>();
            var seen = LoadDomainSiteKeys(skipUrlCsv);
            if (seen.Count > 0)
            {
                Console.WriteLine($"[Workday-CC-Slices] Skipping {seen.Count} domain/site pairs from {skipUrlCsv}");
            }
            var sliceNumber = 0;

            foreach (var index in indexes)
            {
                foreach (var pattern in patterns)
                {
                    if (rows.Count >= maxValidatedUrls) break;
                    sliceNumber++;

                    var remaining = maxValidatedUrls - rows.Count;
                    Console.WriteLine($"[Workday-CC-Slices] Slice {sliceNumber}: index={index.Id}; pattern={pattern}; remaining target={remaining}");
                    var sliceRows = await DiscoverRowsAsync(
                        knownDomains,
                        seen,
                        index,
                        remaining,
                        cdxLimitPerSlice,
                        skipKnown,
                        pagesPerSlice,
                        requestedPageSize,
                        pattern,
                        cdxDelayMs,
                        validationDelayMs,
                        $"slice={sliceNumber}");

                    var added = 0;
                    foreach (var row in sliceRows)
                    {
                        var key = $"{row.GetValueOrDefault("domain")}|{row.GetValueOrDefault("site")}";
                        if (seen.Add(key))
                        {
                            rows.Add(row);
                            added++;
                        }
                    }

                    Console.WriteLine($"[Workday-CC-Slices] Slice {sliceNumber} added {added} new URLs; combined={rows.Count}");

                    if (sliceDelayMs > 0 && rows.Count < maxValidatedUrls)
                    {
                        await Task.Delay(sliceDelayMs);
                    }
                }
            }

            WriteCsv(outputCsv, rows, DiscoveryFields);
            PrintDiscoverySummary("[Workday-CC-Slices]", outputCsv, rows);
        }

        private static async Task<List<Dictionary<string, string>>> DiscoverRowsAsync(
            HashSet<string> knownDomains,
            HashSet<string> alreadySeen,
            CommonCrawlIndex index,
            int maxValidatedUrls,
            int cdxLimit,
            bool skipKnown,
            int pages,
            int pageSize,
            string commonCrawlUrlPattern,
            int cdxDelayMs,
            int validationDelayMs,
            string notesPrefix)
        {
            var cdxUrls = await FetchCdxUrlsAsync(index.CdxApi, commonCrawlUrlPattern, cdxLimit, pages, pageSize, cdxDelayMs);
            var candidates = cdxUrls
                .Select(ParseCandidate)
                .Where(c => c != null)
                .GroupBy(c => $"{c.Domain}|{c.Site}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            var allCandidateCount = candidates.Count;
            var knownCandidateCount = candidates.Count(c => knownDomains.Contains(c.Domain));
            if (skipKnown)
            {
                candidates = candidates
                    .Where(c => !knownDomains.Contains(c.Domain))
                    .ToList();
            }
            if (alreadySeen != null && alreadySeen.Count > 0)
            {
                candidates = candidates
                    .Where(c => !alreadySeen.Contains($"{c.Domain}|{c.Site}"))
                    .ToList();
            }

            Console.WriteLine($"[Workday-CC-Slice] CDX URLs returned: {cdxUrls.Count}");
            Console.WriteLine($"[Workday-CC-Slice] Parsed candidates: {allCandidateCount}; known={knownCandidateCount}; selected={candidates.Count}");

            var rows = new List<Dictionary<string, string>>();
            foreach (var candidate in candidates)
            {
                if (rows.Count >= maxValidatedUrls) break;

                var row = await ValidateCandidateAsync(candidate, index.Id, knownDomains.Contains(candidate.Domain));
                if (row != null)
                {
                    row["discovery_notes"] =
                        $"{notesPrefix}; pattern={commonCrawlUrlPattern}; Common Crawl Workday job URL discovery";
                    rows.Add(row);
                    Console.WriteLine(
                        $"[Workday-CC-Slice] valid {rows.Count}/{maxValidatedUrls}: {candidate.Domain}/{candidate.Site} -> {row["total_jobs"]} jobs");
                }

                if (validationDelayMs > 0)
                {
                    await Task.Delay(validationDelayMs);
                }
            }

            return rows;
        }

        private static List<string> SplitList(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new List<string>();
            return value
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void PrintDiscoverySummary(string prefix, string outputCsv, List<Dictionary<string, string>> rows)
        {
            var totalJobs = rows.Sum(r => int.TryParse(r.GetValueOrDefault("total_jobs"), out var count) ? count : 0);
            var liveSamples = rows.Count(r => string.Equals(r.GetValueOrDefault("sample_still_available"), "True", StringComparison.OrdinalIgnoreCase));
            var known = rows.Count(r => string.Equals(r.GetValueOrDefault("known_from_jobsites"), "True", StringComparison.OrdinalIgnoreCase));

            Console.WriteLine($"{prefix} Output: {Path.GetFullPath(outputCsv)}");
            Console.WriteLine($"{prefix} Validated URLs: {rows.Count}");
            Console.WriteLine($"{prefix} Total jobs across validated URLs: {totalJobs}");
            Console.WriteLine($"{prefix} Sample jobs still available: {liveSamples}/{rows.Count}");
            Console.WriteLine($"{prefix} Domains already present in JobSites.csv: {known}/{rows.Count}");
        }

        public static async Task ExportJobsAsync(
            string inputCsv,
            string outputCsv,
            int? maxPages,
            int? maxJobsPerSite)
        {
            inputCsv = string.IsNullOrWhiteSpace(inputCsv)
                ? Path.Combine("outputs", "workday_commoncrawl", "workday_commoncrawl_valid_latest.csv")
                : inputCsv;
            outputCsv = string.IsNullOrWhiteSpace(outputCsv)
                ? Path.Combine("outputs", "workday_commoncrawl", "workday_commoncrawl_jobs_latest.csv")
                : outputCsv;

            Directory.CreateDirectory(Path.GetDirectoryName(outputCsv) ?? ".");

            var sites = AtsCsv.ReadRows(inputCsv)
                .Where(r => string.Equals(AtsCsv.Get(r, "validated"), "true", StringComparison.OrdinalIgnoreCase))
                .Where(r => !string.IsNullOrWhiteSpace(AtsCsv.Get(r, "api_url")))
                .ToList();

            Console.WriteLine($"[Workday-CC-Jobs] Loaded {sites.Count} validated Workday URL rows from {inputCsv}");

            var rows = new List<Dictionary<string, string>>();
            foreach (var site in sites)
            {
                rows.AddRange(await FetchAllJobsForSiteAsync(site, maxPages.GetValueOrDefault(0), maxJobsPerSite.GetValueOrDefault(0)));
            }

            var fields = new[]
            {
                "source",
                "source_id",
                "domain",
                "tenant",
                "wd_server",
                "site",
                "api_url",
                "company",
                "title",
                "location",
                "is_us",
                "is_remote",
                "job_type",
                "category",
                "date_posted",
                "job_reference",
                "job_url",
                "detail_api_url",
                "detail_status_code",
                "still_available",
                "availability_status",
                "is_it",
                "it_score",
                "description_length",
                "description",
                "external_path",
                "commoncrawl_index",
                "commoncrawl_url",
                "error"
            };

            WriteCsv(outputCsv, rows, fields);

            var total = rows.Count;
            var live = rows.Count(r => string.Equals(r.GetValueOrDefault("still_available"), "True", StringComparison.OrdinalIgnoreCase));
            var described = rows.Count(r => int.TryParse(r.GetValueOrDefault("description_length"), out var length) && length > 0);
            var us = rows.Count(r => string.Equals(r.GetValueOrDefault("is_us"), "True", StringComparison.OrdinalIgnoreCase));
            var remote = rows.Count(r => string.Equals(r.GetValueOrDefault("is_remote"), "True", StringComparison.OrdinalIgnoreCase));
            var it = rows.Count(r => string.Equals(r.GetValueOrDefault("is_it"), "True", StringComparison.OrdinalIgnoreCase));

            Console.WriteLine($"[Workday-CC-Jobs] Output: {Path.GetFullPath(outputCsv)}");
            Console.WriteLine($"[Workday-CC-Jobs] Exported postings: {total}");
            Console.WriteLine($"[Workday-CC-Jobs] Still available by detail API: {live}/{total}");
            Console.WriteLine($"[Workday-CC-Jobs] With descriptions: {described}/{total}");
            Console.WriteLine($"[Workday-CC-Jobs] US={us}; Remote={remote}; IT={it}");
        }

        private static async Task<List<Dictionary<string, string>>> FetchAllJobsForSiteAsync(
            Dictionary<string, string> site,
            int maxPages,
            int maxJobsPerSite)
        {
            var domain = AtsCsv.Get(site, "domain");
            var apiUrl = AtsCsv.Get(site, "api_url");
            var careersUrl = AtsCsv.Get(site, "careers_url");
            if (string.IsNullOrWhiteSpace(careersUrl))
            {
                careersUrl = $"https://{domain}/en-US/{AtsCsv.Get(site, "site")}";
            }

            var summaries = new List<JsonElement>();
            var offset = 0;
            var total = int.MaxValue;
            var pages = 0;

            try
            {
                while (offset < total)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                    request.Headers.Referrer = new Uri(careersUrl);
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(new { appliedFacets = new { }, limit = JobPageSize, offset, searchText = "" }),
                        Encoding.UTF8,
                        "application/json");

                    using var response = await _http.SendAsync(request);
                    var text = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[Workday-CC-Jobs] {domain} HTTP {(int)response.StatusCode}");
                        break;
                    }

                    using var doc = JsonDocument.Parse(text);
                    var root = doc.RootElement;
                    total = GetInt(root, "total") ?? 0;

                    if (!root.TryGetProperty("jobPostings", out var postings) ||
                        postings.ValueKind != JsonValueKind.Array ||
                        postings.GetArrayLength() == 0)
                    {
                        break;
                    }

                    foreach (var posting in postings.EnumerateArray())
                    {
                        summaries.Add(posting.Clone());
                        if (maxJobsPerSite > 0 && summaries.Count >= maxJobsPerSite) break;
                    }

                    pages++;
                    offset += JobPageSize;
                    if ((maxPages > 0 && pages >= maxPages) ||
                        (maxJobsPerSite > 0 && summaries.Count >= maxJobsPerSite))
                    {
                        break;
                    }

                    await Task.Delay(50);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Workday-CC-Jobs] {domain} list error: {ex.Message}");
            }

            var rows = new List<Dictionary<string, string>>();
            var gate = new SemaphoreSlim(DetailWorkers);
            var tasks = summaries.Select(async posting =>
            {
                await gate.WaitAsync();
                try
                {
                    return await MapPostingWithDetailAsync(site, posting);
                }
                finally
                {
                    gate.Release();
                }
            }).ToList();

            var pending = tasks.ToList();
            var completed = 0;
            while (pending.Count > 0)
            {
                var finished = await Task.WhenAny(pending);
                pending.Remove(finished);
                rows.Add(await finished);
                completed++;

                if (completed % 100 == 0 || completed == summaries.Count)
                {
                    Console.WriteLine($"[Workday-CC-Jobs] {domain}/{AtsCsv.Get(site, "site")} detail progress: {completed}/{summaries.Count}");
                }
            }

            var live = rows.Count(r => string.Equals(r.GetValueOrDefault("still_available"), "True", StringComparison.OrdinalIgnoreCase));
            Console.WriteLine($"[Workday-CC-Jobs] {domain}/{AtsCsv.Get(site, "site")} -> {rows.Count} postings; live={live}");
            return rows;
        }

        private static async Task<Dictionary<string, string>> MapPostingWithDetailAsync(Dictionary<string, string> site, JsonElement posting)
        {
            var domain = AtsCsv.Get(site, "domain");
            var externalPath = GetText(posting, "externalPath") ?? "";
            var title = GetText(posting, "title") ?? "";
            var location = GetText(posting, "locationsText") ?? GetText(posting, "location") ?? "";
            var category = CategoryFromJob(posting);
            var jobType = GetText(posting, "timeType") ?? GetText(posting, "employmentType") ?? "";
            var datePosted = ParseDate(FirstNonEmpty(GetText(posting, "startDate"), GetText(posting, "postedOn")));
            var remote = IsRemote(location, posting);
            var jobUrl = JobUrl(site, externalPath);
            var detailUrl = DetailApiUrl(site, externalPath);
            var reference = WorkdayReferenceId(site, posting, externalPath, category);
            var description = "";
            var statusCode = "";
            var stillAvailable = false;
            var availabilityStatus = "unknown";
            var error = "";

            try
            {
                var detailResponse = await FetchDetailTextAsync(detailUrl, jobUrl);
                statusCode = detailResponse.StatusCode;
                var text = detailResponse.Body;
                error = detailResponse.Error;

                if (detailResponse.IsSuccess)
                {
                    using var doc = JsonDocument.Parse(text);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("jobPostingInfo", out var info))
                    {
                        stillAvailable = !string.IsNullOrWhiteSpace(GetText(info, "title"));
                        availabilityStatus = stillAvailable ? "live" : "missing_title";
                        description = CleanDescription(GetText(info, "jobDescription"));
                        jobType = FirstNonEmpty(
                            jobType,
                            GetTextOrDescriptor(info, "timeType"),
                            GetTextOrDescriptor(info, "employmentType"),
                            GetTextOrDescriptor(info, "workerSubType"));
                        reference = PreferredReference(reference, FirstNonEmpty(GetText(info, "jobReqId"), GetText(info, "id")));
                        datePosted = datePosted ?? ParseDate(FirstNonEmpty(GetText(info, "startDate"), GetText(info, "postedOn")));
                    }

                    if (string.IsNullOrWhiteSpace(description))
                    {
                        description = CleanDescription(GetText(root, "jobDescription"));
                    }

                    jobType = FirstNonEmpty(
                        jobType,
                        GetTextOrDescriptor(root, "timeType"),
                        GetTextOrDescriptor(root, "employmentType"),
                        GetTextOrDescriptor(root, "workerSubType"));
                    reference = PreferredReference(reference, FirstNonEmpty(GetText(root, "jobReqId"), GetText(root, "id")));
                    datePosted = datePosted ?? ParseDate(FirstNonEmpty(GetText(root, "startDate"), GetText(root, "postedOn")));
                }
                else
                {
                    availabilityStatus = statusCode == "429"
                        ? "throttled"
                        : statusCode == "404"
                            ? "not_found"
                            : string.IsNullOrWhiteSpace(statusCode)
                                ? "error"
                                : $"http_{statusCode}";
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                availabilityStatus = "error";
            }

            var classification = ItJobFilter.Classify(title, category, description);
            return new Dictionary<string, string>
            {
                ["source"] = "workday_commoncrawl",
                ["source_id"] = "99",
                ["domain"] = domain,
                ["tenant"] = AtsCsv.Get(site, "tenant"),
                ["wd_server"] = AtsCsv.Get(site, "wd_server"),
                ["site"] = AtsCsv.Get(site, "site"),
                ["api_url"] = AtsCsv.Get(site, "api_url"),
                ["company"] = CompanyName(site),
                ["title"] = title,
                ["location"] = location,
                ["is_us"] = UsLocationFilter.IsUs(location).ToString(),
                ["is_remote"] = remote.ToString(),
                ["job_type"] = jobType,
                ["category"] = category,
                ["date_posted"] = datePosted?.ToString("yyyy-MM-dd") ?? "",
                ["job_reference"] = reference,
                ["job_url"] = jobUrl,
                ["detail_api_url"] = detailUrl,
                ["detail_status_code"] = statusCode,
                ["still_available"] = stillAvailable.ToString(),
                ["availability_status"] = availabilityStatus,
                ["is_it"] = classification.IsIT.ToString(),
                ["it_score"] = classification.Score.ToString(),
                ["description_length"] = string.IsNullOrWhiteSpace(description) ? "0" : description.Length.ToString(),
                ["description"] = description,
                ["external_path"] = externalPath,
                ["commoncrawl_index"] = AtsCsv.Get(site, "commoncrawl_index"),
                ["commoncrawl_url"] = AtsCsv.Get(site, "commoncrawl_url"),
                ["error"] = error
            };
        }

        private static async Task<DetailHttpResult> FetchDetailTextAsync(string detailUrl, string jobUrl)
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, detailUrl);
                    if (!string.IsNullOrWhiteSpace(jobUrl)) request.Headers.Referrer = new Uri(jobUrl);

                    using var response = await _http.SendAsync(request);
                    var body = await response.Content.ReadAsStringAsync();
                    var statusCode = ((int)response.StatusCode).ToString();
                    if (response.IsSuccessStatusCode)
                    {
                        return new DetailHttpResult(statusCode, true, body);
                    }

                    var retryable = (int)response.StatusCode == 429 || (int)response.StatusCode >= 500;
                    if (!retryable)
                    {
                        return new DetailHttpResult(statusCode, false, body);
                    }
                }
                catch (Exception ex)
                {
                    if (attempt == 2)
                    {
                        return new DetailHttpResult("", false, "", ex.Message);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
            }

            return new DetailHttpResult("", false, "", "Detail endpoint throttled or failed after retries.");
        }

        private static HashSet<string> LoadKnownDomains(string inputCsv)
        {
            var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(inputCsv)) return domains;

            foreach (var row in AtsCsv.ReadRows(inputCsv))
            {
                foreach (var value in row.Values)
                {
                    var domain = NormalizeDomain(value);
                    if (!string.IsNullOrWhiteSpace(domain)) domains.Add(domain);
                }
            }

            return domains;
        }

        private static HashSet<string> LoadDomainSiteKeys(string csvPath)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath)) return keys;

            foreach (var row in AtsCsv.ReadRows(csvPath))
            {
                var domain = AtsCsv.Get(row, "domain");
                var site = AtsCsv.Get(row, "site");
                if (!string.IsNullOrWhiteSpace(domain) && !string.IsNullOrWhiteSpace(site))
                {
                    keys.Add($"{domain.Trim()}|{site.Trim()}");
                }
            }

            return keys;
        }

        private static async Task<CommonCrawlIndex> ResolveIndexAsync(string indexId)
        {
            if (!string.IsNullOrWhiteSpace(indexId))
            {
                return new CommonCrawlIndex
                {
                    Id = indexId.Trim(),
                    CdxApi = $"https://index.commoncrawl.org/{indexId.Trim()}-index"
                };
            }

            var json = await FetchTextWithRetryAsync("https://index.commoncrawl.org/collinfo.json", "collinfo.json");
            using var doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var id = GetText(item, "id");
                var api = GetText(item, "cdx-api");
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(api))
                {
                    return new CommonCrawlIndex { Id = id, CdxApi = api };
                }
            }

            throw new InvalidOperationException("Unable to resolve a Common Crawl index from collinfo.json.");
        }

        private static async Task<List<string>> FetchCdxUrlsAsync(
            string cdxApi,
            string urlPattern,
            int totalLimit,
            int pages,
            int pageSize,
            int cdxDelayMs)
        {
            var urls = new List<string>();

            for (var page = 1; page <= pages && urls.Count < totalLimit; page++)
            {
                var limit = Math.Min(pageSize, totalLimit - urls.Count);
                var crawlPage = page - 1;
                var query = $"{cdxApi}?url={Uri.EscapeDataString(urlPattern)}&output=json&fl=url&filter=status:200&limit={limit}&page={crawlPage}";
                var text = await FetchCdxPageTextAsync(query, crawlPage);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var pageUrls = 0;

                foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var url = GetText(doc.RootElement, "url");
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            urls.Add(url);
                            pageUrls++;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                Console.WriteLine($"[Workday-CC] CDX page {crawlPage}: returned {pageUrls} URLs");
                if (pageUrls == 0) break;

                if (cdxDelayMs > 0 && page < pages)
                {
                    await Task.Delay(cdxDelayMs);
                }
            }

            return urls;
        }

        private static async Task<string> FetchCdxPageTextAsync(string query, int crawlPage)
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var response = await _http.GetAsync(query);
                    var text = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        return text;
                    }

                    var shouldRetry =
                        (int)response.StatusCode == 429 ||
                        (int)response.StatusCode >= 500;
                    if (!shouldRetry)
                    {
                        Console.WriteLine($"[Workday-CC] CDX page {crawlPage} failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                        return "";
                    }

                    Console.WriteLine($"[Workday-CC] CDX page {crawlPage} throttled; retry {attempt}/3");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Workday-CC] CDX page {crawlPage} request error on retry {attempt}/3: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(attempt * 3));
            }

            Console.WriteLine($"[Workday-CC] CDX page {crawlPage} skipped after repeated throttling/errors.");
            return "";
        }

        private static async Task<string> FetchTextWithRetryAsync(string url, string label)
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var response = await _http.GetAsync(url);
                    var text = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode) return text;

                    if ((int)response.StatusCode < 500 && (int)response.StatusCode != 429)
                    {
                        throw new HttpRequestException($"{label} failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                    }

                    Console.WriteLine($"[Workday-CC] {label} throttled/unavailable; retry {attempt}/3");
                }
                catch (Exception ex)
                {
                    if (attempt == 3) throw;
                    Console.WriteLine($"[Workday-CC] {label} request error on retry {attempt}/3: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(attempt * 3));
            }

            throw new HttpRequestException($"{label} failed after retries.");
        }

        private static WorkdayCandidate ParseCandidate(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

            var domain = uri.Host.ToLowerInvariant();
            var match = WorkdayDomainPattern.Match(domain);
            if (!match.Success) return null;

            var segments = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(WebUtility.UrlDecode)
                .ToList();

            var jobIndex = segments.FindIndex(s => string.Equals(s, "job", StringComparison.OrdinalIgnoreCase));
            if (jobIndex < 2 || jobIndex + 1 >= segments.Count) return null;

            var site = segments[jobIndex - 1];
            if (string.IsNullOrWhiteSpace(site)) return null;

            return new WorkdayCandidate
            {
                Domain = domain,
                Tenant = match.Groups["tenant"].Value,
                Server = match.Groups["server"].Value,
                Site = site,
                CommonCrawlUrl = url
            };
        }

        private static async Task<Dictionary<string, string>> ValidateCandidateAsync(WorkdayCandidate candidate, string indexId, bool knownFromJobSites)
        {
            var started = DateTime.UtcNow;
            var apiUrl = $"https://{candidate.Domain}/wday/cxs/{candidate.Tenant}/{candidate.Site}/jobs";
            var careersUrl = $"https://{candidate.Domain}/en-US/{candidate.Site}";

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                request.Headers.Referrer = new Uri(careersUrl);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(new { appliedFacets = new { }, limit = 1, offset = 0, searchText = "" }),
                    Encoding.UTF8,
                    "application/json");

                using var response = await _http.SendAsync(request);
                var text = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return null;

                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                var total = GetText(root, "total");
                var totalJobs = int.TryParse(total, out var parsedTotal) ? parsedTotal : 0;
                var sampleTitle = "";
                var sampleExternalPath = "";
                var sampleStillAvailable = false;

                if (root.TryGetProperty("jobPostings", out var postings) &&
                    postings.ValueKind == JsonValueKind.Array &&
                    postings.GetArrayLength() > 0)
                {
                    var first = postings[0];
                    sampleTitle = GetText(first, "title") ?? "";
                    sampleExternalPath = GetText(first, "externalPath") ?? "";
                    sampleStillAvailable = await IsSampleStillAvailableAsync(candidate, sampleExternalPath);
                }

                if (totalJobs <= 0 || string.IsNullOrWhiteSpace(sampleTitle) || !sampleStillAvailable)
                {
                    return null;
                }

                return new Dictionary<string, string>
                {
                    ["source_row"] = "commoncrawl",
                    ["original_domain"] = candidate.Domain,
                    ["domain"] = candidate.Domain,
                    ["tenant"] = candidate.Tenant,
                    ["wd_server"] = candidate.Server,
                    ["site"] = candidate.Site,
                    ["api_url"] = apiUrl,
                    ["careers_url"] = careersUrl,
                    ["status_code"] = ((int)response.StatusCode).ToString(),
                    ["final_url"] = apiUrl,
                    ["validated"] = "True",
                    ["total_jobs"] = total ?? "",
                    ["sample_title"] = sampleTitle,
                    ["sample_external_path"] = sampleExternalPath,
                    ["sample_job_url"] = JobUrl(candidate, sampleExternalPath),
                    ["sample_still_available"] = sampleStillAvailable.ToString(),
                    ["known_from_jobsites"] = knownFromJobSites.ToString(),
                    ["commoncrawl_index"] = indexId,
                    ["commoncrawl_url"] = candidate.CommonCrawlUrl,
                    ["error"] = "",
                    ["candidate_count"] = "1",
                    ["attempted_sites"] = candidate.Site,
                    ["discovery_notes"] = "Common Crawl Workday job URL discovery",
                    ["elapsed_seconds"] = (DateTime.UtcNow - started).TotalSeconds.ToString("0.00")
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Workday-CC] {candidate.Domain}/{candidate.Site} validation error: {ex.Message}");
                return null;
            }
        }

        private static async Task<bool> IsSampleStillAvailableAsync(WorkdayCandidate candidate, string externalPath)
        {
            if (string.IsNullOrWhiteSpace(externalPath)) return false;

            try
            {
                var path = externalPath.TrimStart('/');
                if (path.StartsWith("job/", StringComparison.OrdinalIgnoreCase)) path = path.Substring(4);
                var detailUrl = $"https://{candidate.Domain}/wday/cxs/{candidate.Tenant}/{candidate.Site}/job/{path}";

                using var request = new HttpRequestMessage(HttpMethod.Get, detailUrl);
                request.Headers.Referrer = new Uri(JobUrl(candidate, externalPath));
                using var response = await _http.SendAsync(request);
                if (!response.IsSuccessStatusCode) return false;

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return doc.RootElement.TryGetProperty("jobPostingInfo", out var info) &&
                       !string.IsNullOrWhiteSpace(GetText(info, "title"));
            }
            catch
            {
                return false;
            }
        }

        private static string JobUrl(WorkdayCandidate candidate, string externalPath)
        {
            if (string.IsNullOrWhiteSpace(externalPath)) return "";
            if (externalPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                externalPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return externalPath;
            }

            return externalPath.StartsWith("/")
                ? $"https://{candidate.Domain}/en-US/{candidate.Site}{externalPath}"
                : $"https://{candidate.Domain}/en-US/{candidate.Site}/job/{externalPath}";
        }

        private static string JobUrl(Dictionary<string, string> row, string externalPath)
        {
            var domain = AtsCsv.Get(row, "domain");
            var site = AtsCsv.Get(row, "site");
            var path = externalPath ?? "";
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            if (path.StartsWith("/")) return $"https://{domain}/en-US/{site}{path}";
            return $"https://{domain}/en-US/{site}/job/{path}";
        }

        private static string DetailApiUrl(Dictionary<string, string> row, string externalPath)
        {
            var domain = AtsCsv.Get(row, "domain");
            var tenant = AtsCsv.Get(row, "tenant");
            var site = AtsCsv.Get(row, "site");
            var path = (externalPath ?? "").Trim();

            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                path = new Uri(path).AbsolutePath;
            }

            path = path.TrimStart('/');
            if (path.StartsWith("job/", StringComparison.OrdinalIgnoreCase)) path = path.Substring(4);
            return $"https://{domain}/wday/cxs/{tenant}/{site}/job/{path}";
        }

        private static string CompanyName(Dictionary<string, string> row)
        {
            var tenant = AtsCsv.Get(row, "tenant");
            if (string.IsNullOrWhiteSpace(tenant))
            {
                tenant = AtsCsv.Get(row, "domain").Split('.').FirstOrDefault() ?? "";
            }

            var text = Regex.Replace(tenant, "[_-]+", " ").Trim();
            return string.IsNullOrWhiteSpace(text)
                ? ""
                : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        private static string CategoryFromJob(JsonElement job)
        {
            foreach (var key in new[] { "jobFamily", "jobFamilyGroup", "jobProfile", "workerSubType", "employmentType" })
            {
                var value = GetText(job, key);
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }

            if (job.TryGetProperty("bulletFields", out var bullets) && bullets.ValueKind == JsonValueKind.Array)
            {
                var text = string.Join(" | ", bullets.EnumerateArray().Select(v => v.ToString()).Where(v => !string.IsNullOrWhiteSpace(v)));
                return text.Length > 250 ? text.Substring(0, 250) : text;
            }

            return "";
        }

        private static bool IsRemote(string location, JsonElement job)
        {
            var text = string.Join(" ", new[]
            {
                location,
                GetText(job, "workplaceType"),
                GetText(job, "remoteType"),
                GetText(job, "jobType")
            }).ToLowerInvariant();

            return text.Contains("remote") || text.Contains("work from home") || text.Contains("virtual");
        }

        private static string WorkdayReferenceId(Dictionary<string, string> row, JsonElement job, string externalPath, string category)
        {
            foreach (var candidate in new[] { GetText(job, "jobReqId"), category, ReferenceIdFromPath(externalPath), ReferenceIdFromText(category) })
            {
                if (LooksLikeReferenceId(candidate)) return candidate.Trim();
            }

            return GeneratedWorkdayReferenceId(row, job, externalPath);
        }

        private static string PreferredReference(string currentReferenceId, string detailReferenceId)
        {
            if (LooksLikeReferenceId(detailReferenceId) &&
                (string.IsNullOrWhiteSpace(currentReferenceId) ||
                 currentReferenceId.StartsWith("workday:h", StringComparison.OrdinalIgnoreCase)))
            {
                return detailReferenceId.Trim();
            }

            return currentReferenceId ?? "";
        }

        private static string GeneratedWorkdayReferenceId(Dictionary<string, string> row, JsonElement job, string externalPath)
        {
            var key = string.Join("|", new[]
            {
                AtsCsv.Get(row, "domain"),
                AtsCsv.Get(row, "tenant"),
                AtsCsv.Get(row, "site"),
                externalPath,
                GetText(job, "title")
            }).ToLowerInvariant();

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            return $"workday:h{Convert.ToHexString(bytes, 0, 8).ToLowerInvariant()}";
        }

        private static bool LooksLikeReferenceId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var text = value.Trim();
            return text.Length <= 80 && !text.Contains(" ") && !text.Contains("/") && Regex.IsMatch(text, @"\d");
        }

        private static string ReferenceIdFromPath(string externalPath)
        {
            var leaf = (externalPath ?? "").Trim().TrimEnd('/').Split('/').LastOrDefault() ?? "";
            var match = Regex.Match(leaf, @"_([A-Za-z]{1,12}[_-]?\d[\w-]*|\d[\w-]*)$");
            return match.Success ? match.Groups[1].Value : "";
        }

        private static string ReferenceIdFromText(string value)
        {
            var match = Regex.Match(value ?? "", @"\b([A-Za-z]{1,12}[_-]?\d[\w-]*|\d{3,}[\w-]*)\b");
            return match.Success ? match.Groups[1].Value : "";
        }

        private static DateTime? ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var text = value.Trim();
            if (text.Equals("Posted Today", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("Today", StringComparison.OrdinalIgnoreCase))
            {
                return DateTime.Today;
            }

            if (text.Equals("Posted Yesterday", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("Yesterday", StringComparison.OrdinalIgnoreCase))
            {
                return DateTime.Today.AddDays(-1);
            }

            var dayMatch = Regex.Match(text, @"Posted\s+(\d+)\+?\s+Days?\s+Ago", RegexOptions.IgnoreCase);
            if (dayMatch.Success && int.TryParse(dayMatch.Groups[1].Value, out var days))
            {
                return DateTime.Today.AddDays(-days);
            }

            return DateTime.TryParse(text, out var parsed) ? parsed : null;
        }

        private static string CleanDescription(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var text = WebUtility.HtmlDecode(Regex.Replace(value, "<[^>]+>", " "));
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private static string GetTextOrDescriptor(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value)) return null;
            if (value.ValueKind == JsonValueKind.Object)
            {
                return GetText(value, "descriptor") ?? GetText(value, "name") ?? GetText(value, "value");
            }

            return GetText(element, property);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }

            return "";
        }

        private static int? GetInt(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value)) return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
            return int.TryParse(value.ToString(), out number) ? number : null;
        }

        private static string NormalizeDomain(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var text = value.Trim();
            if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                text = "https://" + text.TrimStart('/');
            }

            return Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                   uri.Host.EndsWith(".myworkdayjobs.com", StringComparison.OrdinalIgnoreCase)
                ? uri.Host.ToLowerInvariant()
                : "";
        }

        private static string GetText(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value)) return null;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => "True",
                JsonValueKind.False => "False",
                _ => null
            };
        }

        private static void WriteCsv(string path, List<Dictionary<string, string>> rows, string[] fields)
        {
            using var writer = new StreamWriter(path, false, Encoding.UTF8);
            writer.WriteLine(string.Join(",", fields.Select(Escape)));
            foreach (var row in rows)
            {
                writer.WriteLine(string.Join(",", fields.Select(field => Escape(row.TryGetValue(field, out var value) ? value : ""))));
            }
        }

        private static string Escape(string value)
        {
            value ??= "";
            return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
                ? $"\"{value.Replace("\"", "\"\"")}\""
                : value;
        }

        private sealed class CommonCrawlIndex
        {
            public string Id { get; set; }
            public string CdxApi { get; set; }
        }

        private sealed class WorkdayCandidate
        {
            public string Domain { get; set; }
            public string Tenant { get; set; }
            public string Server { get; set; }
            public string Site { get; set; }
            public string CommonCrawlUrl { get; set; }
        }

        private sealed class DetailHttpResult
        {
            public DetailHttpResult(string statusCode, bool isSuccess, string body, string error = "")
            {
                StatusCode = statusCode ?? "";
                IsSuccess = isSuccess;
                Body = body ?? "";
                Error = error ?? "";
            }

            public string StatusCode { get; }
            public bool IsSuccess { get; }
            public string Body { get; }
            public string Error { get; }
        }
    }
}
