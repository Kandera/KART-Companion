using KARTCompanion.SavedVariables;

namespace KARTCompanion.Archive;

public sealed record MergeResult(int Added, int Updated, int Withdrawn);

/// <summary>
/// Folds one snapshot of the game's loot history into the archive.
///
/// The archive only ever sees snapshots — it never observes the addon REMOVING an entry, and the
/// addon does remove: on "No Winner", on a re-decision through /kart add, on a revoke. An
/// append-only archive would keep a revoked award forever and then write a line into WoWUtils
/// crediting somebody with an item they never received. On a reassignment the item would show
/// against player A while player B is the one who got it.
///
/// So a disappearance has to be explained. There are three causes and two of them leave evidence:
///   * a wipe   — the epoch rose; everything below it is gone
///   * the cap  — only ever the oldest BY TIMESTAMP (TrimHistory: "dropping the entry with the
///                OLDEST timestamp, not index 1", because insertion order stops matching chronology
///                once the catch-up backfills)
///   * a revoke — what is left: neither the oldest, nor explained by an epoch change
///
/// Nothing is deleted. A withdrawal is recorded, and a later export leaves those out.
/// </summary>
public static class ArchiveMerger
{
    public static MergeResult Merge(
        ArchiveDocument doc,
        IReadOnlyList<LootHistoryEntry> snapshot,
        string sourceFile,
        DateTimeOffset now)
    {
        var seen = snapshot.ToDictionary(e => e.Key, e => e);
        int added = 0, updated = 0, withdrawn = 0;

        var byKey = doc.Awards.ToDictionary(a => a.Key, a => a);

        foreach (var entry in snapshot)
        {
            if (byKey.TryGetValue(entry.Key, out var existing))
            {
                // An entry legitimately changes: exported flips false -> true, and the item link is
                // upgraded from the compact item string to the full link once the client resolves it.
                existing.Fields = new Dictionary<string, object?>(entry.Fields);
                existing.LastSeen = now;
                existing.SourceFile = sourceFile;
                // Seeing it again is the game telling us it is not gone after all.
                existing.Withdrawn = false;
                updated++;
            }
            else
            {
                doc.Awards.Add(new ArchivedAward
                {
                    Fields = new Dictionary<string, object?>(entry.Fields),
                    SourceFile = sourceFile,
                    FirstSeen = now,
                    LastSeen = now,
                });
                added++;
            }
        }

        // Nothing to compare against: an empty snapshot cannot distinguish a wipe from a file we
        // simply could not read, so it explains nothing and withdraws nothing.
        if (snapshot.Count == 0) return new MergeResult(added, updated, 0);

        var oldestSurvivingTime = snapshot.Min(e => Time(e.Fields));
        var highestEpoch = snapshot.Select(e => Epoch(e.Fields)).Where(e => e != null).DefaultIfEmpty(null).Max();

        foreach (var award in doc.Awards)
        {
            // Only reason about awards from the source this snapshot came from. Two account folders
            // are merged into one archive, and an award seen only in the other one has not vanished.
            if (award.SourceFile != sourceFile) continue;
            if (award.Withdrawn) continue;
            if (seen.ContainsKey(award.Key)) continue;

            var time = Time(award.Fields);
            var epoch = Epoch(award.Fields);

            // Explained by the cap: it is older than everything that survived.
            if (time <= oldestSurvivingTime) continue;

            // Explained by a wipe: the snapshot has moved to a higher epoch than this award's.
            // Awards written before sub-project 1 carry no epoch; for those this cause cannot be
            // established, and the harmless reading wins.
            if (highestEpoch != null && epoch != null && epoch < highestEpoch) continue;
            if (epoch == null) continue;

            award.Withdrawn = true;
            withdrawn++;
        }

        return new MergeResult(added, updated, withdrawn);
    }

    private static long Time(IReadOnlyDictionary<string, object?> f) =>
        f.TryGetValue("time", out var v) && v is double d ? (long)d : 0;

    private static long? Epoch(IReadOnlyDictionary<string, object?> f) =>
        f.TryGetValue("epoch", out var v) && v is double d ? (long)d : null;
}
