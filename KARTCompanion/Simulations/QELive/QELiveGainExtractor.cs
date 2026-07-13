using KARTCompanion.Matching;

namespace KARTCompanion.Simulations.QELive;

/// <summary>
/// Turns a parsed QELiveReport into gain candidates. Unlike Raidbots, QE Live precomputes the
/// percentage gain itself (percDiff) — no baseline diffing needed here.
/// </summary>
public static class QELiveGainExtractor
{
    public static List<GainCandidate> Extract(QELiveReport report, string? profileKey, DateTimeOffset importedAt)
    {
        var candidates = new List<GainCandidate>(report.Results.Count);

        foreach (var result in report.Results)
        {
            candidates.Add(new GainCandidate(
                ItemId: result.Item,
                Ilvl: result.Level,
                Slot: null, // QE Live doesn't report a slot string; itemId alone is enough to match.
                GainPct: Math.Round(result.PercDiff, 2),
                Source: "qelive",
                ProfileKey: profileKey,
                ImportedAt: importedAt));
        }

        return candidates;
    }
}
