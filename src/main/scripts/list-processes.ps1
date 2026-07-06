$ProgressPreference = 'SilentlyContinue'
$ErrorActionPreference = 'SilentlyContinue'

# Processos criticos do sistema (ou do proprio app) que NUNCA aparecem
# como candidatos a encerramento.
$protected = @(
  'System', 'Idle', 'Registry', 'MemCompression', 'smss', 'csrss', 'wininit',
  'winlogon', 'services', 'lsass', 'svchost', 'fontdrvhost', 'dwm', 'explorer',
  'spoolsv', 'SearchHost', 'SearchIndexer', 'ShellExperienceHost',
  'StartMenuExperienceHost', 'sihost', 'ctfmon', 'RuntimeBroker', 'dllhost',
  'conhost', 'WmiPrvSE', 'audiodg', 'taskhostw', 'SecurityHealthService',
  'SecurityHealthSystray', 'MsMpEng', 'NisSrv', 'powershell', 'pwsh',
  'electron', 'Freitas Boost', 'freitas-boost', 'TextInputHost', 'LockApp',
  'csrss', 'wlanext', 'WUDFHost', 'steam', 'steamwebhelper', 'cs2'
)

$grouped = Get-Process -ErrorAction SilentlyContinue |
  Where-Object { $protected -notcontains $_.ProcessName } |
  Group-Object ProcessName |
  ForEach-Object {
    $memMB = [math]::Round((($_.Group | Measure-Object WorkingSet64 -Sum).Sum) / 1MB, 1)
    $hasWindow = [bool]($_.Group | Where-Object { $_.MainWindowHandle -ne 0 })
    [pscustomobject]@{
      name      = $_.Name
      memMB     = $memMB
      count     = $_.Count
      pids      = @($_.Group | Select-Object -ExpandProperty Id)
      hasWindow = $hasWindow
    }
  } |
  Where-Object { $_.memMB -ge 25 } |
  Sort-Object memMB -Descending |
  Select-Object -First 25

[pscustomobject]@{
  ok        = $true
  processes = @($grouped)
} | ConvertTo-Json -Depth 4 -Compress
