# Runs the probe's Invoke while logging every process created in the meantime (with command lines).
$before = @{}; Get-CimInstance Win32_Process | ForEach-Object { $before[$_.ProcessId] = 1 }
$p = Start-Process -FilePath "$PSScriptRoot\bin\Release\net10.0-windows\ShareProbe.exe" -ArgumentList 'AD54D7CA-052D-4730-B0F2-2BA34D3438DC','--invoke',"$PSScriptRoot\probe-file.txt" -NoNewWindow -PassThru -RedirectStandardOutput "$PSScriptRoot\probe-out.txt"
$deadline = (Get-Date).AddSeconds(12); $seen = @{}
while ((Get-Date) -lt $deadline) {
  Get-CimInstance Win32_Process | Where-Object { -not $before.ContainsKey($_.ProcessId) -and -not $seen.ContainsKey($_.ProcessId) } | ForEach-Object {
    $seen[$_.ProcessId] = 1; "NEW pid=$($_.ProcessId) parent=$($_.ParentProcessId) $($_.Name) :: $($_.CommandLine)" }
  Start-Sleep -Milliseconds 200
}
"--- probe output"; Get-Content "$PSScriptRoot\probe-out.txt"
"--- quick share processes now"; Get-CimInstance Win32_Process -Filter "Name like 'nearby%'" | ForEach-Object { "$($_.ProcessId) $($_.Name) :: $($_.CommandLine)" }
