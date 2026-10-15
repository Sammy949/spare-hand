# Sends the notify icon the callback Windows sends on a left-click (NOTIFYICON_VERSION_4: LOWORD(lParam) = event).
param([int]$Event = 0x0400)  # NIN_SELECT; 0x0202 = WM_LBUTTONUP
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class M { [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr after, string cls, string title);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l); }
"@
$h = [M]::FindWindowEx([IntPtr](-3), [IntPtr]::Zero, 'SpareHandNotifyIcon', [NullString]::Value)
if ($h -eq [IntPtr]::Zero) { 'notify icon window not found'; exit 1 }
[void][M]::PostMessage($h, 0x8001, [IntPtr]::Zero, [IntPtr]((1 -shl 16) -bor $Event)); "sent event 0x{0:X}" -f $Event
