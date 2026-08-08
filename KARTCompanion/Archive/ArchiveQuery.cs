namespace KARTCompanion.Archive;

/// <summary>
/// Where an award stands relative to the two places it can be exported to. Withdrawn outranks every
/// export state: an award that was taken back is not "exported", it is gone.
/// </summary>
public enum AwardStatus
{
    Open,
    ExportedByAddon,
    ExportedByCompanion,
    ExportedByBoth,
    Withdrawn,
}

/// <summary>Answers questions of the archive: what state is this award in, and which awards match a filter.</summary>
public static class ArchiveQuery
{
    public static AwardStatus StatusOf(ArchivedAward award)
    {
        // The addon's "exported" field has three states: false is new, true is exported, and absent
        // means an entry written before the feature existed. Absent can't actually occur here — an
        // award with no minted id never reaches the archive at all — but if it ever did, treating it
        // as not-exported (rather than inventing a fourth AwardStatus) is the safe reading: it means
        // "we don't know", not "the addon exported it".
        if (award.Withdrawn) return AwardStatus.Withdrawn;

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
                (Str(a, "item")?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (Str(a, "reason")?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));

        return query.OrderByDescending(Time).ToList();
    }

    private static string? Str(ArchivedAward award, string key) =>
        award.Fields.TryGetValue(key, out var v) ? v as string : null;

    private static DateTimeOffset Time(ArchivedAward award) =>
        award.Fields.TryGetValue("time", out var v) && v is double d
            ? DateTimeOffset.FromUnixTimeSeconds((long)d)
            : DateTimeOffset.MinValue;
}
