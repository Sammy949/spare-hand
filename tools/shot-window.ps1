# Screenshot a top-level window by exact title. Usage: shot-window.ps1 -Title "..." -Out file.png
param([string]$Title, [string]$Out)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W { [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
 [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Ri,B; }
 [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out R r, int s);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); }
"@
[W]::SetProcessDPIAware() | Out-Null
$h = [W]::FindWindow([NullString]::Value, $Title); if ($h -eq [IntPtr]::Zero) { "window not found"; exit 1 }
$r = New-Object W+R; [void][W]::DwmGetWindowAttribute($h, 9, [ref]$r, 16)   # DWMWA_EXTENDED_FRAME_BOUNDS
$b = New-Object System.Drawing.Bitmap ($r.Ri-$r.L), ($r.B-$r.T); [System.Drawing.Graphics]::FromImage($b).CopyFromScreen($r.L, $r.T, 0, 0, $b.Size); $b.Save($Out)
"captured $($r.L),$($r.T) $($r.Ri-$r.L)x$($r.B-$r.T)"
