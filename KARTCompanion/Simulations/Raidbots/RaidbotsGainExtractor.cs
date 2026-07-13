using KARTCompanion.Matching;

namespace KARTCompanion.Simulations.Raidbots;

/// <summary>
/// Turns a parsed RaidbotsReport into gain candidates. Unlike QE Live, Raidbots doesn't
/// precompute a percentage — we diff each profileset's mean against the player's baseline
/// (whichever of dps/hps is present) ourselves: gainPct = (mean - baseline) / baseline * 100.
/// </summary>
public static class RaidbotsGainExtractor
{
    public static List<GainCandidate> Extract(RaidbotsReport report, string? profileKey, DateTimeOffset importedAt)
    {
        var candidates = new List<GainCandidate>();

        var player = report.Sim.Players.FirstOrDefault();
        var baseline = player?.CollectedData.Dps ?? player?.CollectedData.Hps;
        if (baseline is null || baseline.Mean == 0)
            return candidates;

        foreach (var result in report.Sim.Profilesets.Results)
        {
            var parsed = ProfilesetNameParser.Parse(result.Name);
            if (parsed is null) continue;

            var gainPct = (result.Mean - baseline.Mean) / baseline.Mean * 100.0;
            candidates.Add(new GainCandidate(
                ItemId: parsed.ItemId,
                Ilvl: parsed.Ilvl,
                Slot: parsed.Slot,
                GainPct: Math.Round(gainPct, 2),
                Source: "raidbots",
                ProfileKey: profileKey,
                ImportedAt: importedAt));
        }

        return candidates;
    }
}
