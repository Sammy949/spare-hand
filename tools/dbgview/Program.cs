using System.IO.MemoryMappedFiles;
using System.Text;

// Usage: DbgView <seconds> [processName]. Listens on the DBWIN_* protocol and prints messages from that process.
var seconds = args.Length > 0 ? int.Parse(args[0]) : 20;
var filter = args.Length > 1 ? args[1] : null;
using var bufferReady = new EventWaitHandle(false, EventResetMode.AutoReset, "DBWIN_BUFFER_READY");
using var dataReady = new EventWaitHandle(false, EventResetMode.AutoReset, "DBWIN_DATA_READY");
using var mmf = MemoryMappedFile.CreateOrOpen("DBWIN_BUFFER", 4096);
using var view = mmf.CreateViewAccessor(0, 4096);
var until = DateTime.Now.AddSeconds(seconds);
Console.WriteLine("listening");
while (DateTime.Now < until)
{
    bufferReady.Set();
    if (!dataReady.WaitOne(500))
    {
        continue;
    }

    var pid = view.ReadUInt32(0);
    var bytes = new byte[4092];
    view.ReadArray(4, bytes, 0, bytes.Length);
    var text = Encoding.Default.GetString(bytes, 0, Math.Max(0, Array.IndexOf(bytes, (byte)0))).TrimEnd();
    string name;
    try { name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { name = pid.ToString(); }
    if (filter is null || name.Equals(filter, StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"[{name}] {text}");
    }
}
