namespace KARTCompanion;

/// <summary>
/// Last-resort logging for unhandled exceptions caught at the top level (Program.cs) — this is a
/// background tray app with no console, so without this an unexpected crash leaves no trace.
/// </summary>
public static class CrashLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KARTCompanion", "error.log");

    public static void Log(Exception ex) => Log(ex, LogPath);

    public static void Log(Exception ex, string logPath)
    {
        var dir = Path.GetDirectoryName(logPath)!;
        Directory.CreateDirectory(dir);

        var entry = $"[{DateTimeOffset.UtcNow:u}] {ex}{Environment.NewLine}";
        File.AppendAllText(logPath, entry);
    }
}
