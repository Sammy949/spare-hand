# Logs every process created during the next N seconds, with parent and command line.
param([int]$Seconds = 90, [string]$Out = "$PSScriptRoot\watch-only.txt")
$before = @{}; Get-CimInstance Win32_Process | ForEach-Object { $before[$_.ProcessId] = 1 }
"watching $Seconds s from $(Get-Date -Format HH:mm:ss)" | Set-Content $Out
$deadline = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $deadline) {
  Get-CimInstance Win32_Process | Where-Object { -not $before.ContainsKey($_.ProcessId) } | ForEach-Object {
    $before[$_.ProcessId] = 1
    $parent = (Get-CimInstance Win32_Process -Filter "ProcessId=$($_.ParentProcessId)" -ErrorAction SilentlyContinue).Name
    "$(Get-Date -Format HH:mm:ss.fff) NEW pid=$($_.ProcessId) parent=$($_.ParentProcessId)($parent) $($_.Name) :: $($_.CommandLine)" | Add-Content $Out }
  Start-Sleep -Milliseconds 150
}
"done $(Get-Date -Format HH:mm:ss)" | Add-Content $Out
