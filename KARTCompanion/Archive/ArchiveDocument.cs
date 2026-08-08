using System.Text.Json.Serialization;

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

    private string? Str(string key) => Fields.TryGetValue(key, out var v) ? v as string : null;

    // ArchiveMerger only ever archives entries that carry an id (see its own doc comment on why:
    // entries without one predate the release that fixed the bugs that made a fallback necessary in
    // the first place). An award with no id here would mean that guarantee was violated somewhere
    // upstream, which is worth failing loudly on rather than inventing a fallback key for.
    //
    // [JsonIgnore]: Key is derived from Fields, not independent data — persisting it would be
    // redundant at best. It also matters here specifically: without it, System.Text.Json's default
    // "serialize every public property" behaviour would evaluate Key (and so throw) while saving any
    // ArchivedAward built without an id, which is a legitimate shape in tests that construct one
    // directly rather than through ArchiveMerger.
    [JsonIgnore]
    public string Key => Str("id") ?? throw new InvalidOperationException(
        "ArchivedAward has no id. ArchiveMerger should never archive an entry without one.");
}

public sealed class ArchiveDocument
{
    public int Version { get; set; } = 1;
    public List<ArchivedAward> Awards { get; set; } = new();
}
