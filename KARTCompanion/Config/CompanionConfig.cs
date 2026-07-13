namespace KARTCompanion.Config;

/// <summary>
/// Persisted to %AppData%\KARTCompanion\config.json as plain JSON — deliberately not a secrets
/// vault (out of scope for v1). GroupKey is a bearer credential for the whole roster's WoWUtils
/// data; this file should never be shared or committed anywhere.
/// </summary>
public sealed class CompanionConfig
{
    public string? GroupKey { get; set; }

    /// <summary>Explicit path to the KeineAhnungRaidTools.lua SavedVariables file to write to.
    /// Set once, either via auto-detection (if exactly one match) or a manual picker (if the
    /// scan found zero or multiple candidates, e.g. multiple Battle.net accounts).</summary>
    public string? SavedVariablesFilePath { get; set; }

    public int SyncIntervalMinutes { get; set; } = 15;

    public DateTimeOffset? LastSyncUtc { get; set; }

    public bool IsComplete => !string.IsNullOrWhiteSpace(GroupKey) && !string.IsNullOrWhiteSpace(SavedVariablesFilePath);
}
