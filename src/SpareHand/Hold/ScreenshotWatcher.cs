using Microsoft.UI.Dispatching;
using SpareHand.Shell;

namespace SpareHand.Hold;

/// <summary>
/// On by default (a switch in settings and the guide): new screenshots land in Hold, silently. Watches Windows' Screenshots folder (Win+PrtScn, Snipping Tool,
/// Win+Shift+S). Measured on a real PC: Win+PrtScn writes the whole file at once, Snipping Tool creates it empty and
/// fills it ~90 ms later, so a file is held only once its writes have been quiet for a moment and it opens as a
/// complete image. Only the watcher's own items roll off when the day ends; the files are never touched.
/// </summary>
internal sealed class ScreenshotWatcher
{
    public const string Source = "screenshot";

    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan GiveUp = TimeSpan.FromSeconds(15);
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp"];

    private readonly HoldStore _hold;
    private readonly Settings _settings;
    private readonly DispatcherQueue _ui;
    private readonly Dictionary<string, (DateTime First, DateTime Last)> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _settle;
    private readonly Timer _midnight;
    private FileSystemWatcher? _fsw;

    public ScreenshotWatcher(HoldStore hold, Settings settings, DispatcherQueue ui)
    {
        _hold = hold;
        _settings = settings;
        _ui = ui;
        _settle = new Timer(_ => Settle(), null, Timeout.Infinite, Timeout.Infinite);
        _midnight = new Timer(_ => _ui.TryEnqueue(RollOff), null, Timeout.Infinite, Timeout.Infinite);
        _settings.Changed += () => _ui.TryEnqueue(Sync);
        Sync();
        RollOff();
    }

    /// <summary>Starts or stops watching to match the setting, live.</summary>
    private void Sync()
    {
        if (_settings.HoldNewScreenshots && _fsw is null)
        {
            var folder = KnownFolders.Screenshots;
            if (folder is null || !Directory.Exists(folder))
            {
                Log.Write("screenshots: no Screenshots folder to watch");
                return;
            }

            _fsw = new FileSystemWatcher(folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
            };
            _fsw.Created += (_, e) => Touch(e.FullPath);
            _fsw.Changed += (_, e) => Touch(e.FullPath);
            _fsw.Renamed += (_, e) => _ui.TryEnqueue(() => OnRenamed(e.OldFullPath, e.FullPath));
            _fsw.Deleted += (_, e) => _ui.TryEnqueue(() => OnDeleted(e.FullPath));
            _fsw.Error += (_, e) => Log.Write($"screenshots: watcher error {e.GetException().Message}");
            _fsw.EnableRaisingEvents = true;
            Log.Write("screenshots: watching the Screenshots folder");
        }
        else if (!_settings.HoldNewScreenshots && _fsw is not null)
        {
            _fsw.Dispose();
            _fsw = null;
            lock (_pending)
            {
                _pending.Clear();
            }

            Log.Write("screenshots: stopped watching");
        }
    }

    private void Touch(string path)
    {
        if (!Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_pending)
        {
            var now = DateTime.UtcNow;
            _pending[path] = _pending.TryGetValue(path, out var seen) ? (seen.First, now) : (now, now);
        }

        _settle.Change(Quiet + TimeSpan.FromMilliseconds(50), Timeout.InfiniteTimeSpan);
    }

    /// <summary>Holds every pending file that has gone quiet and opens as a complete image; retries the rest.</summary>
    private void Settle()
    {
        var ready = new List<string>();
        var now = DateTime.UtcNow;
        lock (_pending)
        {
            foreach (var (path, (first, last)) in _pending.ToList())
            {
                if (now - last < Quiet)
                {
                    continue;
                }

                if (IsCompleteImage(path))
                {
                    ready.Add(path);
                    _pending.Remove(path);
                }
                else if (now - first > GiveUp || !File.Exists(path))
                {
                    _pending.Remove(path);
                    Log.Write("screenshots: gave up on a file that never finished writing");
                }
            }

            if (_pending.Count > 0)
            {
                _settle.Change(TimeSpan.FromMilliseconds(250), Timeout.InfiniteTimeSpan);
            }
        }

        if (ready.Count > 0)
        {
            _ui.TryEnqueue(() =>
            {
                var added = _hold.AddFrom(ready, Source);
                Log.Write($"screenshots: held {added} new screenshot(s)");
                RollOff();
            });
        }
    }

    /// <summary>Not empty, not still open for writing, and starts with a real image signature.</summary>
    private static bool IsCompleteImage(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < 32)
            {
                return false;
            }

            Span<byte> head = stackalloc byte[8];
            stream.ReadExactly(head);
            return head[..4].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }) // PNG
                || head[..3].SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF })       // JPEG
                || head[..4].SequenceEqual("GIF8"u8)
                || head[..2].SequenceEqual("BM"u8);
        }
        catch (IOException)
        {
            return false; // still being written (the capture app holds it open), or gone
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void OnRenamed(string oldPath, string newPath)
    {
        if (_hold.Items.Any(i => string.Equals(i.Path, oldPath, StringComparison.OrdinalIgnoreCase)))
        {
            _hold.Relocate(oldPath, newPath);
        }
        else
        {
            Touch(newPath); // some apps write a temporary name and rename it into place
        }
    }

    // A screenshot deleted on purpose leaves Hold too, instead of lingering as Missing. Only the watcher's own items.
    private void OnDeleted(string path) => _hold.ForgetFrom(path, Source);

    /// <summary>"Only today's": the watcher's items from earlier days leave Hold. Runs at start, after each add, and at midnight.</summary>
    private void RollOff()
    {
        var removed = _hold.ForgetOlderThan(Source, DateTime.Today);
        if (removed > 0)
        {
            Log.Write($"screenshots: {removed} from earlier days left Hold (files untouched)");
        }

        var untilMidnight = DateTime.Today.AddDays(1) - DateTime.Now + TimeSpan.FromSeconds(5);
        _midnight.Change(untilMidnight, Timeout.InfiniteTimeSpan);
    }
}
