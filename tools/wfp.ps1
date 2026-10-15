# Third-process probe: which top-level window owns point (X,Y)? Optionally show a plain topmost WinForms control window first.
param([int]$X, [int]$Y, [switch]$Control)
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class W {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [StructLayout(LayoutKind.Sequential)] public struct P { public int X, Y; }
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(P p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int m);
  public static string At(int x, int y) { var h = GetAncestor(WindowFromPoint(new P{X=x,Y=y}), 2); var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
}
"@
[W]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
if ($Control) {
  $f = New-Object System.Windows.Forms.Form
  $f.StartPosition = 'Manual'; $f.Location = New-Object System.Drawing.Point(($X - 50), ($Y - 50)); $f.Size = New-Object System.Drawing.Size(100, 100)
  $f.TopMost = $true; $f.ShowInTaskbar = $false; $f.FormBorderStyle = 'None'
  $f.Show(); [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 300
  "control window up: under($X,$Y) = $([W]::At($X,$Y))"
  $f.Close()
} else { "under($X,$Y) = $([W]::At($X,$Y))" }
