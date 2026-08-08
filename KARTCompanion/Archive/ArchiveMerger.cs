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
///   * a wipe   — nothing at this award's epoch survived in the new snapshot
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
        // First occurrence wins: a duplicate derived key within one snapshot should not occur (see
        // LootHistoryEntry's own doc comment on why) and none exist in the real file, but a merge is
        // a bad place to discover otherwise — skip the repeat rather than aborting the whole pass.
        var seen = new Dictionary<string, LootHistoryEntry>();
        foreach (var entry in snapshot) seen.TryAdd(entry.Key, entry);

        int added = 0, updated = 0, withdrawn = 0;

        var byKey = new Dictionary<string, ArchivedAward>();
        foreach (var award in doc.Awards) byKey.TryAdd(award.Key, award);

        foreach (var entry in seen.Values)
        {
            if (byKey.TryGetValue(entry.Key, out var existing))
            {
                // An entry legitimately changes: exported flips false -> true, and the item link is
                // upgraded from the compact item string to the full link once the client resolves
                // it. Merge field-wise rather than replacing the dictionary outright: a field this
                // snapshot's source doesn't carry (color is missing from some read paths, present on
                // 104 of 133 real awards) must not be silently dropped from an award another source
                // already reported it on.
                var fieldsChanged = ApplyFields(existing, entry.Fields);
                var wasWithdrawn = existing.Withdrawn;
                existing.LastSeen = now;
                existing.SourceFile = sourceFile;
                // Seeing it again is the game telling us it is not gone after all.
                existing.Withdrawn = false;
                if (fieldsChanged || wasWithdrawn) updated++;
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
        var survivingEpochs = snapshot
            .Select(e => Epoch(e.Fields))
            .Where(e => e != null)
            .Select(e => e!.Value)
            .ToHashSet();

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

            // Explained by a wipe: nothing at this award's epoch survived into the new snapshot. A
            // single saved-variables file only ever holds one non-null epoch at a time — LH.AdoptEpoch
            // drops every entry below the new epoch, and LH.AdmitEpoch refuses entries above the
            // current one unless it adopts first — so "not among the surviving epochs" is the actual
            // rule, not a proxy for "is this award's epoch lower than the snapshot's highest": it also
            // covers a snapshot whose epoch went backwards, or one that lost its epoch fields
            // entirely, both of which are evidence the file is stale or foreign rather than that
            // anything was revoked.
            if (epoch != null && !survivingEpochs.Contains(epoch.Value)) continue;

            // Awards written before sub-project 1 carry no epoch; for those the wipe cause cannot be
            // established at all, and the harmless reading wins.
            if (epoch == null) continue;

            award.Withdrawn = true;
            withdrawn++;
        }

        return new MergeResult(added, updated, withdrawn);
    }

    // Applies `incoming` onto `existing.Fields` key-by-key — adding new keys and overwriting changed
    // ones — and reports whether anything actually changed. A field `incoming` doesn't carry is left
    // untouched rather than dropped; see LootHistoryEntry's own doc comment on why an absent field
    // must never be read as "known to be nothing."
    private static bool ApplyFields(ArchivedAward existing, IReadOnlyDictionary<string, object?> incoming)
    {
        var changed = false;
        foreach (var (key, value) in incoming)
        {
            if (!existing.Fields.TryGetValue(key, out var current) || !FieldsEqual(current, value))
            {
                existing.Fields[key] = value;
                changed = true;
            }
        }
        return changed;
    }

    // Fields can nest one level deep (color is a Dictionary<string, object?>); reference equality
    // would report "changed" on every merge just because the new snapshot built a fresh dictionary
    // instance with the same content.
    private static bool FieldsEqual(object? a, object? b)
    {
        if (a is Dictionary<string, object?> da && b is Dictionary<string, object?> db)
        {
            if (da.Count != db.Count) return false;
            foreach (var (key, value) in da)
            {
                if (!db.TryGetValue(key, out var otherValue) || !FieldsEqual(value, otherValue)) return false;
            }
            return true;
        }
        return Equals(a, b);
    }

    private static long Time(IReadOnlyDictionary<string, object?> f) =>
        f.TryGetValue("time", out var v) && v is double d ? (long)d : 0;

    private static long? Epoch(IReadOnlyDictionary<string, object?> f) =>
        f.TryGetValue("epoch", out var v) && v is double d ? (long)d : null;
}
