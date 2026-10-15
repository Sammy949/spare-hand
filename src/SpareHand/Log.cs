namespace SpareHand;

/// <summary>Plain append-only log in %LOCALAPPDATA%\SpareHand\logs. Coordinates are physical pixels.</summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static readonly string PathOnDisk = System.IO.Path.Combine(AppPaths.LogDirectory, $"spare-hand-{DateTime.Now:yyyyMMdd}.log");

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}";
        try
        {
            lock (Gate)
            {
                File.AppendAllText(PathOnDisk, line + Environment.NewLine);
            }
        }
        catch (IOException)
        {
            // Logging must never take the app down.
        }
    }
}

internal static class AppPaths
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpareHand");
    public static readonly string LogDirectory = Directory.CreateDirectory(Path.Combine(Root, "logs")).FullName;
    public static string File(string name) => Path.Combine(Directory.CreateDirectory(Root).FullName, name);
}
