# Launcher for WtHud. Keeps the portable .NET runtime discoverable.
param(
    [string]$Mode = 'overlay',
    [string[]]$Args = @(),
    [int]$Seconds = 3600,
    [switch]$Demo
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dotnetRoot = Join-Path $root '.tools\dotnet'
if (Test-Path (Join-Path $dotnetRoot 'dotnet.exe')) { $env:DOTNET_ROOT = $dotnetRoot }

$exe = Join-Path $PSScriptRoot 'bin\Debug\net8.0-windows\WtHud.exe'
if (-not (Test-Path $exe)) {
    Write-Output "WtHud.exe not found; run build.ps1 first"
    exit 1
}

$all = @($Mode)
if ($Mode -eq 'overlay') {
    $all += "$Seconds"
    if ($Demo) { $all += 'demo' }
}
elseif ($Args.Count -gt 0) {
    $all += $Args
}

Write-Output ("running: WtHud.exe " + ($all -join ' '))
& $exe @all
exit $LASTEXITCODE

