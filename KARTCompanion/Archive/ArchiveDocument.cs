namespace KARTCompanion.Archive;

/// <summary>One award as the archive keeps it: the addon's fields verbatim, plus where it came from.</summary>
public sealed class ArchivedAward
{
    public Dictionary<string, object?> Fields { get; set; } = new();
    public string SourceFile { get; set; } = "";
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }

    /// <summary>
    /// Set when a snapshot no longer contains this award and neither the 500-entry cap nor an epoch
    /// bump explains its absence — see ArchiveMerger. Nothing is ever deleted from the archive; this
    /// records that the game dropped it, so a later export can leave it out rather than credit
    /// somebody with an item that was taken back off them.
    /// </summary>
    public bool Withdrawn { get; set; }

    private long Time => Fields.TryGetValue("time", out var v) && v is double d ? (long)d : 0;
    private string? Str(string key) => Fields.TryGetValue(key, out var v) ? v as string : null;

    public string Key => Str("id") ?? $"{Time}|{Str("winnerKey")}|{Str("item")}";
}

public sealed class ArchiveDocument
{
    public int Version { get; set; } = 1;
    public List<ArchivedAward> Awards { get; set; } = new();
}
