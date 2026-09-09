param(
    [string]$SeedCsv = "outputs\workday_commoncrawl\workday_db_seed_urls_sample.csv",
    [string]$SkipUrlCsv = "outputs\workday_commoncrawl\workday_commoncrawl_skip_previous_all.csv",
    [string]$JobSitesCsv = "input\JobSites.csv",
    [string]$OutputCsv = "outputs\workday_commoncrawl\workday_valid_dbseed_batch2_100.csv",
    [int]$Limit = 100,
    [int]$DelayMs = 200
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot

function Get-HostFromText([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    if ($Text -match '(?i)([a-z0-9-]+\.(?:wd\d+|myworkdayjobs)\.myworkdayjobs\.com)') {
        return $matches[1].ToLowerInvariant()
    }
    if ($Text -match '(?i)([a-z0-9-]+\.myworkdayjobs\.com)') {
        return $matches[1].ToLowerInvariant()
    }
    return $null
}

function Get-KnownDomains([string]$Path) {
    $set = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    if (-not (Test-Path -LiteralPath $Path)) { return ,$set }

    foreach ($row in Import-Csv -LiteralPath $Path) {
        foreach ($prop in $row.PSObject.Properties) {
            $domainName = Get-HostFromText $prop.Value
            if ($domainName) { [void]$set.Add($domainName) }
        }
    }

    return ,$set
}

function Get-SkipKeys([string]$Path) {
    $set = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    if (-not (Test-Path -LiteralPath $Path)) { return ,$set }

    foreach ($row in Import-Csv -LiteralPath $Path) {
        if ($row.domain -and $row.site) {
            [void]$set.Add("$($row.domain.Trim())|$($row.site.Trim())")
        }
    }

    return ,$set
}

function Convert-JobUrlToCandidate([string]$Url) {
    if ([string]::IsNullOrWhiteSpace($Url)) { return $null }

    try {
        $uri = [Uri]$Url
    } catch {
        return $null
    }

    if (-not $uri.Host.EndsWith(".myworkdayjobs.com", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $null
    }

    $segments = $uri.AbsolutePath.Trim("/") -split "/" | Where-Object { $_ }
    $jobIndex = [Array]::IndexOf($segments, "job")
    if ($jobIndex -lt 1) { return $null }

    $site = $segments[$jobIndex - 1]
    if ($site -match '^[a-z]{2}(?:-[A-Z]{2})?$' -and $jobIndex -ge 2) {
        $site = $segments[$jobIndex - 2]
    }

    if ([string]::IsNullOrWhiteSpace($site)) { return $null }

    $hostParts = $uri.Host.Split(".")
    if ($hostParts.Count -lt 3) { return $null }

    $tenant = $hostParts[0]
    $server = $hostParts[1]

    [pscustomobject]@{
        Domain = $uri.Host.ToLowerInvariant()
        Tenant = $tenant
        Server = $server
        Site = $site
        JobUrl = $uri.AbsoluteUri
    }
}

function Invoke-WorkdayValidation($Candidate) {
    $apiUrl = "https://$($Candidate.Domain)/wday/cxs/$($Candidate.Tenant)/$($Candidate.Site)/jobs"
    $careersUrl = "https://$($Candidate.Domain)/en-US/$($Candidate.Site)"
    $body = @{ appliedFacets = @{}; limit = 1; offset = 0; searchText = "" } | ConvertTo-Json -Depth 5

    try {
        $response = Invoke-RestMethod -Method Post -Uri $apiUrl -Body $body -ContentType "application/json" -Headers @{
            "Accept" = "application/json"
            "User-Agent" = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
            "Referer" = $careersUrl
        } -TimeoutSec 30

        $total = 0
        if ($null -ne $response.total) {
            $total = [int]$response.total
        }
        if ($total -le 0) { return $null }

        $sample = $null
        if ($response.jobPostings -and $response.jobPostings.Count -gt 0) {
            $sample = $response.jobPostings[0]
        }

        $externalPath = ""
        $sampleTitle = ""
        $sampleUrl = ""
        if ($sample) {
            $externalPath = [string]$sample.externalPath
            $sampleTitle = [string]$sample.title
            if (-not [string]::IsNullOrWhiteSpace($externalPath)) {
                $sampleUrl = "https://$($Candidate.Domain)/en-US/$($Candidate.Site)$externalPath"
            }
        }

        [pscustomobject]@{
            source_row = "dbseed"
            original_domain = $Candidate.Domain
            domain = $Candidate.Domain
            tenant = $Candidate.Tenant
            wd_server = $Candidate.Server
            site = $Candidate.Site
            api_url = $apiUrl
            careers_url = $careersUrl
            status_code = "200"
            final_url = $apiUrl
            validated = "True"
            total_jobs = $total
            sample_title = $sampleTitle
            sample_external_path = $externalPath
            sample_job_url = $sampleUrl
            sample_still_available = if ($sampleUrl) { "True" } else { "False" }
            known_from_jobsites = "False"
            commoncrawl_index = ""
            commoncrawl_url = $Candidate.JobUrl
            error = ""
            candidate_count = "1"
            attempted_sites = $Candidate.Site
            discovery_notes = "database-seeded Workday job URL reconstruction"
            elapsed_seconds = ""
        }
    } catch {
        return $null
    }
}

if (-not (Test-Path -LiteralPath $SeedCsv)) {
    throw "Seed CSV not found: $SeedCsv"
}

New-Item -ItemType Directory -Path (Split-Path -Parent $OutputCsv) -Force | Out-Null

$knownDomains = Get-KnownDomains $JobSitesCsv
$skipKeys = Get-SkipKeys $SkipUrlCsv
$seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$rows = New-Object System.Collections.Generic.List[object]

foreach ($seed in Import-Csv -LiteralPath $SeedCsv) {
    if ($rows.Count -ge $Limit) { break }

    $candidate = Convert-JobUrlToCandidate $seed.url
    if (-not $candidate) { continue }
    if ($knownDomains.Contains($candidate.Domain)) { continue }

    $key = "$($candidate.Domain)|$($candidate.Site)"
    if ($skipKeys.Contains($key)) { continue }
    if (-not $seen.Add($key)) { continue }

    $validated = Invoke-WorkdayValidation $candidate
    if ($validated) {
        $rows.Add($validated)
        Write-Host "[Workday-DBSeed] valid $($rows.Count)/${Limit}: $($candidate.Domain)/$($candidate.Site) -> $($validated.total_jobs) jobs"
    }

    if ($DelayMs -gt 0) {
        Start-Sleep -Milliseconds $DelayMs
    }
}

$rows | Export-Csv -LiteralPath $OutputCsv -NoTypeInformation -Encoding UTF8
$totalJobs = ($rows | Measure-Object -Property total_jobs -Sum).Sum
Write-Host "[Workday-DBSeed] Output: $((Resolve-Path -LiteralPath $OutputCsv).Path)"
Write-Host "[Workday-DBSeed] Validated URLs: $($rows.Count)"
Write-Host "[Workday-DBSeed] Total jobs across validated URLs: $totalJobs"
