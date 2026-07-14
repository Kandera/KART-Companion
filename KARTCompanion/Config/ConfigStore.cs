using System.Text.Json;

namespace KARTCompanion.Config;

public static class ConfigStore
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KARTCompanion", "config.json");

    public static CompanionConfig Load()
    {
        if (!File.Exists(ConfigPath)) return new CompanionConfig();

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<CompanionConfig>(json) ?? new CompanionConfig();
        }
        catch
        {
            // Corrupt/unreadable config — start fresh rather than crash the tray app on launch.
            return new CompanionConfig();
        }
    }

    private static readonly object SaveLock = new();

    public static void Save(CompanionConfig config) => Save(config, ConfigPath);

    public static void Save(CompanionConfig config, string configPath)
    {
        var dir = Path.GetDirectoryName(configPath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        // Unique per call, and the whole write+rename serialized via SaveLock: two overlapping
        // Save() calls (background timer vs. Settings dialog's Force Sync) must never write to
        // the same temp file or race each other's rename onto configPath.
        var tempPath = configPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        lock (SaveLock)
        {
            try
            {
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, configPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
    }
}
