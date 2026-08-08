using System.Text.Json;

namespace KARTCompanion.Archive;

public sealed class ArchiveUnreadableException : Exception
{
    public ArchiveUnreadableException(string quarantinePath, Exception inner)
        : base($"The loot history archive could not be read. It was moved to {quarantinePath}.", inner)
        => QuarantinePath = quarantinePath;

    public string QuarantinePath { get; }
}

public static class ArchiveStore
{
    public static string ArchivePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KARTCompanion", "loot-history.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly object SaveLock = new();

    public static ArchiveDocument Load() => Load(ArchivePath);

    public static ArchiveDocument Load(string path)
    {
        if (!File.Exists(path)) return new ArchiveDocument();

        try
        {
            var json = File.ReadAllText(path);
            return Deserialize(json) ?? new ArchiveDocument();
        }
        catch (Exception ex)
        {
            // Deliberately NOT ConfigStore's "start fresh" behaviour. A config regenerates itself;
            // this file holds every award the game has already forgotten, and there is no second
            // copy anywhere. Move it aside and let the caller tell the user.
            var quarantine = path + ".unreadable-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            File.Move(path, quarantine, overwrite: true);
            throw new ArchiveUnreadableException(quarantine, ex);
        }
    }

    public static void Save(ArchiveDocument doc) => Save(doc, ArchivePath);

    public static void Save(ArchiveDocument doc, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var json = Serialize(doc);
        // Same discipline as ConfigStore.Save: a unique temp file per call and the whole
        // write-then-rename under one lock, so two overlapping saves cannot race each other.
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        lock (SaveLock)
        {
            try
            {
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
    }

    // Fields are object? holding strings, doubles and bools. System.Text.Json round-trips those as
    // JsonElement unless told otherwise, which would turn every value into a JsonElement on the way
    // back and break the merge's comparisons. Convert explicitly instead.
    private static string Serialize(ArchiveDocument doc) => JsonSerializer.Serialize(doc, Options);

    private static ArchiveDocument? Deserialize(string json)
    {
        var doc = JsonSerializer.Deserialize<ArchiveDocument>(json, Options);
        if (doc == null) return null;

        foreach (var award in doc.Awards)
        {
            foreach (var key in award.Fields.Keys.ToList())
            {
                if (award.Fields[key] is JsonElement el)
                {
                    award.Fields[key] = el.ValueKind switch
                    {
                        JsonValueKind.String => el.GetString(),
                        JsonValueKind.Number => el.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => null,
                    };
                }
            }
        }

        return doc;
    }
}
