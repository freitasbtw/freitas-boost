$ProgressPreference = 'SilentlyContinue'
$ErrorActionPreference = 'SilentlyContinue'

$applied = @()
$stateBase = if ($env:APPDATA) { $env:APPDATA } else { $env:TEMP }
$stateDir = Join-Path $stateBase 'Freitas Boost'
$statePath = Join-Path $stateDir 'fps-mode-state.json'
$historyPath = Join-Path $stateDir 'state-history.json'

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

function Save-FpsState {
  if (Test-Path -LiteralPath $statePath) { return $false }

  New-Item -Path $stateDir -ItemType Directory -Force | Out-Null
  $state = [pscustomobject]@{
    version       = 1
    appliedAt     = (Get-Date).ToString('o')
    powerPlanGuid = Get-ActivePowerPlanGuid
    powerPlanName = Get-ActivePowerPlanName
    registry      = @(
      Get-RegState 'HKCU:\Software\Microsoft\GameBar' 'AllowAutoGameMode'
      Get-RegState 'HKCU:\Software\Microsoft\GameBar' 'AutoGameModeEnabled'
      Get-RegState 'HKCU:\System\GameConfigStore' 'GameDVR_Enabled'
    )
  }

  $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statePath -Encoding UTF8
  Add-HistoryItem $state 'automatico' 'Antes do Modo FPS'
  return $true
}

function New-StateId {
  return ((Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssfffZ') + '-' + ([guid]::NewGuid().ToString('N').Substring(0, 8)))
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

function Add-HistoryItem($State, $Source, $Label) {
  $history = Read-History
  $item = [pscustomobject]@{
    id            = New-StateId
    label         = $Label
    source        = $Source
    createdAt     = (Get-Date).ToString('o')
    powerPlanGuid = $State.powerPlanGuid
    powerPlanName = $State.powerPlanName
    registry      = @($State.registry)
  }

  $items = @($history.items)
  $items = @($item) + $items
  $history.items = @($items | Select-Object -First 25)
  Write-History $history
}

try {
  if (Save-FpsState) {
    $applied += 'Snapshot de restauracao salvo'
  } else {
    $applied += 'Snapshot anterior preservado'
  }
} catch {}

# 1) Plano de energia de Alto Desempenho (GUID padrao do Windows).
$highPerf = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'
try {
  powercfg /setactive $highPerf | Out-Null
  if ($LASTEXITCODE -eq 0) { $applied += 'Plano de energia: Alto desempenho' }
} catch {}

# 2) Modo Jogo do Windows ligado.
try {
  Set-DWordValue 'HKCU:\Software\Microsoft\GameBar' 'AllowAutoGameMode' 1
  Set-DWordValue 'HKCU:\Software\Microsoft\GameBar' 'AutoGameModeEnabled' 1
  $applied += 'Modo Jogo do Windows: ativado'
} catch {}

# 3) Game DVR (gravacao em segundo plano) desligado -> menos overhead em jogo.
try {
  Set-DWordValue 'HKCU:\System\GameConfigStore' 'GameDVR_Enabled' 0
  $applied += 'Game DVR: desativado'
} catch {}

# 4) Limpa o cache de DNS (ajuda a latencia em jogos online).
try {
  ipconfig /flushdns | Out-Null
  $applied += 'Cache DNS: limpo'
} catch {}

[pscustomobject]@{
  ok        = $true
  applied   = @($applied)
  statePath = $statePath
} | ConvertTo-Json -Compress
