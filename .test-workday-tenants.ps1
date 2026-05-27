$ErrorActionPreference = 'Continue'
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12

$tenants = @(
  @{ Company='Cisco';            Tenant='cisco';         Site='External';                       Wd='wd5'  },
  @{ Company='ServiceNow';       Tenant='servicenow';    Site='ServiceNowExternalCareer';       Wd='wd1'  },
  @{ Company='Autodesk';         Tenant='autodesk';      Site='Ext';                            Wd='wd1'  },
  @{ Company='AMD';              Tenant='amd';           Site='External';                       Wd='wd1'  },
  @{ Company='Qualcomm';         Tenant='qualcomm';      Site='External';                       Wd='wd5'  },
  @{ Company='Broadcom';         Tenant='broadcom';      Site='External_Career_Site';           Wd='wd1'  },
  @{ Company='Dell';             Tenant='dell';          Site='External';                       Wd='wd1'  },
  @{ Company='HP';               Tenant='hp';            Site='ExternalCareerSite';             Wd='wd5'  },
  @{ Company='HPE';              Tenant='hpe';           Site='Jobsathpe';                      Wd='wd5'  },
  @{ Company='VMware';           Tenant='vmware';        Site='VMware';                         Wd='wd1'  },
  @{ Company='Micron';           Tenant='micron';        Site='External';                       Wd='wd1'  },
  @{ Company='NXP';              Tenant='nxp';           Site='careers';                        Wd='wd3'  },
  @{ Company='Analog Devices';   Tenant='analog';        Site='External';                       Wd='wd1'  },
  @{ Company='AT&T';             Tenant='att';           Site='ATTExternal';                    Wd='wd1'  },
  @{ Company='Verizon';          Tenant='verizon';       Site='External';                       Wd='wd5'  },
  @{ Company='T-Mobile';         Tenant='t-mobile';      Site='External';                       Wd='wd1'  },
  @{ Company='Comcast';          Tenant='comcast';       Site='Comcast_Careers';                Wd='wd5'  },
  @{ Company='Charter';          Tenant='charter';       Site='External';                       Wd='wd5'  },
  @{ Company='Cox';              Tenant='cox';           Site='External';                       Wd='wd5'  },
  @{ Company='Mastercard';       Tenant='mastercard';    Site='CorporateCareers';               Wd='wd1'  },
  @{ Company='Visa';             Tenant='visa';          Site='Visa';                           Wd='wd1'  },
  @{ Company='American Express'; Tenant='aexp';          Site='External';                       Wd='wd1'  },
  @{ Company='JPMorgan Chase';   Tenant='jpmc';          Site='ExternalSite';                   Wd='wd5'  },
  @{ Company='Morgan Stanley';   Tenant='morganstanley'; Site='External';                       Wd='wd5'  },
  @{ Company='BlackRock';        Tenant='blackrock';     Site='Professional';                   Wd='wd1'  },
  @{ Company='Charles Schwab';   Tenant='schwab';        Site='External';                       Wd='wd1'  },
  @{ Company='Ally';             Tenant='ally';          Site='Ally_External_Careers';          Wd='wd5'  },
  @{ Company='Discover';         Tenant='discover';      Site='Discover';                       Wd='wd5'  },
  @{ Company='Cigna';            Tenant='cigna';         Site='cignacareers';                   Wd='wd5'  },
  @{ Company='Humana';           Tenant='humana';        Site='Humana_External_Career_Site';    Wd='wd1'  },
  @{ Company='Elevance Health';  Tenant='elevancehealth';Site='ANT_External';                   Wd='wd1'  },
  @{ Company='Merck';            Tenant='merck';         Site='SearchJobs';                     Wd='wd5'  },
  @{ Company='AstraZeneca';      Tenant='astrazeneca';   Site='Careers';                        Wd='wd3'  },
  @{ Company='GSK';              Tenant='gsk';           Site='GSKCareers';                     Wd='wd5'  },
  @{ Company='Moderna';          Tenant='moderna';       Site='M_tx';                           Wd='wd1'  },
  @{ Company='AbbVie';           Tenant='abbvie';        Site='External';                       Wd='wd5'  },
  @{ Company='Eli Lilly';        Tenant='lilly';         Site='LLY';                            Wd='wd5'  },
  @{ Company='Medtronic';        Tenant='medtronic';     Site='MedtronicCareers';               Wd='wd1'  },
  @{ Company='Abbott';           Tenant='abbott';        Site='abbottcareers';                  Wd='wd5'  },
  @{ Company='Siemens';          Tenant='siemens';       Site='External';                       Wd='wd1'  },
  @{ Company='Honeywell';        Tenant='honeywell';     Site='Honeywellexternal';              Wd='wd1'  },
  @{ Company='GE Aerospace';     Tenant='ge';            Site='GE_External';                    Wd='wd5'  },
  @{ Company='PepsiCo';          Tenant='pepsico';       Site='PepsiCoJobs';                    Wd='wd5'  },
  @{ Company='Coca-Cola';        Tenant='coca-cola';     Site='coca-cola_career_site';          Wd='wd1'  },
  @{ Company="McDonald's";       Tenant='mcdonalds';     Site='McDonalds';                      Wd='wd5'  },
  @{ Company='Delta';            Tenant='delta';         Site='DeltaCareers';                   Wd='wd5'  },
  @{ Company='United Airlines';  Tenant='united';        Site='ualcareers';                     Wd='wd5'  },
  @{ Company='American Airlines';Tenant='aa';            Site='American_Airlines_Career_Site';  Wd='wd1'  },
  @{ Company='Marriott';         Tenant='marriott';      Site='Marriott';                       Wd='wd1'  },
  @{ Company='Hilton';           Tenant='hilton';        Site='Hilton_Worldwide';               Wd='wd1'  }
)

$body = '{"appliedFacets":{},"limit":1,"offset":0,"searchText":""}'
$results = @()
$i = 0
foreach ($t in $tenants) {
  $i++
  $url = "https://$($t.Tenant).$($t.Wd).myworkdayjobs.com/wday/cxs/$($t.Tenant)/$($t.Site)/jobs"
  $ref = "https://$($t.Tenant).$($t.Wd).myworkdayjobs.com/en-US/$($t.Site)"
  $headers = @{
    'Accept'          = 'application/json'
    'Accept-Language' = 'en-US'
    'Referer'         = $ref
    'User-Agent'      = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36'
  }
  $status = ''; $total = ''
  try {
    $r = Invoke-WebRequest -Uri $url -Method Post -Body $body -ContentType 'application/json' -Headers $headers -TimeoutSec 15 -UseBasicParsing -ErrorAction Stop
    $status = [int]$r.StatusCode
    try {
      $j = $r.Content | ConvertFrom-Json
      $total = $j.total
    } catch { $total = '?' }
  } catch {
    $resp = $_.Exception.Response
    if ($resp) { $status = [int]$resp.StatusCode } else { $status = 'ERR' }
    $total = '-'
  }
  $line = "{0,2}. {1,-20} {2,-18} {3,-32} {4,4} status={5,-4} total={6}" -f $i, $t.Company, "$($t.Tenant).$($t.Wd)", $t.Site, '', $status, $total
  Write-Output $line
  $results += [pscustomobject]@{ Company=$t.Company; Tenant=$t.Tenant; Wd=$t.Wd; Site=$t.Site; Status=$status; Total=$total }
}

Write-Output ''
Write-Output '=== SUMMARY ==='
$ok   = $results | Where-Object { $_.Status -eq 200 -and $_.Total -ne '?' -and [int]($_.Total) -gt 0 }
$zero = $results | Where-Object { $_.Status -eq 200 -and ($_.Total -eq 0 -or $_.Total -eq '?') }
$fail = $results | Where-Object { $_.Status -ne 200 }
Write-Output ("OK with jobs : {0}" -f $ok.Count)
Write-Output ("OK zero jobs : {0}" -f $zero.Count)
Write-Output ("Failed       : {0}" -f $fail.Count)
Write-Output ''
Write-Output '--- FAILED ---'
$fail | ForEach-Object { Write-Output ("  {0,-22} {1}.{2}/{3}  status={4}" -f $_.Company, $_.Tenant, $_.Wd, $_.Site, $_.Status) }
