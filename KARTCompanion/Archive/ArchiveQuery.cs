using System.Text.RegularExpressions;

namespace KARTCompanion.Archive;

/// <summary>
/// Where an award stands relative to the two places it can be exported to. Withdrawn and Excluded both
/// outrank every export state: an award that was taken back is not "exported", it is gone, and one the
/// maintainer excluded will not be exported either way, whatever the addon or the Companion recorded.
/// </summary>
public enum AwardStatus
{
    Open,
    ExportedByAddon,
    ExportedByCompanion,
    ExportedByBoth,
    Withdrawn,

    /// <summary>The maintainer marked this award as never-export. Declared last so the status filter
    /// dropdown, which is built from Enum.GetValues in declaration order, keeps the indices it had —
    /// see HistoryExportPlanner.StatusForComboIndex.</summary>
    Excluded,
}

/// <summary>Answers questions of the archive: what state is this award in, and which awards match a filter.</summary>
public static class ArchiveQuery
{
    // The display name inside a hyperlink's brackets: |c...|Hitem:...|h[Name]|h|r.
    private static readonly Regex ItemNamePattern = new(@"\[(.*?)\]", RegexOptions.Compiled);

    public static AwardStatus StatusOf(ArchivedAward award)
    {
        // The addon's "exported" field has three states: false is new, true is exported, and absent
        // means an entry written before the feature existed. Absent can't actually occur here — an
        // award with no minted id never reaches the archive at all — but if it ever did, treating it
        // as not-exported (rather than inventing a fourth AwardStatus) is the safe reading: it means
        // "we don't know", not "the addon exported it".
        if (award.Withdrawn) return AwardStatus.Withdrawn;

        // Below Withdrawn deliberately: the game dropping its own row is a stronger statement about an
        // award than the maintainer choosing to hold it back, and a withdrawn award is already never
        // exported. Above the export marks, because what matters about this award now is that it will
        // not go again.
        if (award.ExcludedFromExport) return AwardStatus.Excluded;

        var byAddon = award.Fields.TryGetValue("exported", out var v) && v is true;
        var byCompanion = award.ExportedByCompanionAt != null;

        return (byAddon, byCompanion) switch
        {
            (true, true) => AwardStatus.ExportedByBoth,
            (true, false) => AwardStatus.ExportedByAddon,
            (false, true) => AwardStatus.ExportedByCompanion,
            (false, false) => AwardStatus.Open,
        };
    }

    public static IReadOnlyList<ArchivedAward> Filter(
        IEnumerable<ArchivedAward> awards,
        string? player,
        DateTimeOffset? from,
        DateTimeOffset? to,
        AwardStatus? status,
        string? search)
    {
        var query = awards.AsEnumerable();

        if (player != null)
            query = query.Where(a => Str(a, "winner") == player);

        if (from != null)
            query = query.Where(a => Time(a) >= from.Value);

        if (to != null)
            query = query.Where(a => Time(a) <= to.Value);

        if (status != null)
            query = query.Where(a => StatusOf(a) == status.Value);

        if (search != null)
            query = query.Where(a =>
                ItemDisplayName(Str(a, "item")).Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (Str(a, "reason")?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));

        return query.OrderByDescending(Time).ToList();
    }

    /// <summary>
    /// The item name a human sees — the text inside a hyperlink's brackets, or the raw value when it
    /// carries no link (which is what a hand-written fixture holds).
    ///
    /// Both the list column and the search go through here, deliberately: searching the raw link
    /// instead matched the item id, every bonus id, and the color code — so "cff" returned every row
    /// and "212446" returned one for a number that is nowhere on screen. A search can only be
    /// understood if it searches what is being shown.
    /// </summary>
    public static string ItemDisplayName(string? link)
    {
        if (string.IsNullOrEmpty(link)) return "";
        var m = ItemNamePattern.Match(link);
        return m.Success ? m.Groups[1].Value : link;
    }

    private static string? Str(ArchivedAward award, string key) =>
        award.EffectiveFields.TryGetValue(key, out var v) ? v as string : null;

    private static DateTimeOffset Time(ArchivedAward award) =>
        award.Fields.TryGetValue("time", out var v) && v is double d
            ? DateTimeOffset.FromUnixTimeSeconds((long)d)
            : DateTimeOffset.MinValue;
}
