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

    /// <summary>
    /// When this award was exported from the Companion, or null if it never was.
    ///
    /// Lives here rather than in Fields on purpose: Fields is a faithful copy of what the addon
    /// wrote, and it stays that way — which also means ArchiveMerger's ApplyFields, writing only
    /// keys a snapshot carries, can never clear this.
    ///
    /// A timestamp rather than a bool: "exported on Tuesday" answers a question "exported: yes"
    /// does not.
    /// </summary>
    public DateTimeOffset? ExportedByCompanionAt { get; set; }

    /// <summary>
    /// The maintainer's corrections, field name to corrected value. Empty when the award was never
    /// edited.
    ///
    /// Beside Fields rather than inside it, and that placement is the whole design. Fields is a
    /// verbatim copy of what the addon wrote; the Companion never writes back into the game, so every
    /// future snapshot still carries the ORIGINAL value. ArchiveMerger.ApplyFields writes exactly the
    /// keys an incoming snapshot carries, so an edit stored in Fields would be silently undone by the
    /// very next sync. Here it cannot be reached at all.
    ///
    /// Keeping the original is also what makes reverting possible, what makes "was this touched?"
    /// answerable, and what guarantees an edit can never destroy data — which matters, because this
    /// archive is the only file in this program that cannot be regenerated.
    ///
    /// Only the fields in AwardEditor.EditableFields may appear here; AwardEditor is the only thing
    /// that should write to it.
    /// </summary>
    public Dictionary<string, string> Edits { get; set; } = new();

    /// <summary>
    /// Set by the maintainer to keep this award out of every export, permanently — for a row a defect
    /// made wrong beyond correcting.
    ///
    /// Distinct from Withdrawn, which is DERIVED (the addon deleted its own row; see ArchiveMerger)
    /// and can be undone by a later snapshot. This one is a decision, and nothing but the maintainer
    /// changes it. It exists because the alternative — leaving the row out of the selection on every
    /// export — relies on memory and fails silently the one time it is forgotten.
    /// </summary>
    public bool ExcludedFromExport { get; set; }

    /// <summary>
    /// What this award says, once the maintainer's corrections are taken into account: Fields with
    /// Edits laid over it. Everything that displays or exports an award reads this; only the merge
    /// reads Fields.
    ///
    /// [JsonIgnore]: derived from two properties that are both persisted, so writing it out would
    /// store the same information twice and give a hand-edited file two disagreeing answers.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, object?> EffectiveFields
    {
        get
        {
            // The overwhelmingly common case: no edit, no allocation.
            if (Edits.Count == 0) return Fields;

            var merged = new Dictionary<string, object?>(Fields);
            foreach (var (key, value) in Edits) merged[key] = value;
            return merged;
        }
    }

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
    /// <summary>The only archive layout this build understands. ArchiveStore.Load refuses anything
    /// else rather than guessing at it — see the validation there.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<ArchivedAward> Awards { get; set; } = new();
}
