namespace KARTCompanion.Matching;

/// <summary>
/// One simulated item swap for a character, extracted from either a Raidbots or QE Live report.
/// This is the shape written into KART_WoWUtilsCache for the addon to read.
/// </summary>
public sealed record GainCandidate(
    int ItemId,
    int? Ilvl,
    string? Slot,
    double GainPct,
    string Source,
    string? ProfileKey,
    DateTimeOffset ImportedAt);
