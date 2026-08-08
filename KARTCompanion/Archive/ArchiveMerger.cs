using KARTCompanion.SavedVariables;

namespace KARTCompanion.Archive;

public sealed record MergeResult(int Added, int Updated, int Withdrawn, int Skipped);

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
///                once the catch-up backfills), and only out of a snapshot that is actually AT
///                MAX_HISTORY_ENTRIES — below it the cap has evicted nothing, so being the oldest
///                is no evidence at all
///   * a revoke — what is left: neither the oldest, nor explained by an epoch change
///
/// Nothing is deleted. A withdrawal is recorded, and a later export leaves those out.
///
/// An entry with no id is never archived at all, regardless of any of the above. The maintainer's
/// 133 real pre-release entries have no id, and they are not distinct awards: they are the same
/// award observed once per syncing client, from bugs the id-minting release fixed. There is no key
/// that can tell those apart from a genuine repeat without the id, so entries without one are
/// counted and skipped rather than given a synthetic key that would misrepresent them.
/// </summary>
public static class ArchiveMerger
{
    /// <summary>
    /// The addon's MAX_HISTORY_ENTRIES (KeineAhnungRaidTools, LootHistory.lua). The cap can only
    /// have evicted anything from a snapshot that is actually at it, so the cap excuse is gated on
    /// this — see the comment at the gate itself.
    ///
    /// This is the one place the addon's constant is duplicated into the Companion. If the addon
    /// ever changes it, the error moves in a known direction:
    ///   * addon cap LARGER than this value  — the Companion excuses too often. Harmless: at worst
    ///     a revoked award stays exportable and shows up as a duplicate, which is visible.
    ///   * addon cap SMALLER than this value — the Companion never excuses, and marks awards the
    ///     cap genuinely evicted as withdrawn. Harmful: that data leaves the export silently.
    /// So a stale value here is only safe while it is not below the addon's.
    /// </summary>
    public const int AddonHistoryCap = 500;

    public static MergeResult Merge(
        ArchiveDocument doc,
        IReadOnlyList<LootHistoryEntry> snapshot,
        string sourceFile,
        DateTimeOffset now)
    {
        int added = 0, updated = 0, withdrawn = 0, skipped = 0;

        // First occurrence wins: a duplicate id within one snapshot should not occur, but a merge is
        // a bad place to discover otherwise — skip the repeat rather than aborting the whole pass.
        var seen = new Dictionary<string, LootHistoryEntry>();
        foreach (var entry in snapshot)
        {
            if (entry.Id == null) { skipped++; continue; }
            seen.TryAdd(entry.Id, entry);
        }

        var byKey = new Dictionary<string, ArchivedAward>();
        foreach (var award in doc.Awards) byKey.TryAdd(award.Key, award);

        foreach (var entry in seen.Values)
        {
            if (byKey.TryGetValue(entry.Id!, out var existing))
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
        if (snapshot.Count == 0) return new MergeResult(added, updated, 0, skipped);

        // A snapshot entry missing a usable "time" must not corrupt this boundary. Excluded here
        // rather than contributing Time()'s default of 0, which would drag oldestSurvivingTime down
        // to 0 and silently disable the cap check for every award in this pass — turning a correct
        // excuse into a wrongful withdrawal. If NOTHING in the snapshot has a usable time, the
        // boundary can't be established at all; the harmless reading wins there too, same as
        // everywhere else in this method.
        var survivingTimes = snapshot
            .Select(e => TimeOrNull(e.Fields))
            .Where(t => t != null)
            .Select(t => t!.Value)
            .ToList();
        var oldestSurvivingTime = survivingTimes.Count > 0 ? (long?)survivingTimes.Min() : null;

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

            // The boundary could not be established at all — nothing in the snapshot carries a
            // usable time. The harmless reading wins, same as everywhere else in this method. See
            // the comment on survivingTimes above.
            if (oldestSurvivingTime == null) continue;

            // Explained by the cap: it is older than everything that survived — but ONLY if a cap
            // eviction could have happened in the first place. Ordering alone is not evidence: in a
            // snapshot well under the cap the oldest award is simply the oldest award, so a revoke
            // of it looks exactly like an eviction and would be excused at any file size. That is
            // not a rare shape — the file is empty after sub-project 1's one-time purge and empty
            // again after every raid-wide wipe, so for the first weeks of an epoch the oldest entry
            // in the file IS a recent award, and re-deciding the first item of the night through
            // /kart add is ordinary lootmaster behaviour.
            if (snapshot.Count >= AddonHistoryCap && time <= oldestSurvivingTime) continue;

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

        return new MergeResult(added, updated, withdrawn, skipped);
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

    private static long? TimeOrNull(IReadOnlyDictionary<string, object?> f) =>
        f.TryGetValue("time", out var v) && v is double d ? (long)d : null;

    private static long? Epoch(IReadOnlyDictionary<string, object?> f) =>
        f.TryGetValue("epoch", out var v) && v is double d ? (long)d : null;
}
