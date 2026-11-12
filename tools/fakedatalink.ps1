# WtHud 数据链伪发送端
#
# 用途：在没有游戏、没有 HUD 的另一台机器上，往指定地址持续发 WTDL2 报文，
#       用来验证「包到底能不能穿过去」（内网穿透 / VPN / 跨网段路由）。
#
# 不需要装 .NET，Windows 自带的 PowerShell 就能跑。
# 接收端用 WtHud.exe dlprobe 30 对着看，收到几行就说明链路通。
#
# 例：
#   .\fakedatalink.ps1 -Target 192.168.16.78:48017
#   .\fakedatalink.ps1 -Target 255.255.255.255:48017 -Quiet
#   .\fakedatalink.ps1 -Target 192.168.16.78:48017 -Mode fixed -Seconds 10
#
# 报文（一行文本，竖线分隔）：
#   WTDL2|节点ID|气压高度m|升降速度 0.1m/s|航向度|转向率 0.1°/s|归一化x|归一化y
# 后两段是 2026-11-12 追加的，坐标 = map_obj 那套 0..1，乘 100000 存整数。

param(
    [Parameter(Mandatory = $true)][string]$Target,
    [int]$Id = 777777,
    [double]$RateHz = 4,
    [ValidateSet("circle", "fixed")][string]$Mode = "circle",
    [double]$Seconds = 0,
    # 编队角色：0 = 不发（旧格式 8 段），1 = 长机，2 = 僚机（第 10 段）
    [ValidateRange(0, 2)][int]$Role = 0,
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"

$sep = $Target.LastIndexOf(":")
if ($sep -gt 0) {
    $ipText = $Target.Substring(0, $sep)
    $port = [int]$Target.Substring($sep + 1)
} else {
    $ipText = $Target
    $port = 48017
}

$ip = [System.Net.IPAddress]::Parse($ipText)
$ep = New-Object System.Net.IPEndPoint -ArgumentList $ip, $port
$udp = New-Object System.Net.Sockets.UdpClient
$udp.EnableBroadcast = $true

if (-not $Quiet) {
    Write-Host "fakedatalink: id=$Id  -> $ipText`:$port  mode=$Mode  rate=${RateHz}Hz " -NoNewline
    Write-Host " role=$Role"
    if ($Seconds -gt 0) { Write-Host "  for ${Seconds}s" } else { Write-Host "  (Ctrl+C to stop)" }
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$dt = 1.0 / $RateHz
$next = 0.0
$sent = 0
$failed = 0
$deg = 180.0 / [Math]::PI

while ($true) {
    $t = $sw.Elapsed.TotalSeconds
    if ($Seconds -gt 0 -and $t -ge $Seconds) { break }
    if ($t -lt $next) { Start-Sleep -Milliseconds 5; continue }
    $next = $t + $dt

    if ($Mode -eq "circle") {
        # 半径 0.02 归一化（64 km 地图约 1.3 km），10 圈/1000s，高度 3000±200 m
        $phase = $t * 0.1
        $xn = 0.5 + 0.02 * [Math]::Cos($phase)
        $yn = 0.5 + 0.02 * [Math]::Sin($phase)
        $alt = [int][Math]::Round(3000 + 200 * [Math]::Sin($t * 0.05))
        $vy = 200 * 0.05 * [Math]::Cos($t * 0.05)
        # 航向沿圆周切线：map_obj 的 y 向南，所以北是 -y
        $dx = -0.02 * 0.1 * [Math]::Sin($phase)
        $dy = 0.02 * 0.1 * [Math]::Cos($phase)
        $hdg = [Math]::Atan2($dx, -$dy) * $deg
        if ($hdg -lt 0) { $hdg += 360 }
        $turn = 0.1 * $deg
    }
    else {
        $xn = 0.5
        $yn = 0.5
        $alt = 3000
        $vy = 0.0
        $hdg = 90.0
        $turn = 0.0
    }

    $ix = [int][Math]::Round($xn * 100000)
    $iy = [int][Math]::Round($yn * 100000)
    $hdgInt = [int][Math]::Round($hdg) % 360
    if ($hdgInt -lt 0) { $hdgInt += 360 }
    $vyTenths = [int][Math]::Round($vy * 10)
    $turnTenths = [int][Math]::Round($turn * 10)

    $payload = "WTDL2|$Id|$alt|$vyTenths|$hdgInt|$turnTenths|$ix|$iy"
    if ($Role -gt 0) {
        # 第 9 段（坡度）留空占位，角色必须落在第 10 段 —— 和 HUD 的拼法保持一致
        $payload += "||$Role"
    }
    $bytes = [System.Text.Encoding]::ASCII.GetBytes($payload)

    try {
        [void]$udp.Send($bytes, $bytes.Length, $ep)
        $sent++
        if (-not $Quiet) {
            Write-Host ("[{0,6:F1}s] -> {1}:{2}  {3}" -f $t, $ipText, $port, $payload)
        }
    }
    catch {
        $failed++
        if (-not $Quiet) { Write-Host ("[{0,6:F1}s] send failed: {1}" -f $t, $_.Exception.Message) }
    }
}

$udp.Close()
Write-Host ""
Write-Host "fakedatalink: sent=$sent failed=$failed to $ipText`:$port"
if ($sent -eq 0) { exit 1 }
