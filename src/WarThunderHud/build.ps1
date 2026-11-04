# Build script for WtHud. Uses the portable .NET SDK in .tools/dotnet when present.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ROOT = Split-Path -Parent $dotnet

$proj = Join-Path $PSScriptRoot 'WarThunderHud.csproj'
& $dotnet build $proj -c Debug --nologo
exit $LASTEXITCODE

