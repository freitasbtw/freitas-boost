$ProgressPreference = 'SilentlyContinue'
$ErrorActionPreference = 'SilentlyContinue'

function Get-ActivePowerPlanName {
  $active = (powercfg /getactivescheme) -join ' '
  if ($active -match '\((.+)\)') { return $matches[1] }
  return 'Desconhecido'
}

function Get-RegValue($Path, $Name) {
  if (-not (Test-Path $Path)) { return $null }
  $item = Get-ItemProperty -Path $Path -Name $Name -ErrorAction SilentlyContinue
  if ($item -and $item.PSObject.Properties[$Name]) { return $item.$Name }
  return $null
}

function Get-SteamPath {
  $paths = @(
    'HKCU:\Software\Valve\Steam',
    'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam',
    'HKLM:\SOFTWARE\Valve\Steam'
  )

  foreach ($path in $paths) {
    $steamPath = Get-RegValue $path 'SteamPath'
    if ($steamPath -and (Test-Path -LiteralPath $steamPath)) { return $steamPath }

    $installPath = Get-RegValue $path 'InstallPath'
    if ($installPath -and (Test-Path -LiteralPath $installPath)) { return $installPath }
  }

  return $null
}

function Find-Cs2Exe($SteamPath) {
  $candidates = @()
  if ($SteamPath) {
    $candidates += Join-Path $SteamPath 'steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe'
    $libraryFile = Join-Path $SteamPath 'steamapps\libraryfolders.vdf'
    if (Test-Path -LiteralPath $libraryFile) {
      $content = Get-Content -LiteralPath $libraryFile -Raw
      $matches = [regex]::Matches($content, '"path"\s+"([^"]+)"')
      foreach ($match in $matches) {
        $libraryPath = $match.Groups[1].Value -replace '\\\\', '\'
        if ($libraryPath) {
          $candidates += Join-Path $libraryPath 'steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe'
        }
      }
    }
  }

  foreach ($candidate in $candidates | Select-Object -Unique) {
    if (Test-Path -LiteralPath $candidate) { return $candidate }
  }

  return $null
}

function Get-GpuVendor($Name) {
  $n = ([string]$Name).ToLowerInvariant()
  if ($n -match 'nvidia|geforce|rtx|gtx') { return 'nvidia' }
  if ($n -match 'amd|radeon|rx ') { return 'amd' }
  if ($n -match 'intel|arc') { return 'intel' }
  return 'unknown'
}

function New-Recommendation($Category, $Title, $Impact, $Tradeoff, $Action, $Priority) {
  [pscustomobject]@{
    category = $Category
    title    = $Title
    impact   = $Impact
    tradeoff = $Tradeoff
    action   = $Action
    priority = $Priority
  }
}

$gpu = Get-CimInstance Win32_VideoController |
  Sort-Object AdapterRAM -Descending |
  Select-Object -First 1

$gpuName = if ($gpu.Name) { $gpu.Name.Trim() } else { 'GPU desconhecida' }
$vendor = Get-GpuVendor $gpuName
$driverVersion = if ($gpu.DriverVersion) { $gpu.DriverVersion } else { 'desconhecido' }

$steamPath = Get-SteamPath
$cs2Exe = Find-Cs2Exe $steamPath
$gameMode = Get-RegValue 'HKCU:\Software\Microsoft\GameBar' 'AutoGameModeEnabled'
$gameDvr = Get-RegValue 'HKCU:\System\GameConfigStore' 'GameDVR_Enabled'
$hags = Get-RegValue 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' 'HwSchMode'

$recommendations = @()

if ($vendor -eq 'nvidia') {
  $recommendations += New-Recommendation `
    'Latencia' `
    'NVIDIA Reflex: Enabled + Boost' `
    'Melhor custo-beneficio competitivo quando a prioridade e resposta do mouse/tiro.' `
    'Pode custar alguns FPS e usar mais energia, mas a reducao de latencia costuma valer mais em CS2.' `
    'Ative no CS2 em Video > Advanced Video > NVIDIA Reflex Low Latency.' `
    1
} elseif ($vendor -eq 'amd') {
  $recommendations += New-Recommendation `
    'Latencia' `
    'AMD Anti-Lag 2 no menu do CS2' `
    'Boa troca quando o sistema esta GPU-bound ou com fila de renderizacao.' `
    'Pode mexer em 1% lows em alguns PCs; teste ligado/desligado no mesmo mapa.' `
    'Use a opcao integrada ao jogo, nunca tweaks externos que mexam no processo.' `
    1
} else {
  $recommendations += New-Recommendation `
    'Latencia' `
    'Redutor de latencia do driver/jogo' `
    'Priorize a opcao nativa do jogo ou driver da sua GPU.' `
    'Sem GPU NVIDIA/AMD detectada, o app nao deve aplicar ajuste automatico.' `
    'Valide no painel do fabricante e no menu Advanced Video do CS2.' `
    1
}

$recommendations += New-Recommendation `
  'Frame pacing' `
  'FPS maximo estavel, nao apenas FPS maximo possivel' `
  'Melhora consistencia de mira quando o frametime fica estavel.' `
  'Limitar FPS abaixo do pico pode reduzir media de FPS, mas melhora 1% low e sensacao.' `
  'Use os sliders Maximum FPS In Game e Maximum FPS In Menus do CS2.' `
  2

$recommendations += New-Recommendation `
  'Windows' `
  'HAGS: teste A/B por hardware' `
  'Pode reduzir overhead de agendamento em alguns drivers.' `
  'Tambem pode causar stutter em outros; nao e ajuste para aplicar cegamente.' `
  'Teste uma partida com HAGS ligado e outra desligado, medindo 1% low e latencia.' `
  3

$recommendations += New-Recommendation `
  'Overlays' `
  'Gravacao e overlays fora da partida competitiva' `
  'Reduz processos disputando CPU/GPU e evita hitches de captura.' `
  'Perde recursos de clipe/overlay enquanto joga.' `
  'Deixe Game DVR desligado e feche overlays que nao usa no competitivo.' `
  4

[pscustomobject]@{
  ok            = $true
  gpuName       = $gpuName
  gpuVendor     = $vendor
  driverVersion = $driverVersion
  steamPath     = $steamPath
  cs2Path       = $cs2Exe
  cs2Detected   = [bool]$cs2Exe
  powerPlan     = Get-ActivePowerPlanName
  gameMode      = if ($gameMode -eq $null) { 'desconhecido' } elseif ([int]$gameMode -eq 1) { 'ativado' } else { 'desativado' }
  gameDvr       = if ($gameDvr -eq $null) { 'desconhecido' } elseif ([int]$gameDvr -eq 0) { 'desativado' } else { 'ativado' }
  hags          = if ($hags -eq $null) { 'desconhecido' } elseif ([int]$hags -eq 2) { 'ativado' } else { 'desativado' }
  recommendations = @($recommendations | Sort-Object priority)
} | ConvertTo-Json -Depth 5 -Compress
