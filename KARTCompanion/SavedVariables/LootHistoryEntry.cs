namespace KARTCompanion.SavedVariables;

/// <summary>
/// One award, exactly as the addon stored it. Fields live in a dictionary rather than in properties
/// so that a field this build has never heard of survives being read and written again — a newer
/// addon version may add one, and an archive that silently drops it is lossy against a file it could
/// read perfectly well.
///
/// A field the addon did not write is ABSENT from Fields, not present-and-null. The distinction
/// matters: in the maintainer's real file, color is on 104 of 133 entries and difficultyID on 102.
/// Turning "we never knew" into "we know it was nothing" is a fact invented at read time and then
/// kept forever.
/// </summary>
public sealed class LootHistoryEntry
{
    public LootHistoryEntry(IReadOnlyDictionary<string, object?> fields) => Fields = fields;

    public IReadOnlyDictionary<string, object?> Fields { get; }

    private T? Get<T>(string key) where T : class => Fields.TryGetValue(key, out var v) ? v as T : null;
    private long? Num(string key) => Fields.TryGetValue(key, out var v) && v is double d ? (long)d : null;

    public long Time => Num("time") ?? 0;
    public string? Item => Get<string>("item");
    public string? Winner => Get<string>("winner");
    public string? WinnerKey => Get<string>("winnerKey");
    public string? Reason => Get<string>("reason");
    public string? Class => Get<string>("class");
    public long? RollId => Num("rollID");
    public string? Difficulty => Get<string>("difficulty");
    public long? DifficultyId => Num("difficultyID");
    public string? Id => Get<string>("id");
    public long? Epoch => Num("epoch");
    public bool? Exported => Fields.TryGetValue("exported", out var v) && v is bool b ? b : null;

    /// <summary>
    /// The key for entries written before sub-project 1 minted a stable id — the maintainer's live
    /// file holds 133 of them. Two awards in the same second, to the same person, for the same item
    /// do not occur. This is not a substitute for the minted id: it cannot survive a display-name
    /// change and it is not what peers agree on. For an archive that only has to recognise its own
    /// earlier snapshots, it is enough.
    /// </summary>
    public string DerivedKey => $"{Time}|{WinnerKey}|{Item}";

    public string Key => Id ?? DerivedKey;
}
