$ErrorActionPreference = 'Continue'
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12

# (tenant, current wd, current site) — only the 38 failures from prior pass
$probe = @(
  @('cisco','wd5','External'),
  @('servicenow','wd1','ServiceNowExternalCareer'),
  @('autodesk','wd1','Ext'),
  @('amd','wd1','External'),
  @('qualcomm','wd5','External'),
  @('broadcom','wd1','External_Career_Site'),
  @('vmware','wd1','VMware'),
  @('analog','wd1','External'),
  @('att','wd1','ATTExternal'),
  @('verizon','wd5','External'),
  @('t-mobile','wd1','External'),
  @('charter','wd5','External'),
  @('cox','wd5','External'),
  @('visa','wd1','Visa'),
  @('aexp','wd1','External'),
  @('jpmc','wd5','ExternalSite'),
  @('morganstanley','wd5','External'),
  @('blackrock','wd1','Professional'),
  @('schwab','wd1','External'),
  @('ally','wd5','Ally_External_Careers'),
  @('discover','wd5','Discover'),
  @('humana','wd1','Humana_External_Career_Site'),
  @('elevancehealth','wd1','ANT_External'),
  @('merck','wd5','SearchJobs'),
  @('moderna','wd1','M_tx'),
  @('abbvie','wd5','External'),
  @('siemens','wd1','External'),
  @('honeywell','wd1','Honeywellexternal'),
  @('ge','wd5','GE_External'),
  @('pepsico','wd5','PepsiCoJobs'),
  @('coca-cola','wd1','coca-cola_career_site'),
  @('mcdonalds','wd5','McDonalds'),
  @('delta','wd5','DeltaCareers'),
  @('united','wd5','ualcareers'),
  @('aa','wd1','American_Airlines_Career_Site'),
  @('marriott','wd1','Marriott'),
  @('hilton','wd1','Hilton_Worldwide')
)

$wdCandidates = @('wd1','wd3','wd5','wd12','wd2','wd6')

foreach ($p in $probe) {
  $tenant = $p[0]
  $found = $false
  foreach ($wd in $wdCandidates) {
    $root = "https://$tenant.$wd.myworkdayjobs.com/"
    try {
      $r = Invoke-WebRequest -Uri $root -Method Get -MaximumRedirection 3 -TimeoutSec 8 -UseBasicParsing -ErrorAction Stop
      $final = $r.BaseResponse.ResponseUri.AbsoluteUri
      # Extract /en-US/<Site> or /<Site>
      $site = ''
      if ($final -match 'myworkdayjobs\.com/(?:[a-zA-Z-]+/)?([^/?#]+)') { $site = $matches[1] }
      Write-Output ("{0,-18} -> wd={1} site={2}  (final={3})" -f $tenant, $wd, $site, $final)
      $found = $true
      break
    } catch {
      $resp = $_.Exception.Response
      $code = if ($resp) { [int]$resp.StatusCode } else { 'ERR' }
      if ($code -ge 300 -and $code -lt 400) {
        $loc = $resp.Headers['Location']
        Write-Output ("{0,-18} -> wd={1} redirect={2}" -f $tenant, $wd, $loc)
        $found = $true
        break
      }
      # try next wd silently unless final iteration
    }
  }
  if (-not $found) {
    Write-Output ("{0,-18} -> NOT FOUND on any wdN" -f $tenant)
  }
}
