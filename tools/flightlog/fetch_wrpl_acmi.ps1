<#
.SYNOPSIS
    下载 wrpl-acmi.exe（War Thunder .wrpl -> ACMI 转换器）到 FlightLog 工具目录。

.DESCRIPTION
    来源: https://github.com/3794432/wrpl-acmi/releases   （AGPL-3.0）
    实测版本: wrpl-acmi v3.0.0-20261101175226-9ed43b9f108b（ACMI Comments 里自报）
    文件大小: 15784448 字节（约 15.1 MB）
    SHA256  : C80D9C5C04531DADC27342788F9800A440A6F258076054DABFB6F9904E71890E

    本机访问 GitHub 直连会被 Steam++（Watt Toolkit）的加速规则拦，脚本默认
    先试直连、失败后自动改用 gh-proxy 镜像:
        https://gh-proxy.com/https://github.com/.../releases/latest/download/wrpl-acmi.exe

.PARAMETER Dest
    目标目录（默认：本脚本所在目录，即 tools\flightlog）或完整的 .exe 路径。

.PARAMETER Url
    手动指定下载地址（给了就只用这个地址，不再尝试直连/镜像）。

.PARAMETER Force
    目标已存在也重新下载。

.PARAMETER CheckOnly
    只检查本地文件（大小/SHA256），不联网。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\fetch_wrpl_acmi.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\fetch_wrpl_acmi.ps1 -Dest D:\WtHud-发布 -Force
#>
[CmdletBinding()]
param(
    [string]$Dest = '',
    [string]$Url = '',
    [switch]$Force,
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'

# ---- 期望值（换新版本时更新这三行）----
$ExpectedVersion = 'v3.0.0-20261101175226-9ed43b9f108b'
$ExpectedSize    = 15784448
$ExpectedSha256  = 'C80D9C5C04531DADC27342788F9800A440A6F258076054DABFB6F9904E71890E'

$DirectUrl  = 'https://github.com/3794432/wrpl-acmi/releases/latest/download/wrpl-acmi.exe'
$MirrorUrls = @(
    'https://gh-proxy.com/https://github.com/3794432/wrpl-acmi/releases/latest/download/wrpl-acmi.exe',
    'https://ghfast.top/https://github.com/3794432/wrpl-acmi/releases/latest/download/wrpl-acmi.exe'
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
if ([string]::IsNullOrWhiteSpace($Dest)) {
    $DestDir = $ScriptDir
    $Target  = Join-Path $ScriptDir 'wrpl-acmi.exe'
} elseif ($Dest.ToLower().EndsWith('.exe')) {
    $Target  = $Dest
    $DestDir = Split-Path -Parent $Dest
} else {
    $DestDir = $Dest
    $Target  = Join-Path $Dest 'wrpl-acmi.exe'
}
if ([string]::IsNullOrWhiteSpace($DestDir)) { $DestDir = $ScriptDir }
if (-not (Test-Path -LiteralPath $DestDir)) {
    New-Item -ItemType Directory -Force -Path $DestDir | Out-Null
}

Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' wrpl-acmi.exe 下载器（War Thunder .wrpl -> ACMI 转换器）'   -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host (" 来源      : https://github.com/3794432/wrpl-acmi/releases")
Write-Host (" 许可证    : AGPL-3.0")
Write-Host (" 期望版本  : {0}" -f $ExpectedVersion)
Write-Host (" 期望大小  : {0} 字节（约 {1:N1} MB）" -f $ExpectedSize, ($ExpectedSize / 1MB))
Write-Host (" SHA256    : {0}" -f $ExpectedSha256)
Write-Host (" 目标文件  : {0}" -f $Target)
Write-Host ''

function Get-Sha256([string]$Path) {
    try {
        return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
    } catch {
        return ''
    }
}

function Show-LocalStatus {
    if (-not (Test-Path -LiteralPath $Target)) {
        Write-Host ' 本地状态  : 未找到 wrpl-acmi.exe' -ForegroundColor Yellow
        return $false
    }
    $item = Get-Item -LiteralPath $Target
    $hash = Get-Sha256 $Target
    $sizeText = '{0} 字节（约 {1:N1} MB）' -f $item.Length, ($item.Length / 1MB)
    Write-Host (" 本地状态  : 已存在，{0}" -f $sizeText)
    Write-Host (" 本地 SHA256: {0}" -f $hash)
    if ($hash -eq $ExpectedSha256.ToUpperInvariant()) {
        Write-Host ' 校验结果  : 与期望版本一致 ✔' -ForegroundColor Green
        return $true
    }
    Write-Host ' 校验结果  : 与期望版本不一致（可能是更新的版本，或文件损坏）' -ForegroundColor Yellow
    return ($item.Length -gt 10MB)
}

$localOk = Show-LocalStatus
Write-Host ''

if ($CheckOnly) {
    if ($localOk) { exit 0 } else { exit 1 }
}
if ($localOk -and -not $Force) {
    Write-Host '已是最新/可用文件，跳过下载（加 -Force 可强制重新下载）。' -ForegroundColor Green
    exit 0
}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls11
$ProgressPreference = 'SilentlyContinue'

$candidates = @()
if (-not [string]::IsNullOrWhiteSpace($Url)) {
    $candidates = @($Url)
    Write-Host ' 使用手动指定的地址，不再尝试直连/镜像。' -ForegroundColor Yellow
} else {
    $candidates = @($DirectUrl) + $MirrorUrls
}

$temp = Join-Path $DestDir ('wrpl-acmi.download.' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.exe.part')
$downloaded = $null
foreach ($candidate in $candidates) {
    Write-Host (" 尝试下载  : {0}" -f $candidate) -ForegroundColor Cyan
    try {
        if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
        $sw = [Diagnostics.Stopwatch]::StartNew()
        Invoke-WebRequest -Uri $candidate -OutFile $temp -UseBasicParsing -TimeoutSec 300
        $sw.Stop()
        $len = (Get-Item -LiteralPath $temp).Length
        if ($len -lt 10MB) {
            throw ("下载结果只有 {0} 字节，不像是 exe" -f $len)
        }
        Write-Host ("            成功，{0:N1} MB，耗时 {1:N1} s" -f ($len / 1MB), $sw.Elapsed.TotalSeconds) -ForegroundColor Green
        $downloaded = $candidate
        break
    } catch {
        Write-Host ("            失败: {0}" -f $_.Exception.Message) -ForegroundColor DarkYellow
        if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue }
    }
}

if ($null -eq $downloaded) {
    Write-Host ''
    Write-Host '下载失败：直连和镜像都不可用。' -ForegroundColor Red
    Write-Host '可以手动下载后放到本目录：' -ForegroundColor Yellow
    Write-Host ("  {0}" -f $DirectUrl)
    Write-Host ("  {0}" -f $MirrorUrls[0])
    exit 1
}

$newHash = Get-Sha256 $temp
$newSize = (Get-Item -LiteralPath $temp).Length
Write-Host ''
Write-Host (" 下载来源  : {0}" -f $downloaded)
Write-Host (" 下载大小  : {0} 字节（约 {1:N1} MB）" -f $newSize, ($newSize / 1MB))
Write-Host (" 下载 SHA256: {0}" -f $newHash)
if ($newHash -ne $ExpectedSha256.ToUpperInvariant() -or $newSize -ne $ExpectedSize) {
    Write-Host ' 注意      : 与脚本内置的期望版本不一致（上游可能发不了新版本）' -ForegroundColor Yellow
    Write-Host '             已按实际下载内容保存；如不放心可手动核对发布页。' -ForegroundColor Yellow
} else {
    Write-Host ' 校验结果  : 与期望版本一致 ✔' -ForegroundColor Green
}

if (Test-Path -LiteralPath $Target) {
    Remove-Item -LiteralPath $Target -Force
}
Move-Item -LiteralPath $temp -Destination $Target -Force
Write-Host ''
Write-Host (" 已保存到  : {0}" -f $Target) -ForegroundColor Green
Write-Host ' 下一步    : 让 wrpl-acmi.exe 和 FlightLog.exe 放在同一目录，' -ForegroundColor Green
Write-Host '             然后 python -m flightlog <回放.wrpl> --list 试试。' -ForegroundColor Green
exit 0
