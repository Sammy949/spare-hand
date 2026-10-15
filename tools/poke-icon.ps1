# Sends WM_SETTINGCHANGE to Spare Hand's message-only notify-icon windows, forcing their WndProc to run.
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class M { [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr after, string cls, string title);
 [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l); }
"@
$n = 0; $h = [IntPtr]::Zero
while (($h = [M]::FindWindowEx([IntPtr](-3), $h, 'SpareHandNotifyIcon', [NullString]::Value)) -ne [IntPtr]::Zero) { [void][M]::SendMessage($h, 0x1A, [IntPtr]::Zero, [IntPtr]::Zero); $n++ }
"poked $n notify-icon window(s)"
