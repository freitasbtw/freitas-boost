$ProgressPreference = 'SilentlyContinue'
$ErrorActionPreference = 'SilentlyContinue'

$restored = @()
$stateBase = if ($env:APPDATA) { $env:APPDATA } else { $env:TEMP }
$statePath = Join-Path (Join-Path $stateBase 'Freitas Boost') 'fps-mode-state.json'
$stateUsed = $false

function Set-DWordValue($Path, $Name, $Value) {
  if (-not (Test-Path $Path)) {
    New-Item -Path $Path -Force | Out-Null
  }
  $current = Get-ItemProperty -Path $Path -Name $Name -ErrorAction SilentlyContinue
  if ($current -and $current.PSObject.Properties[$Name]) {
    Set-ItemProperty -Path $Path -Name $Name -Value $Value
  } else {
    New-ItemProperty -Path $Path -Name $Name -Value $Value -PropertyType DWord -Force | Out-Null
  }
}

function Restore-RegValue($Entry) {
  if (-not $Entry) { return }

  $path = [string]$Entry.path
  $name = [string]$Entry.name
  if (-not $path -or -not $name) { return }

  if ([bool]$Entry.exists) {
    Set-DWordValue $path $name ([int]$Entry.value)
    $script:restored += "$name`: restaurado"
  } else {
    if (Test-Path $path) {
      Remove-ItemProperty -Path $path -Name $name -ErrorAction SilentlyContinue
    }
    $script:restored += "$name`: removido"
  }
}

if (Test-Path -LiteralPath $statePath) {
  try {
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $stateUsed = $true

    if ($state.powerPlanGuid) {
      powercfg /setactive ([string]$state.powerPlanGuid) | Out-Null
      if ($LASTEXITCODE -eq 0) { $restored += 'Plano de energia anterior restaurado' }
    }

    foreach ($entry in @($state.registry)) {
      Restore-RegValue $entry
    }

    Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue
  } catch {}
}

if (-not $stateUsed) {
  # Fallback para instalacoes antigas sem snapshot salvo.
  $balanced = '381b4222-f694-41f0-9685-ff5bb260df2e'
  try {
    powercfg /setactive $balanced | Out-Null
    if ($LASTEXITCODE -eq 0) { $restored += 'Plano de energia: Equilibrado (fallback)' }
  } catch {}

  try {
    Set-DWordValue 'HKCU:\System\GameConfigStore' 'GameDVR_Enabled' 1
    $restored += 'Game DVR: reativado (fallback)'
  } catch {}
}

[pscustomobject]@{
  ok        = $true
  restored  = @($restored)
  stateUsed = [bool]$stateUsed
} | ConvertTo-Json -Compress
