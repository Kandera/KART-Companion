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
            // A file containing the literal JSON "null" deserializes without throwing. Treat it the
            // same as unreadable rather than let it slip through to the empty-document fallback below.
            return Deserialize(json) ?? throw new FormatException("Archive file deserialized to null.");
        }
        catch (Exception ex)
        {
            // Deliberately NOT ConfigStore's "start fresh" behaviour. A config regenerates itself;
            // this file holds every award the game has already forgotten, and there is no second
            // copy anywhere. Move it aside and let the caller tell the user.
            //
            // The quarantine name needs the same per-call uniqueness Save() already gives its temp
            // file: a second-granularity timestamp alone lets two quarantines in the same second
            // collide, and overwrite:true would then silently destroy the first one's bytes — the
            // exact loss quarantining exists to prevent. No overwrite: if the target somehow exists
            // anyway, that is a fact worth failing on, not one worth papering over.
            var quarantine = path + ".unreadable-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss")
                + "-" + Guid.NewGuid().ToString("N");
            File.Move(path, quarantine);
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
                    award.Fields[key] = ConvertElement(el);
                }
            }
        }

        return doc;
    }

    // The reader's nested-table fields (e.g. "color") come through as Dictionary<string, object?>
    // values, not just string/double/bool, so this has to recurse into objects and arrays rather than
    // handle one flat level. An unhandled JsonValueKind throws instead of falling back to null: a loud
    // failure on a shape this build does not know about is strictly better than silently discarding it.
    private static object? ConvertElement(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Object => ConvertObject(el),
        JsonValueKind.Array => ConvertArray(el),
        _ => throw new FormatException($"Unrecognised JSON value kind: {el.ValueKind}"),
    };

    private static Dictionary<string, object?> ConvertObject(JsonElement el)
    {
        var result = new Dictionary<string, object?>();
        foreach (var property in el.EnumerateObject())
            result[property.Name] = ConvertElement(property.Value);
        return result;
    }

    private static List<object?> ConvertArray(JsonElement el)
    {
        var result = new List<object?>();
        foreach (var item in el.EnumerateArray())
            result.Add(ConvertElement(item));
        return result;
    }
}
