$ProgressPreference = 'SilentlyContinue'
$ErrorActionPreference = 'SilentlyContinue'

# Usa a API EmptyWorkingSet do Windows para reduzir o working set dos
# processos, devolvendo memoria fisica ao sistema. Tecnica nativa e segura
# (nao mata processos, apenas pede que liberem paginas nao usadas).
$signature = @'
using System;
using System.Runtime.InteropServices;
public static class FBMem {
  [DllImport("psapi.dll")]
  public static extern int EmptyWorkingSet(IntPtr hwProc);
}
'@
Add-Type -TypeDefinition $signature -ErrorAction SilentlyContinue

$os = Get-CimInstance Win32_OperatingSystem
$totalMB = [math]::Round($os.TotalVisibleMemorySize / 1024)
$beforeUsedMB = [math]::Round(($os.TotalVisibleMemorySize - $os.FreePhysicalMemory) / 1024)

$trimmed = 0
foreach ($proc in Get-Process) {
  try {
    [void][FBMem]::EmptyWorkingSet($proc.Handle)
    $trimmed++
  } catch {}
}

Start-Sleep -Milliseconds 500

$os2 = Get-CimInstance Win32_OperatingSystem
$afterUsedMB = [math]::Round(($os2.TotalVisibleMemorySize - $os2.FreePhysicalMemory) / 1024)
$freedMB = [math]::Max(0, $beforeUsedMB - $afterUsedMB)

[pscustomobject]@{
  ok               = $true
  totalMB          = $totalMB
  beforeUsedMB     = $beforeUsedMB
  afterUsedMB      = $afterUsedMB
  freedMB          = $freedMB
  processesTrimmed = $trimmed
} | ConvertTo-Json -Compress
