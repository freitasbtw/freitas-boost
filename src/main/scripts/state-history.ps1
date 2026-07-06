$ProgressPreference = 'SilentlyContinue'
$ErrorActionPreference = 'SilentlyContinue'

$stateBase = if ($env:APPDATA) { $env:APPDATA } else { $env:TEMP }
$stateDir = Join-Path $stateBase 'Freitas Boost'
$historyPath = Join-Path $stateDir 'state-history.json'
$action = if ($env:FB_ACTION) { $env:FB_ACTION.ToLowerInvariant() } else { 'list' }

function Get-ActivePowerPlanGuid {
  $active = (powercfg /getactivescheme) -join ' '
  if ($active -match '([0-9a-fA-F-]{36})') { return $matches[1] }
  return $null
}

function Get-ActivePowerPlanName {
  $active = (powercfg /getactivescheme) -join ' '
  if ($active -match '\((.+)\)') { return $matches[1] }
  return 'Desconhecido'
}

function Get-RegState($Path, $Name) {
  $value = $null
  $exists = $false
  if (Test-Path $Path) {
    $item = Get-ItemProperty -Path $Path -Name $Name -ErrorAction SilentlyContinue
    if ($item -and $item.PSObject.Properties[$Name]) {
      $exists = $true
      $value = $item.$Name
    }
  }

  [pscustomobject]@{
    path   = $Path
    name   = $Name
    exists = [bool]$exists
    value  = $value
    type   = 'DWord'
  }
}

function Capture-State($Label, $Source) {
  [pscustomobject]@{
    id            = ((Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssfffZ') + '-' + ([guid]::NewGuid().ToString('N').Substring(0, 8)))
    label         = if ($Label) { $Label } else { 'Estado manual' }
    source        = if ($Source) { $Source } else { 'manual' }
    createdAt     = (Get-Date).ToString('o')
    powerPlanGuid = Get-ActivePowerPlanGuid
    powerPlanName = Get-ActivePowerPlanName
    registry      = @(
      Get-RegState 'HKCU:\Software\Microsoft\GameBar' 'AllowAutoGameMode'
      Get-RegState 'HKCU:\Software\Microsoft\GameBar' 'AutoGameModeEnabled'
      Get-RegState 'HKCU:\System\GameConfigStore' 'GameDVR_Enabled'
    )
  }
}

function Read-History {
  if (-not (Test-Path -LiteralPath $historyPath)) {
    return [pscustomobject]@{
      version = 1
      items   = @()
    }
  }

  try {
    $history = Get-Content -LiteralPath $historyPath -Raw | ConvertFrom-Json
    if (-not $history.items) { $history | Add-Member -NotePropertyName items -NotePropertyValue @() -Force }
    return $history
  } catch {
    return [pscustomobject]@{
      version = 1
      items   = @()
    }
  }
}

function Write-History($History) {
  New-Item -Path $stateDir -ItemType Directory -Force | Out-Null
  $History | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $historyPath -Encoding UTF8
}

function Add-HistoryItem($Item) {
  $history = Read-History
  $items = @($history.items)
  $items = @($Item) + $items
  $history.items = @($items | Select-Object -First 25)
  Write-History $history
  return $history
}

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
    return "$name restaurado"
  }

  if (Test-Path $path) {
    Remove-ItemProperty -Path $path -Name $name -ErrorAction SilentlyContinue
  }
  return "$name removido"
}

function Restore-State($Id) {
  $history = Read-History
  $item = @($history.items) | Where-Object { $_.id -eq $Id } | Select-Object -First 1
  if (-not $item) {
    return [pscustomobject]@{
      ok      = $false
      error   = 'Estado nao encontrado'
      history = $history
    }
  }

  $restored = @()
  if ($item.powerPlanGuid) {
    powercfg /setactive ([string]$item.powerPlanGuid) | Out-Null
    if ($LASTEXITCODE -eq 0) { $restored += 'Plano de energia restaurado' }
  }

  foreach ($entry in @($item.registry)) {
    $msg = Restore-RegValue $entry
    if ($msg) { $restored += $msg }
  }

  [pscustomobject]@{
    ok       = $true
    item     = $item
    restored = @($restored)
    history  = $history
  }
}

function Remove-HistoryItem($Id) {
  $history = Read-History
  $before = @($history.items).Count
  $history.items = @($history.items | Where-Object { $_.id -ne $Id })
  Write-History $history
  [pscustomobject]@{
    ok      = $true
    removed = ($before -ne @($history.items).Count)
    history = $history
  }
}

switch ($action) {
  'capture' {
    $label = if ($env:FB_LABEL) { $env:FB_LABEL } else { 'Estado manual' }
    $item = Capture-State $label 'manual'
    $history = Add-HistoryItem $item
    [pscustomobject]@{
      ok      = $true
      item    = $item
      history = $history
      path    = $historyPath
    } | ConvertTo-Json -Depth 8 -Compress
  }
  'restore' {
    Restore-State $env:FB_ID | ConvertTo-Json -Depth 8 -Compress
  }
  'delete' {
    Remove-HistoryItem $env:FB_ID | ConvertTo-Json -Depth 8 -Compress
  }
  default {
    $history = Read-History
    [pscustomobject]@{
      ok      = $true
      history = $history
      path    = $historyPath
      current = (Capture-State 'Estado atual' 'current')
    } | ConvertTo-Json -Depth 8 -Compress
  }
}
