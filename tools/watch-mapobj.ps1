param([int]$Minutes = 6)

# Poll 8111 while the user enters a battle, so we can see the exact moment
# map_obj starts carrying objects. Log only, no interpretation.

$log = Join-Path ([Environment]::GetFolderPath('Desktop')) 'map_obj-watch.csv'
"time,map_bytes,mapinfo_valid,has_objectives,tas,alt,gear" | Out-File $log -Encoding utf8

$end = (Get-Date).AddMinutes($Minutes)
while ((Get-Date) -lt $end) {
    $t = Get-Date -Format 'HH:mm:ss'
    try {
        $mo = (Invoke-WebRequest 'http://127.0.0.1:8111/map_obj.json' -UseBasicParsing -TimeoutSec 2).Content
        $mi = (Invoke-WebRequest 'http://127.0.0.1:8111/map_info.json' -UseBasicParsing -TimeoutSec 2).Content
        $ms = (Invoke-WebRequest 'http://127.0.0.1:8111/mission.json' -UseBasicParsing -TimeoutSec 2).Content
        $st = (Invoke-WebRequest 'http://127.0.0.1:8111/state' -UseBasicParsing -TimeoutSec 2).Content

        $moBytes = [Text.Encoding]::UTF8.GetByteCount([string]$mo)
        $miValid = if ($mi -match '"valid"\s*:\s*true') { '1' } else { '0' }
        $hasObj  = if ($ms -match '"objectives"\s*:\s*null') { '0' } else { '1' }

        $tas = ''
        if ($st -match '"TAS, km/h":\s*([\d.]+)') { $tas = $matches[1] }
        $alt = ''
        if ($st -match '"H, m":\s*(-?[\d.]+)') { $alt = $matches[1] }
        $gear = ''
        if ($st -match '"gear, %":\s*([\d.]+)') { $gear = $matches[1] }

        "$t,$moBytes,$miValid,$hasObj,$tas,$alt,$gear" | Out-File $log -Append -Encoding utf8
    }
    catch {
        "$t,ERR,,,,," | Out-File $log -Append -Encoding utf8
    }
    Start-Sleep -Seconds 2
}
"watch done -> $log"
