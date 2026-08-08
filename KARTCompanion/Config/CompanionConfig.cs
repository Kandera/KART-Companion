namespace KARTCompanion.Config;

/// <summary>
/// Persisted to %AppData%\KARTCompanion\config.json as plain JSON — deliberately not a secrets
/// vault (out of scope for v1). GroupKey is a bearer credential for the whole roster's WoWUtils
/// data; this file should never be shared or committed anywhere.
/// </summary>
public sealed class CompanionConfig
{
    public string? GroupKey { get; set; }

    /// <summary>The WoW install folder the user browsed to in Settings (contains "_retail_").
    /// Kept only so the Settings dialog can show it back on reopen — sync itself uses
    /// SavedVariablesFilePath, which is resolved from this once and persisted separately.</summary>
    public string? WowInstallPath { get; set; }

    /// <summary>Explicit path to the KeineAhnungRaidTools.lua SavedVariables file to write to.
    /// Set once, either via auto-detection (if exactly one match) or a manual picker (if the
    /// scan found zero or multiple candidates, e.g. multiple Battle.net accounts).</summary>
    public string? SavedVariablesFilePath { get; set; }

    public int SyncIntervalMinutes { get; set; } = 15;

    /// <summary>When false, the background timer in TrayApplicationContext doesn't run — only
    /// the Settings dialog's manual "Force Sync" button still syncs. Defaults to true so existing
    /// config.json files without this field keep their current always-on behavior after
    /// upgrading.</summary>
    public bool AutoSyncEnabled { get; set; } = true;

    public DateTimeOffset? LastSyncUtc { get; set; }

    /// <summary>
    /// Last-write time of each saved-variables file the last time it was read into the archive,
    /// keyed by full path. Compared against File.GetLastWriteTimeUtc on each tick — deliberately
    /// instead of watching the game process, which would mean holding a handle on it.
    /// </summary>
    public Dictionary<string, DateTimeOffset> LootHistoryReadAt { get; set; } = new();

    /// <summary>
    /// Last-write time of each saved-variables file at the point we last told the user it could not
    /// be read, keyed by full path. Lets the automatic tick tell a file that is still failing for the
    /// same content (skip — already reported) from one that failed, then changed, then failed again
    /// (report — that is new information). Manual "Read loot history now" ignores this and always
    /// reports, per Important 4: a parse failure must never be silent.
    /// </summary>
    public Dictionary<string, DateTimeOffset> LootHistoryUnreadableNotifiedAt { get; set; } = new();

    public bool IsComplete => !string.IsNullOrWhiteSpace(GroupKey) && !string.IsNullOrWhiteSpace(SavedVariablesFilePath);

    /// <summary>
    /// A copy carrying every field, for callers that want to change a few of them without rebuilding
    /// the object out of named properties. The Settings dialog did rebuild it that way and so
    /// silently dropped LootHistoryReadAt on every OK — harmless only because the merge is
    /// idempotent, and it would have swallowed the next field anyone added. MemberwiseClone cannot
    /// omit a field; the one mutable collection is copied so the two objects stay independent.
    /// </summary>
    public CompanionConfig Copy()
    {
        var copy = (CompanionConfig)MemberwiseClone();
        copy.LootHistoryReadAt = new Dictionary<string, DateTimeOffset>(LootHistoryReadAt);
        copy.LootHistoryUnreadableNotifiedAt = new Dictionary<string, DateTimeOffset>(LootHistoryUnreadableNotifiedAt);
        return copy;
    }
}
