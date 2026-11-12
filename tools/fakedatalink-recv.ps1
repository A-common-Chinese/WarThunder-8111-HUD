# WtHud 数据链伪接收端
#
# 用途：在没有 WtHud.exe 的机器上接收 WTDL2 报文，验证「包能不能穿过来」。
#       和 fakedatalink.ps1 配对使用 —— 两端各开一个收、一个发，一轮就能验证双向。
#
# 不需要装 .NET，Windows 自带的 PowerShell 5.1 就能跑。
#
# 例：
#   .\fakedatalink-recv.ps1                 # 一直收，Ctrl+C 停，停时打印统计
#   .\fakedatalink-recv.ps1 -Seconds 120    # 收 120 秒自动退出
#   .\fakedatalink-recv.ps1 -Quiet          # 只打印统计，不打每一包
#
# ★ 解析规则刻意和 WtHud 的 DataLink.TryParsePayload 保持一致：
#   - 必须 4 段以上、首段 WTDL2，id/alt/vy 必须是整数
#   - 航向 0..359、坐标 0..100000、坡度 -900..900，越界只丢该段，不丢整包
#   这样「这里能收」才等价于「HUD 能收」，不会出现脚本通过、游戏里没反应的假绿。

param(
    [int]$Port = 48017,
    [double]$Seconds = 0,
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"

try {
    $udp = New-Object System.Net.Sockets.UdpClient -ArgumentList $Port
} catch {
    Write-Host "cannot bind udp $Port : $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "另一个 WtHud / dlprobe / 本脚本已经在跑？先关掉它（同一个端口只能一个人占）。"
    exit 2
}

$udp.Client.ReceiveTimeout = 500

$rx = 0
$bad = 0
$nodes = @{}
$lastPeer = ""
$clock = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host "datalink recv: listening on udp $Port" -NoNewline
if ($Seconds -gt 0) { Write-Host " for ${Seconds}s" } else { Write-Host " (Ctrl+C to stop)" }
Write-Host "  expecting WTDL2|id|alt|vy|hdg|turn[|x|y[|bank]]"
Write-Host ""

$remote = New-Object System.Net.IPEndPoint -ArgumentList ([System.Net.IPAddress]::Any), 0

try {
    while ($true) {
        if ($Seconds -gt 0 -and $clock.Elapsed.TotalSeconds -ge $Seconds) { break }

        try {
            $data = $udp.Receive([ref]$remote)
        } catch [System.Net.Sockets.SocketException] {
            if ($_.Exception.SocketErrorCode -eq "TimedOut") { continue }
            throw
        }

        $rx++
        $from = $remote.ToString()
        $lastPeer = $from
        $text = [System.Text.Encoding]::ASCII.GetString($data)
        $parts = $text.Split("|")

        $ok = $parts.Length -ge 4 -and $parts[0] -eq "WTDL2"
        $id = 0; $alt = 0; $vy = 0.0; $hdg = "-"; $turn = "-"; $pos = "-"; $bank = "-"

        if ($ok) {
            $vyTenths = 0
            $ok = [int]::TryParse($parts[1], [ref]$id) -and
                  [int]::TryParse($parts[2], [ref]$alt) -and
                  [int]::TryParse($parts[3], [ref]$vyTenths)
            if ($ok) {
                $vy = $vyTenths / 10.0
                if ($parts.Length -ge 5) {
                    $h = 0
                    if ([int]::TryParse($parts[4], [ref]$h) -and $h -ge 0 -and $h -lt 360) { $hdg = "$h" }
                }
                if ($parts.Length -ge 6) {
                    $t = 0
                    if ([int]::TryParse($parts[5], [ref]$t)) { $turn = ("{0:F1}" -f ($t / 10.0)) }
                }
                if ($parts.Length -ge 8) {
                    $ix = 0; $iy = 0
                    if ([int]::TryParse($parts[6], [ref]$ix) -and [int]::TryParse($parts[7], [ref]$iy) -and
                        $ix -ge 0 -and $ix -le 100000 -and $iy -ge 0 -and $iy -le 100000) {
                        $pos = ("{0:F5},{1:F5}" -f ($ix / 100000.0), ($iy / 100000.0))
                    }
                }
                if ($parts.Length -ge 9) {
                    $b = 0
                    if ([int]::TryParse($parts[8], [ref]$b) -and $b -ge -900 -and $b -le 900) {
                        $bank = ("{0:F0}deg" -f ($b / 10.0))
                    }
                }
            }
        }

        if (-not $ok) {
            $bad++
            if (-not $Quiet) {
                Write-Host ("  {0}  from {1,-22}  BAD  {2}" -f (Get-Date -Format "HH:mm:ss.fff"), $from, $text)
            }
            continue
        }

        $nodes[$id] = $true

        if (-not $Quiet) {
            Write-Host ("  {0}  from {1,-22} id={2,-7} alt={3,5}m  vy={4,6:F1}  hdg={5,4}  turn={6,6}  bank={7,6}  pos={8}" -f `
                (Get-Date -Format "HH:mm:ss.fff"), $from, $id, $alt, $vy, $hdg, $turn, $bank, $pos)
        }
    }
} finally {
    $udp.Close()
}

Write-Host ""
Write-Host ("result: rx={0}  bad={1}  nodes={2}  last={3}" -f `
    $rx, $bad, $nodes.Count, $(if ($lastPeer) { $lastPeer } else { "none" }))

if ($rx -eq 0) {
    Write-Host ""
    Write-Host "no packet arrived. check, in this order:"
    Write-Host "  1. firewall on BOTH ends (the virtual adapter is usually 'public' - use -Profile Any)"
    Write-Host "  2. target ip:port - the sender must point at THIS machine's virtual IP, not 192.168.x"
    Write-Host "  3. ping the peer first; if ping fails, udp will not pass either"
    exit 1
}

if ($bad -gt 0) {
    Write-Host "  $bad packet(s) failed to parse - the wire is rewriting or truncating them."
}
exit 0
