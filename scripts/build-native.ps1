param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $repo ".dotnet\dotnet.exe"
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { "dotnet" }

$env:NUGET_PACKAGES = Join-Path $repo "native\.nuget-packages"

& $dotnet restore (Join-Path $repo "native\FreitasBoost.Native.slnx") `
    /p:NuGetAudit=false `
    /p:RestoreUseStaticGraphEvaluation=true

& $dotnet build (Join-Path $repo "native\FreitasBoost.Native.slnx") `
    -c $Configuration `
    --no-restore `
    /p:NuGetAudit=false
