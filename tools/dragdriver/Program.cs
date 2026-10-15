using System.Runtime.InteropServices;

// Usage: DragDriver <targetX> <targetY> [logPath]
// Drags a temp file from its own window to (targetX, targetY) in physical pixels, hovers, releases,
// and logs the drop effect DoDragDrop returned.
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        SetProcessDpiAwarenessContext(-4); // per-monitor v2
        var target = new Point(int.Parse(args[0]), int.Parse(args[1]));
        var logPath = args.Length > 2 ? args[2] : Path.Combine(AppContext.BaseDirectory, "driver.log");
        void Log(string m) => File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss.fff} driver: {m}{Environment.NewLine}");

        var file = Path.Combine(Path.GetTempPath(), "droptray-test.txt");
        File.WriteAllText(file, "drop tray test file");

        var form = new Form { Text = "drag source", StartPosition = FormStartPosition.Manual, Location = new Point(120, 120), Size = new Size(220, 120), TopMost = true };
        form.MouseDown += (_, _) =>
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { file });
            var effect = form.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            Log($"DoDragDrop returned {effect}");
            form.BeginInvoke(() => form.Close());
        };
        if (args.Contains("--probe-only"))
        {
            // No mouse movement: report what owns the target point, from outside the tray process.
            for (var i = 0; i < 4; i++)
            {
                var u = WindowFromPoint(target);
                var r = GetAncestor(u, 2);
                Log($"probe-only #{i} (no form): under={ClassOf(u)} root={ClassOf(r)} pid={PidOf(r)} rootTopmost={(GetWindowLongPtr(r, -20) & 0x8) != 0}");
                Thread.Sleep(300);
            }

            var term = FindWindow("CASCADIA_HOSTING_WINDOW_CLASS", null);
            Log($"terminal hwnd=0x{term:X} topmost={(GetWindowLongPtr(term, -20) & 0x8) != 0}");
            form.Shown += (_, _) =>
            {
                var u = WindowFromPoint(target);
                var r = GetAncestor(u, 2);
                Log($"probe-only (form shown): under={ClassOf(u)} root={ClassOf(r)} pid={PidOf(r)}");
                form.BeginInvoke(() => form.Close());
            };
            Application.Run(form);
            return;
        }

        form.Shown += (_, _) => new Thread(() =>
        {
            Thread.Sleep(400);
            var start = form.Invoke(() => form.PointToScreen(new Point(form.ClientSize.Width / 2, form.ClientSize.Height / 2)));
            Log($"start {start} -> target {target}");
            MoveTo(start);
            Thread.Sleep(100);
            Mouse(MOUSEEVENTF_LEFTDOWN);
            Thread.Sleep(150);
            const int steps = 40;
            for (var i = 1; i <= steps; i++)
            {
                MoveTo(new Point(start.X + ((target.X - start.X) * i / steps), start.Y + ((target.Y - start.Y) * i / steps)));
                Thread.Sleep(15);
            }

            for (var i = 0; i < 10; i++)
            {
                MoveTo(new Point(target.X + (i % 2), target.Y));
                Thread.Sleep(60);
            }

            var under = WindowFromPoint(target);
            var root = GetAncestor(under, 2);
            Log($"under target: {ClassOf(under)} root={ClassOf(root)} pid={PidOf(root)}");
            Mouse(MOUSEEVENTF_LEFTUP);
            Log("released");
        }) { IsBackground = true }.Start();
        Application.Run(form);
    }

    private const uint MOUSEEVENTF_MOVE = 0x1, MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4, MOUSEEVENTF_VIRTUALDESK = 0x4000, MOUSEEVENTF_ABSOLUTE = 0x8000;

    private static void MoveTo(Point p)
    {
        int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
        var nx = (int)Math.Round((p.X - vx) * 65535.0 / (vw - 1));
        var ny = (int)Math.Round((p.Y - vy) * 65535.0 / (vh - 1));
        Send(new INPUT { type = 0, mi = new MOUSEINPUT { dx = nx, dy = ny, dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK } });
    }

    private static void Mouse(uint flags) => Send(new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = flags } });

    private static void Send(INPUT input) => SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());

    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public nint dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public MOUSEINPUT mi; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point p);
    [DllImport("user32.dll")] private static extern long GetWindowLongPtr(nint h, int i);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string? c, string? t);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint h, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint h, System.Text.StringBuilder sb, int max);
    private static string ClassOf(nint h) { var sb = new System.Text.StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
    private static string PidOf(nint h) { GetWindowThreadProcessId(h, out var pid); try { return $"{pid}:{System.Diagnostics.Process.GetProcessById((int)pid).ProcessName}"; } catch { return pid.ToString(); } }
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(nint ctx);
}
