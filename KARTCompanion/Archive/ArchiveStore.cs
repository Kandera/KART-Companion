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

    /// <summary>Appended to <see cref="ArchivePath"/> for the one-generation copy Save keeps.</summary>
    public const string BackupSuffix = ".bak";

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
                // One generation of history kept before the overwrite. The whole premise of this
                // file is that the game has already forgotten what is in it, so there is no second
                // copy to fall back on — and until now every save replaced it in place. A bad merge,
                // a bad shutdown, or a bug in a future build otherwise takes the only copy with it.
                //
                // The backup is a courtesy on top of the save it must never block: a backup tool, an
                // editor, or a virus scanner holding a handle on the .bak file would otherwise turn a
                // save that would have succeeded into a thrown exception and a crash balloon, on the
                // one file in this program that cannot be regenerated. Losing this generation's backup
                // is a strictly smaller problem than losing the save itself, so a failure here does not
                // abort the save.
                try
                {
                    if (File.Exists(path)) File.Copy(path, path + BackupSuffix, overwrite: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
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

        // Anything this build does not understand is a quarantine case, not something to work
        // around at merge time. Before this, a hand-edited archive — or one an intermediate build
        // wrote — was loaded happily and then threw out of ArchivedAward.Key on every single pass,
        // with nothing but a generic crash balloon to show for it and no way back.
        if (doc.Version != ArchiveDocument.CurrentVersion)
            throw new FormatException(
                $"Archive version {doc.Version} is not {ArchiveDocument.CurrentVersion}, which is the only one this build understands.");

        foreach (var award in doc.Awards)
        {
            foreach (var key in award.Fields.Keys.ToList())
            {
                if (award.Fields[key] is JsonElement el)
                {
                    award.Fields[key] = ConvertElement(el);
                }
            }

            // ArchiveMerger never archives an entry without an id, so one in the file means the
            // document did not come from this program. ArchivedAward.Key would throw on it later;
            // failing here means the file is quarantined intact rather than crashing every pass.
            if (award.Fields.GetValueOrDefault("id") is not string id || id.Length == 0)
                throw new FormatException("Archive holds an award with no id. ArchiveMerger never writes one.");
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
