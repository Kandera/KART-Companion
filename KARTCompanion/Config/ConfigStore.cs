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

    public static void Save(CompanionConfig config)
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        var tempPath = ConfigPath + ".tmp";
        File.WriteAllText(tempPath, json);

        if (File.Exists(ConfigPath))
            File.Replace(tempPath, ConfigPath, null);
        else
            File.Move(tempPath, ConfigPath);
    }
}
