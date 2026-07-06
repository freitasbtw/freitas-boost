$ProgressPreference = 'SilentlyContinue'
$ErrorActionPreference = 'SilentlyContinue'

$os = Get-CimInstance Win32_OperatingSystem
$totalKB = [double]$os.TotalVisibleMemorySize
$freeKB = [double]$os.FreePhysicalMemory

$cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name
if (-not $cpu) { $cpu = 'Processador desconhecido' }

$active = (powercfg /getactivescheme) -join ' '
if ($active -match '\((.+)\)') { $planName = $matches[1] } else { $planName = 'Desconhecido' }

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

[pscustomobject]@{
  ok        = $true
  totalMB   = [math]::Round($totalKB / 1024)
  freeMB    = [math]::Round($freeKB / 1024)
  usedMB    = [math]::Round(($totalKB - $freeKB) / 1024)
  usedPct   = [math]::Round((($totalKB - $freeKB) / $totalKB) * 100)
  cpu       = $cpu.Trim()
  powerPlan = $planName
  isAdmin   = [bool]$isAdmin
} | ConvertTo-Json -Compress
