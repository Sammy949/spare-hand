# Lists visible top-level windows from the top of the z-order down, until it reaches the given classes.
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text; using System.Collections.Generic;
public static class Z {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EP p, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern long GetWindowLongPtr(IntPtr h, int i);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Ri, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int s);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int m);
  public static List<string> List() {
    var o = new List<string>(); int i = 0;
    EnumWindows((h, l) => {
      if (IsWindowVisible(h)) {
        var s = new StringBuilder(256); GetClassName(h, s, 256); R r; GetWindowRect(h, out r); int c; DwmGetWindowAttribute(h, 14, out c, 4); uint pid; GetWindowThreadProcessId(h, out pid);
        long ex = GetWindowLongPtr(h, -20);
        string pn = ""; try { pn = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch {}
        o.Add(String.Format("#{0,-3} {1,-40} {2,-18} top={3} cloaked={4} ex=0x{5:X} rect={6},{7} {8}x{9}", i, s, pn, (ex & 8) != 0, c, ex, r.L, r.T, r.Ri - r.L, r.B - r.T));
      }
      i++; return true; }, IntPtr.Zero);
    return o;
  }
}
"@
[Z]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
[Z]::List() | Select-Object -First 30
