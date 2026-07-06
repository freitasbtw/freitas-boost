$ProgressPreference = 'SilentlyContinue'
$ErrorActionPreference = 'SilentlyContinue'

# Mesma lista de protecao do list-processes: barreira de seguranca caso o
# renderer envie um nome critico por engano.
$protected = @(
  'System', 'Idle', 'Registry', 'MemCompression', 'smss', 'csrss', 'wininit',
  'winlogon', 'services', 'lsass', 'svchost', 'fontdrvhost', 'dwm', 'explorer',
  'spoolsv', 'SearchHost', 'SearchIndexer', 'ShellExperienceHost',
  'StartMenuExperienceHost', 'sihost', 'ctfmon', 'RuntimeBroker', 'dllhost',
  'conhost', 'WmiPrvSE', 'audiodg', 'taskhostw', 'SecurityHealthService',
  'SecurityHealthSystray', 'MsMpEng', 'NisSrv', 'powershell', 'pwsh',
  'electron', 'Freitas Boost', 'freitas-boost', 'TextInputHost', 'LockApp',
  'wlanext', 'WUDFHost', 'steam', 'steamwebhelper', 'cs2'
)

$items = @()
if ($env:FB_ITEMS) {
  try { $items = @($env:FB_ITEMS | ConvertFrom-Json) } catch { $items = @() }
} elseif ($env:FB_NAMES) {
  try {
    $items = @($env:FB_NAMES | ConvertFrom-Json | ForEach-Object {
      [pscustomobject]@{ name = $_; pids = @() }
    })
  } catch { $items = @() }
}

$killed = @()
$failed = @()

foreach ($item in $items) {
  $name = [string]$item.name
  if (-not $name) { continue }
  if ($protected -contains $name) { continue }

  $pids = @($item.pids | Where-Object { $_ } | ForEach-Object { [int]$_ })
  if ($pids.Count -eq 0) {
    $pids = @(Get-Process -Name $name -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
  }

  foreach ($pid in $pids) {
    $proc = Get-Process -Id $pid -ErrorAction SilentlyContinue
    if (-not $proc) { continue }
    if ($protected -contains $proc.ProcessName) { continue }
    if ($name -and ($proc.ProcessName -ne $name)) {
      $failed += "$name (PID $pid mudou)"
      continue
    }

    try {
      Stop-Process -Id $pid -Force -ErrorAction Stop
      $killed += [pscustomobject]@{
        name = $name
        pid  = $pid
      }
    } catch {
      $failed += "$name (PID $pid)"
    }
  }
}

[pscustomobject]@{
  ok     = $true
  killed = @($killed)
  failed = @($failed)
} | ConvertTo-Json -Depth 4 -Compress
