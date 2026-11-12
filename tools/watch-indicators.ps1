param([int]$Minutes = 12)

# Sample indicators / map_obj / state once a second while the user switches
# aircraft, so a change from valid:false to valid:true cannot be missed.

$log = Join-Path ([Environment]::GetFolderPath('Desktop')) 'indicators-watch.csv'
"time,ind_bytes,ind_valid,ind_body,mapobj_bytes,mapinfo_valid,tas,alt,gear,rpm" | Out-File $log -Encoding utf8

$end = (Get-Date).AddMinutes($Minutes)
while ((Get-Date) -lt $end) {
    $t = Get-Date -Format 'HH:mm:ss'
    try {
        $ind = [string](Invoke-WebRequest 'http://127.0.0.1:8111/indicators' -UseBasicParsing -TimeoutSec 2).Content
        $mo  = [string](Invoke-WebRequest 'http://127.0.0.1:8111/map_obj.json' -UseBasicParsing -TimeoutSec 2).Content
        $mi  = [string](Invoke-WebRequest 'http://127.0.0.1:8111/map_info.json' -UseBasicParsing -TimeoutSec 2).Content
        $st  = [string](Invoke-WebRequest 'http://127.0.0.1:8111/state' -UseBasicParsing -TimeoutSec 2).Content

        $indValid = if ($ind -match '"valid"\s*:\s*true') { '1' } else { '0' }
        $miValid  = if ($mi  -match '"valid"\s*:\s*true') { '1' } else { '0' }
        $body     = ($ind -replace '\s+', ' ').Trim()
        if ($body.Length -gt 300) { $body = $body.Substring(0, 300) }

        $tas = ''; if ($st -match '"TAS, km/h":\s*([\d.]+)') { $tas = $matches[1] }
        $alt = ''; if ($st -match '"H, m":\s*(-?[\d.]+)')      { $alt = $matches[1] }
        $gear= ''; if ($st -match '"gear, %":\s*([\d.]+)')     { $gear= $matches[1] }
        $rpm = ''; if ($st -match '"RPM 1":\s*([\d.]+)')       { $rpm = $matches[1] }

        $indBytes = [Text.Encoding]::UTF8.GetByteCount($ind)
        $moBytes  = [Text.Encoding]::UTF8.GetByteCount($mo)

        # quote the body so commas inside it do not break the CSV
        "$t,$indBytes,$indValid,`"$body`",$moBytes,$miValid,$tas,$alt,$gear,$rpm" |
            Out-File $log -Append -Encoding utf8
    }
    catch {
        "$t,ERR,,,,,,,," | Out-File $log -Append -Encoding utf8
    }
    Start-Sleep -Seconds 1
}
"watch done -> $log"
