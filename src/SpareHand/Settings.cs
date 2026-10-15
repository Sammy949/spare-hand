using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpareHand;

internal enum TriggerKey
{
    Alt,
    Ctrl,
    Shift,
}

internal sealed class Settings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public TriggerKey Trigger { get; set; } = TriggerKey.Alt;

    /// <summary>Max gap between the two taps; 0 = the system double-click time.</summary>
    public int DoubleTapMs { get; set; }

    public bool ShowQuickShare { get; set; } = true;

    public bool ShowLocalSend { get; set; } = true;

    public bool ShowShareSheet { get; set; } = true;

    public int MaxFolders { get; set; } = 4;

    /// <summary>Double-tapping the trigger without a drag opens the Hold library.</summary>
    public bool LibraryOnDoubleTap { get; set; } = true;

    /// <summary>Optional extra shortcut for the library, e.g. "Win+Shift+H", for keys that clash with other tools.</summary>
    public string LibraryHotkey { get; set; } = string.Empty;

    /// <summary>Notification-area icon. When hidden, launching Spare Hand again opens the library (docs/research.md: 6 asks).</summary>
    public bool ShowNotifyIcon { get; set; } = true;

    /// <summary>Never trigger while a fullscreen app, game or presentation is in front (boring.notch #119; docs/research.md).</summary>
    public bool PauseInFullscreen { get; set; } = true;

    /// <summary>Process names (e.g. "photoshop.exe") where the trigger never fires.</summary>
    public List<string> ExcludedApps { get; set; } = [];

    /// <summary>Off by default: nothing leaves Hold on its own (docs/research.md).</summary>
    public bool RemoveAfterDragOut { get; set; }

    public static Settings Load()
    {
        var path = AppPaths.File("settings.json");
        try
        {
            if (File.Exists(path))
            {
                // Save straight back so settings added in newer builds show up in the file with their defaults.
                var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json) ?? new Settings();
                loaded.Save();
                return loaded;
            }
        }
        catch (Exception e)
        {
            Log.Write($"settings unreadable, using defaults: {e.Message}");
        }

        var fresh = new Settings();
        fresh.Save();
        return fresh;
    }

    /// <summary>Raised after every Save, so running parts can pick up changes live.</summary>
    public event Action? Changed;

    public void Save()
    {
        File.WriteAllText(AppPaths.File("settings.json"), JsonSerializer.Serialize(this, Json));
        Changed?.Invoke();
    }

    /// <summary>Parses LibraryHotkey into RegisterHotKey modifiers + virtual key; null when unset or invalid.</summary>
    public (uint Modifiers, uint Key)? ParseLibraryHotkey()
    {
        if (string.IsNullOrWhiteSpace(LibraryHotkey))
        {
            return null;
        }

        uint mods = 0x4000; // MOD_NOREPEAT
        uint key = 0;
        foreach (var part in LibraryHotkey.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "alt": mods |= 0x1; break;
                case "ctrl" or "control": mods |= 0x2; break;
                case "shift": mods |= 0x4; break;
                case "win": mods |= 0x8; break;
                case { Length: 1 } c when char.IsLetterOrDigit(c[0]): key = char.ToUpperInvariant(c[0]); break;
                case var f when f.StartsWith('f') && int.TryParse(f[1..], out var n) && n is >= 1 and <= 24: key = (uint)(0x70 + n - 1); break;
                default: return null;
            }
        }

        return key == 0 ? null : (mods, key);
    }

    public (int Left, int Right) TriggerVirtualKeys() => Trigger switch
    {
        TriggerKey.Ctrl => (0xA2, 0xA3),
        TriggerKey.Shift => (0xA0, 0xA1),
        _ => (0xA4, 0xA5),
    };
}
