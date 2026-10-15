# Memory figures for a process: Task Manager's "Memory" column is the private working set.
param([string]$Name = 'SpareHand', [switch]$Trim)
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class T { [DllImport("psapi.dll")] public static extern bool EmptyWorkingSet(IntPtr h); }
"@
$p = Get-Process $Name
if ($Trim) { [void][T]::EmptyWorkingSet($p.Handle); Start-Sleep -Milliseconds 500; $p.Refresh() }
$pws = (Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -Filter "IDProcess=$($p.Id)").WorkingSetPrivate
'private working set (Task Manager): {0,5:N0} MB | working set: {1,5:N0} MB | private bytes: {2,5:N0} MB' -f ($pws/1MB), ($p.WorkingSet64/1MB), ($p.PrivateMemorySize64/1MB)
