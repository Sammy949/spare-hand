# Lists top-level windows of a process: title, class, visibility, rect.
param([string]$Name = 'SpareHand')
Add-Type @"
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class E {
  public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(P p, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Ri,B; }
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int s);
  public static List<string> For(uint pid) { var o = new List<string>(); EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p);
    if (p == pid) { var t = new StringBuilder(256); GetWindowText(h,t,256); var c = new StringBuilder(256); GetClassName(h,c,256); R r; GetWindowRect(h, out r); int cl; DwmGetWindowAttribute(h,14,out cl,4);
      o.Add(String.Format("[{0}] {1} visible={2} cloaked={3} rect={4},{5} {6}x{7}", t, c, IsWindowVisible(h), cl, r.L, r.T, r.Ri-r.L, r.B-r.T)); } return true; }, IntPtr.Zero); return o; } }
"@
foreach ($p in Get-Process $Name -ErrorAction SilentlyContinue) { [E]::For([uint32]$p.Id) }
