namespace KARTCompanion.SavedVariables;

/// <summary>
/// Canonical English difficulty name by Blizzard's difficultyID, mirroring LootHistory.lua's
/// DIFFICULTY_EN. Shared by RcLootCouncilJsonWriter's export and HistoryScreen's list — before
/// this they used two different rules: the export always preferred the canonical English name,
/// while the list preferred whatever (possibly localized) string the game client had stored and
/// otherwise fell back to showing a raw difficultyID number. On a non-English client that meant
/// the list and the export disagreed about the same award, and an award logged without a
/// difficulty string showed a bare number in the list. One table, one rule, used by both.
/// </summary>
public static class DifficultyNames
{
    private static readonly Dictionary<long, string> En = new()
    {
        [1] = "Normal",
        [2] = "Heroic",
        [3] = "10 Player",
        [4] = "25 Player",
        [5] = "10 Player (Heroic)",
        [6] = "25 Player (Heroic)",
        [7] = "LFR",
        [8] = "Mythic Keystone",
        [9] = "40 Player",
        [14] = "Normal",
        [15] = "Heroic",
        [16] = "Mythic",
        [17] = "LFR",
        [23] = "Mythic",
        [24] = "Timewalking",
        [33] = "Timewalking",
        [208] = "Delve",
    };

    /// <summary>Canonical English name by difficultyID when known, otherwise falling back to the
    /// stored (possibly localized) difficulty string; empty when neither is available. Entries
    /// logged before difficultyID existed have no id, so the fallback is what carries those.</summary>
    public static string Canonical(LootHistoryEntry e) =>
        (e.DifficultyId is { } id && En.TryGetValue(id, out var name)) ? name : (e.Difficulty ?? "");
}
