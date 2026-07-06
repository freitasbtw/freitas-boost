$ProgressPreference = 'SilentlyContinue'
$ErrorActionPreference = 'SilentlyContinue'

$deepClean = $env:FB_DEEP_CLEAN -eq '1'
$skipped = @()

# Locais seguros de cache/temporarios. NUNCA tocamos em documentos do usuario.
$targets = @(
  $env:TEMP,
  (Join-Path $env:WINDIR 'Temp'),
  (Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\INetCache'),
  (Join-Path $env:LOCALAPPDATA 'CrashDumps')
) | Where-Object { $_ } | Select-Object -Unique

if ($deepClean) {
  $targets += (Join-Path $env:WINDIR 'Prefetch')
} else {
  $skipped += 'Prefetch preservado para evitar piorar carregamentos'
}

$freed = [double]0
$count = 0
$details = @()

foreach ($path in $targets) {
  if (-not (Test-Path -LiteralPath $path)) { continue }

  $before = $freed

  Get-ChildItem -LiteralPath $path -Force -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
    $len = 0
    try { $len = [double]$_.Length } catch {}
    try {
      Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop
      $freed += $len
      $count++
    } catch {}
  }

  # Remove pastas que ficaram vazias (de baixo para cima).
  Get-ChildItem -LiteralPath $path -Force -Recurse -Directory -ErrorAction SilentlyContinue |
    Sort-Object { $_.FullName.Length } -Descending |
    ForEach-Object { try { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop } catch {} }

  $details += [pscustomobject]@{
    path    = $path
    freedMB = [math]::Round((($freed - $before) / 1MB), 1)
  }
}

# Esvaziar a Lixeira libera espaco, mas nao melhora FPS/latencia.
if ($deepClean) {
  try { Clear-RecycleBin -Force -ErrorAction Stop; $recycle = $true } catch { $recycle = $false }
} else {
  $recycle = $false
  $skipped += 'Lixeira preservada'
}

[pscustomobject]@{
  ok           = $true
  deepClean    = [bool]$deepClean
  freedBytes   = [int64]$freed
  freedMB      = [math]::Round($freed / 1MB, 1)
  filesRemoved = $count
  recycleBin   = $recycle
  skipped      = @($skipped)
  details      = @($details)
} | ConvertTo-Json -Depth 4 -Compress
