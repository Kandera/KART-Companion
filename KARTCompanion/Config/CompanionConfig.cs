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

    public bool IsComplete => !string.IsNullOrWhiteSpace(GroupKey) && !string.IsNullOrWhiteSpace(SavedVariablesFilePath);
}
