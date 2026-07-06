param(
  [switch]$NoElevate
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$electronCmd = Join-Path $root 'node_modules\.bin\electron.cmd'

if (-not (Test-Path -LiteralPath $electronCmd)) {
  throw 'Electron nao encontrado. Rode npm install antes de iniciar o app.'
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]$identity
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if ($NoElevate -or $isAdmin) {
  Push-Location $root
  try {
    & $electronCmd $root
    exit $LASTEXITCODE
  } finally {
    Pop-Location
  }
}

$cmdArgs = "/d /s /c `"`"$electronCmd`" `"$root`"`""
Start-Process -FilePath $env:ComSpec `
  -ArgumentList $cmdArgs `
  -WorkingDirectory $root `
  -Verb RunAs `
  -WindowStyle Hidden

Write-Host 'Freitas Boost solicitado em modo administrador.'
