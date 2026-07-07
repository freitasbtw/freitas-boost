param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Output = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $repo ".dotnet\dotnet.exe"
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { "dotnet" }
$project = Join-Path $repo "native\src\FreitasBoost.App\FreitasBoost.App.csproj"

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $repo "artifacts\FreitasBoost-$Configuration"
}

$env:NUGET_PACKAGES = Join-Path $repo "native\.nuget-packages"
New-Item -ItemType Directory -Force -Path $Output | Out-Null

& $dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o $Output `
    /p:NuGetAudit=false

Write-Host "Publish gerado em: $Output"
