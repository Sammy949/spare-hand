using static SpareHand.Native.Win32;

namespace SpareHand.Tray;

/// <summary>
/// Where a card goes near the cursor: never under it. Above, else below, else right/left, clamped to the
/// work area of the cursor's monitor (docs/design.md). All values are physical pixels.
/// </summary>
internal static class Placement
{
    public static double ScaleAt(POINT point)
    {
        var mon = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
        GetDpiForMonitor(mon, 0, out var dpi, out _);
        return dpi / 96.0;
    }

    public static (int X, int Y, string Side) NearCursor(POINT cursor, int cardW, int cardH, int gap)
    {
        var mon = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(mon, ref mi);
        var work = mi.rcWork;

        bool Fits(int x, int y) => x >= work.Left && y >= work.Top && x + cardW <= work.Right && y + cardH <= work.Bottom;
        int ClampX(int x) => Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - cardW));
        int ClampY(int y) => Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - cardH));

        var candidates = new (string Side, int X, int Y)[]
        {
            ("above", cursor.X - (cardW / 2), cursor.Y - gap - cardH),
            ("below", cursor.X - (cardW / 2), cursor.Y + gap),
            ("right", cursor.X + gap, cursor.Y - (cardH / 2)),
            ("left", cursor.X - gap - cardW, cursor.Y - (cardH / 2)),
        };

        // A side fits if the card clears the cursor along that axis; the cross axis is clamped.
        foreach (var (side, cx, cy) in candidates)
        {
            var vertical = side is "above" or "below";
            if (Fits(vertical ? ClampX(cx) : cx, vertical ? cy : ClampY(cy)))
            {
                return (ClampX(cx), ClampY(cy), side);
            }
        }

        return (ClampX(candidates[0].X), ClampY(candidates[0].Y), "clamped");
    }
}
