using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace SpareHand;

/// <summary>Start with Windows, and awareness of PowerToys modules that listen for the same keys.</summary>
internal static class SystemIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "SpareHand";

    /// <summary>Passed at sign-in, so starting with Windows doesn't open the settings window.</summary>
    public const string BackgroundArg = "--background";

    private static string RunCommand => $"\"{Environment.ProcessPath}\" {BackgroundArg}";

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }

        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
            {
                key.SetValue(RunValue, RunCommand);
            }
            else
            {
                key.DeleteValue(RunValue, throwOnMissingValue: false);
            }

            Log.Write($"start with Windows = {value}");
        }
    }

    /// <summary>
    /// Entries written before v0.1.2 start Spare Hand with no arguments, which now opens settings. Rewrite our own
    /// entry with --background; leave anything that points at another copy alone.
    /// </summary>
    public static void UpgradeStartupEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(RunValue) is string value && !value.Contains(BackgroundArg, StringComparison.Ordinal) &&
                value.Contains(Environment.ProcessPath!, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(RunValue, RunCommand);
                Log.Write("start with Windows entry upgraded to --background");
            }
        }
        catch (Exception e)
        {
            Log.Write($"couldn't upgrade the startup entry: {e.Message}");
        }
    }

    /// <summary>
    /// True when PowerToys Find My Mouse is enabled and activated by double-pressing Ctrl (its default),
    /// which would fire alongside a Ctrl trigger. Read from PowerToys' own settings files.
    /// </summary>
    public static bool FindMyMouseUsesDoubleCtrl()
    {
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "PowerToys");
            var general = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "settings.json")));
            if (general?["enabled"]?["FindMyMouse"]?.GetValue<bool>() != true)
            {
                return false;
            }

            var fmm = Path.Combine(root, "FindMyMouse", "settings.json");
            if (!File.Exists(fmm))
            {
                return true; // enabled with defaults: double Left Ctrl
            }

            var method = JsonNode.Parse(File.ReadAllText(fmm))?["properties"]?["activation_method"]?["value"]?.GetValue<int>() ?? 0;
            return method is 0 or 1; // 0 = double Left Ctrl, 1 = double Right Ctrl, 2 = shake
        }
        catch
        {
            return false;
        }
    }
}
