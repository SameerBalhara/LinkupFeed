param(
    [string]$UrlCsv = "outputs\workday_commoncrawl\workday_commoncrawl_valid_top100_latest.csv",
    [string]$TargetTable = "dbo.temp_tbl_Scrap_jobs_compare_20260810",
    [string]$ConnectionString = "Data source=209.59.189.133\ITJOBCAFESERVER,1435;Initial Catalog=ITJC_SCRAPPER;User Id=itjobcafe;Pwd=Chand@789!;TrustServerCertificate=True;Connection Timeout=20",
    [int]$Limit = 0,
    [int]$LimitSites = 0,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot

if (-not (Test-Path -LiteralPath $UrlCsv)) {
    throw "URL CSV not found: $UrlCsv"
}

$env:ITJC_SCRAPPER_CONNECTION_STRING = $ConnectionString
$env:ITJC_SCRAPPER_TARGET_TABLE = $TargetTable

$argsList = @(
    "run",
    "--no-build",
    "--",
    "all",
    "--only",
    "workday",
    "--workday-url-csv",
    $UrlCsv,
    "--target-table",
    $TargetTable
)

if ($DryRun) {
    $argsList += "--dry-run"
}

if ($Limit -gt 0) {
    $argsList += @("--limit", $Limit.ToString())
}

if ($LimitSites -gt 0) {
    $argsList += @("--limit-sites", $LimitSites.ToString())
}

Write-Host "[CommonCrawl-Compare] URL CSV: $UrlCsv"
Write-Host "[CommonCrawl-Compare] Target table: $TargetTable"
Write-Host "[CommonCrawl-Compare] Mode: $(if ($DryRun) { 'dry run' } else { 'write' })"

dotnet @argsList
