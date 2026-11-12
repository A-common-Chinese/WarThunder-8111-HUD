# WtHud 数据链一键联调
#
# 一条命令同时开两个窗口：一个收、一个发。两端各跑一次，双向就都验完了。
# 省掉三件最容易出错的事：cd 到脚本目录、占位符尖括号、ip:port 写错格式。
#
# 用法：
#   .\datalink-test.ps1 -Peer 16.12.7.9              # 测 120 秒
#   .\datalink-test.ps1 -Peer 16.12.7.9 -Seconds 300 # 五分钟稳定性
#   .\datalink-test.ps1 -Peer 16.12.7.9 -DryRun      # 只打印命令，不开窗口
#
# ★ 发送端不占 48017，所以收和发能在同一台机器上同时跑。
# ★ 两端都跑这个脚本，A→B 和 B→A 一轮同时验完。

param(
    [Parameter(Mandatory = $true)][string]$Peer,
    [int]$Seconds = 120,
    [int]$Port = 48017,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$dir = $PSScriptRoot

# ---- 1. 把 -Peer 洗成纯 IP ----
$peerText = $Peer.Trim()

if ($peerText.Contains("<") -or $peerText.Contains(">")) {
    Write-Host "看起来尖括号一起抄进来了：$peerText" -ForegroundColor Red
    Write-Host ("尖括号是占位符，不是地址的一部分。正确写法： -Peer " + $peerText.Trim("<", ">").Trim())
    exit 2
}

# 允许直接粘 ip:port
$colon = $peerText.LastIndexOf(":")
if ($colon -gt 0) {
    $maybePort = 0
    if ([int]::TryParse($peerText.Substring($colon + 1), [ref]$maybePort) -and
        $maybePort -gt 0 -and $maybePort -le 65535) {
        $peerText = $peerText.Substring(0, $colon)
        $Port = $maybePort
    }
}

$peerIp = $null
if (-not [System.Net.IPAddress]::TryParse($peerText, [ref]$peerIp)) {
    Write-Host "不是合法 IP：$peerText" -ForegroundColor Red
    Write-Host "填对方的蒲公英虚拟 IP，形如 16.12.7.9（不带尖括号、不带端口也行）"
    exit 2
}

foreach ($f in @("fakedatalink.ps1", "fakedatalink-recv.ps1")) {
    if (-not (Test-Path (Join-Path $dir $f))) {
        Write-Host "缺少脚本：$f（应该和本脚本放在同一个文件夹里）" -ForegroundColor Red
        exit 2
    }
}

# ---- 2. 顺手把本机地址列出来，方便和对方核对 ----
Write-Host "本机 IPv4："
Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -ne "127.0.0.1" } |
    Sort-Object InterfaceAlias |
    ForEach-Object {
        # 认蒲公英不靠网段：实测 OrayBoxVpnEnt 给的是 172.16.1.x，
        # 和 VMware 的 192.168.x 一样是私有地址，只能看网卡名。
        $mark = if ($_.InterfaceAlias -match "Oray|蒲公英|Pgy|VpnEnt") { "  <- 蒲公英虚拟网卡" } else { "" }
        Write-Host ("  {0,-16} {1}{2}" -f $_.IPAddress, $_.InterfaceAlias, $mark)
    }

Write-Host ""
Write-Host "对端：$($peerIp.ToString()):$Port   时长：${Seconds}s"
Write-Host ""

# ---- 3. 开两个窗口 ----
# ★ 只传文件名、用 -WorkingDirectory 指定目录：脚本可能放在带中文的路径下
#   （如「桌面\WtHud-数据链联调」），把中文路径塞进命令行容易被代码页搞坏。
$recvArgs = @("-NoExit", "-ExecutionPolicy", "Bypass", "-File", ".\fakedatalink-recv.ps1",
              "-Seconds", $Seconds, "-Port", $Port)
$sendArgs = @("-NoExit", "-ExecutionPolicy", "Bypass", "-File", ".\fakedatalink.ps1",
              "-Target", "$($peerIp.ToString()):$Port", "-Seconds", $Seconds)

if ($DryRun) {
    Write-Host "recv: cd `"$dir`"; powershell $($recvArgs -join ' ')"
    Write-Host "send: cd `"$dir`"; powershell $($sendArgs -join ' ')"
    exit 0
}

try {
    $recv = Start-Process powershell -ArgumentList $recvArgs -WorkingDirectory $dir -PassThru
    Start-Sleep -Milliseconds 400
    $send = Start-Process powershell -ArgumentList $sendArgs -WorkingDirectory $dir -PassThru
} catch {
    Write-Host "开窗口失败：$($_.Exception.Message)" -ForegroundColor Red
    Write-Host "退回到手动方式：两个窗口分别跑 fakedatalink-recv.ps1 和 fakedatalink.ps1"
    exit 2
}

Write-Host "已开两个窗口："
Write-Host ("  收  pid={0}" -f $recv.Id)
Write-Host ("  发  pid={0}" -f $send.Id)
Write-Host ""
Write-Host "看什么：收的窗口每 0.25 s 刷一行 id=777777 -> 通；"
Write-Host "        跑完各打印 result: rx=... bad=... nodes=...，两边都要 rx>=85%。"
Write-Host "        ${Seconds}s 后窗口不会自己关（留着看结果），看完直接关掉。"
Write-Host ""
Write-Host "★ 这两个窗口开着时不能再开 HUD / dlprobe，它们抢同一个 48017。"
