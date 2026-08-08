using KARTCompanion.Archive;
using KARTCompanion.SavedVariables;

namespace KARTCompanion.Tests;

public class ArchiveMergerTests
{
    private const string Source = @"C:\wow\file.lua";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1785400000);

    private static LootHistoryEntry Entry(long time, string winnerKey, string item,
                                          string? id = null, long? epoch = null)
    {
        var f = new Dictionary<string, object?>
        {
            ["time"] = (double)time,
            ["winnerKey"] = winnerKey,
            ["item"] = item,
        };
        if (id != null) f["id"] = id;
        if (epoch != null) f["epoch"] = (double)epoch;
        return new LootHistoryEntry(f);
    }

    [Fact]
    public void Merge_SameSnapshotTwice_ChangesNothing()
    {
        var doc = new ArchiveDocument();
        var snapshot = new[] { Entry(100, "P-1", "itemA"), Entry(200, "P-2", "itemB") };

        var first = ArchiveMerger.Merge(doc, snapshot, Source, Now);
        var second = ArchiveMerger.Merge(doc, snapshot, Source, Now);

        Assert.Equal(2, first.Added);
        Assert.Equal(0, second.Added);
        Assert.Equal(2, doc.Awards.Count);
        Assert.All(doc.Awards, a => Assert.False(a.Withdrawn));
    }

    // The catch-up sync backfills awards OLDER than everything already stored. An archive that
    // assumed snapshots only grow at the end would never take them.
    [Fact]
    public void Merge_BackfilledOlderEntry_IsTakenIn()
    {
        var doc = new ArchiveDocument();
        ArchiveMerger.Merge(doc, new[] { Entry(200, "P-2", "itemB") }, Source, Now);

        ArchiveMerger.Merge(doc, new[] { Entry(100, "P-1", "itemA"), Entry(200, "P-2", "itemB") }, Source, Now);

        Assert.Equal(2, doc.Awards.Count);
        Assert.All(doc.Awards, a => Assert.False(a.Withdrawn));
    }

    // Only the oldest ever falls to the cap. TrimHistory says so itself: "Enforces
    // MAX_HISTORY_ENTRIES by dropping the entry with the OLDEST timestamp, not index 1."
    [Fact]
    public void Merge_OldestEntryGone_ReadsAsCapEvictionNotWithdrawal()
    {
        var doc = new ArchiveDocument();
        ArchiveMerger.Merge(doc, new[] { Entry(100, "P-1", "itemA"), Entry(200, "P-2", "itemB") }, Source, Now);

        ArchiveMerger.Merge(doc, new[] { Entry(200, "P-2", "itemB") }, Source, Now);

        var gone = doc.Awards.Single(a => a.Key.StartsWith("100|", StringComparison.Ordinal));
        Assert.False(gone.Withdrawn);
    }

    [Fact]
    public void Merge_EpochRose_ReadsAsWipeNotWithdrawal()
    {
        var doc = new ArchiveDocument();
        ArchiveMerger.Merge(doc, new[]
        {
            Entry(100, "P-1", "itemA", id: "a", epoch: 1),
            Entry(200, "P-2", "itemB", id: "b", epoch: 1),
        }, Source, Now);

        ArchiveMerger.Merge(doc, new[] { Entry(300, "P-3", "itemC", id: "c", epoch: 2) }, Source, Now);

        Assert.All(doc.Awards.Where(a => a.Key is "a" or "b"), a => Assert.False(a.Withdrawn));
    }

    // What is left when neither the cap nor a wipe explains it: the award was revoked or reassigned.
    // Exporting it anyway would credit player A with an item player B actually received.
    [Fact]
    public void Merge_MiddleEntryGoneWithoutCapOrWipe_ReadsAsWithdrawn()
    {
        var doc = new ArchiveDocument();
        ArchiveMerger.Merge(doc, new[]
        {
            Entry(100, "P-1", "itemA", id: "a", epoch: 1),
            Entry(200, "P-2", "itemB", id: "b", epoch: 1),
            Entry(300, "P-3", "itemC", id: "c", epoch: 1),
        }, Source, Now);

        var result = ArchiveMerger.Merge(doc, new[]
        {
            Entry(100, "P-1", "itemA", id: "a", epoch: 1),
            Entry(300, "P-3", "itemC", id: "c", epoch: 1),
        }, Source, Now);

        Assert.Equal(1, result.Withdrawn);
        Assert.True(doc.Awards.Single(a => a.Key == "b").Withdrawn);
        // Nothing is deleted. The archive keeps what the game forgot, including the fact that it did.
        Assert.Equal(3, doc.Awards.Count);
    }

    // Entries written before sub-project 1 carry no epoch at all, so the wipe cause cannot be
    // established for them. The rule must tolerate that rather than break on it, and the harmless
    // reading wins: treated as evicted, so the award stays exportable.
    [Fact]
    public void Merge_NoEpochAnywhere_DoesNotCrashAndPrefersTheHarmlessReading()
    {
        var doc = new ArchiveDocument();
        ArchiveMerger.Merge(doc, new[] { Entry(100, "P-1", "itemA"), Entry(200, "P-2", "itemB") }, Source, Now);

        ArchiveMerger.Merge(doc, Array.Empty<LootHistoryEntry>(), Source, Now);

        Assert.All(doc.Awards, a => Assert.False(a.Withdrawn));
    }

    [Fact]
    public void Merge_KnownAward_UpdatesTheFieldsThatLegitimatelyChange()
    {
        var doc = new ArchiveDocument();
        var before = Entry(100, "P-1", "item:249331", id: "a", epoch: 1);
        ArchiveMerger.Merge(doc, new[] { before }, Source, Now);

        var after = new Dictionary<string, object?>(before.Fields)
        {
            ["item"] = "|cffa335ee|Hitem:249331::::::::80:::::|h[Gloves]|h|r",
            ["exported"] = true,
        };
        var result = ArchiveMerger.Merge(doc, new[] { new LootHistoryEntry(after) }, Source, Now);

        Assert.Equal(1, result.Updated);
        var award = doc.Awards.Single();
        Assert.Equal("|cffa335ee|Hitem:249331::::::::80:::::|h[Gloves]|h|r", award.Fields["item"]);
        Assert.Equal(true, award.Fields["exported"]);
    }

    // Two account folders are read into one archive. An award seen only in one of them must not be
    // read as having vanished from the other.
    [Fact]
    public void Merge_SecondSource_DoesNotWithdrawTheFirstSourcesAwards()
    {
        var doc = new ArchiveDocument();
        ArchiveMerger.Merge(doc, new[] { Entry(100, "P-1", "itemA", id: "a", epoch: 1) }, Source, Now);

        ArchiveMerger.Merge(doc, new[] { Entry(200, "P-2", "itemB", id: "b", epoch: 1) }, @"D:\other.lua", Now);

        Assert.False(doc.Awards.Single(a => a.Key == "a").Withdrawn);
    }

    // The test above happens to have the first source's award older than everything in the second
    // source's snapshot, so the cap check alone would explain it away even without the source guard.
    // Give the first source's award the newer timestamp so only the guard can save it from being
    // read as a revoke.
    [Fact]
    public void Merge_SecondSourceWithNewerTimestamp_DoesNotWithdrawTheFirstSourcesAwards()
    {
        var doc = new ArchiveDocument();
        ArchiveMerger.Merge(doc, new[] { Entry(300, "P-1", "itemA", id: "a", epoch: 1) }, Source, Now);

        ArchiveMerger.Merge(doc, new[] { Entry(100, "P-2", "itemB", id: "b", epoch: 1) }, @"D:\other.lua", Now);

        Assert.False(doc.Awards.Single(a => a.Key == "a").Withdrawn);
    }

    // Merge_OldestEntryGone_ReadsAsCapEvictionNotWithdrawal always has the vanished award strictly
    // older than the new snapshot's minimum, so "<=" and "<" agree and the boundary itself is never
    // exercised. Force the equal-timestamp case: two awards share a timestamp, only one of them
    // survives, so the vanished one is exactly as old as the surviving snapshot's oldest entry.
    [Fact]
    public void Merge_EntryAtExactCapBoundary_ReadsAsCapEvictionNotWithdrawal()
    {
        var doc = new ArchiveDocument();
        ArchiveMerger.Merge(doc, new[]
        {
            Entry(100, "P-1", "itemA", id: "a", epoch: 1),
            Entry(100, "P-4", "itemD", id: "d", epoch: 1),
            Entry(200, "P-2", "itemB", id: "b", epoch: 1),
        }, Source, Now);

        // itemD drops out; itemA, at the same timestamp, is what survived as the new oldest.
        ArchiveMerger.Merge(doc, new[]
        {
            Entry(100, "P-1", "itemA", id: "a", epoch: 1),
            Entry(200, "P-2", "itemB", id: "b", epoch: 1),
        }, Source, Now);

        Assert.False(doc.Awards.Single(a => a.Key == "d").Withdrawn);
    }

    // Merge_NoEpochAnywhere_DoesNotCrashAndPrefersTheHarmlessReading merges an EMPTY snapshot, which
    // short-circuits before the withdrawal logic ever runs — it never actually reaches the
    // epoch-is-null bail-out. Exercise the bail-out for real: a pre-epoch award disappears from a
    // non-empty snapshot that otherwise carries epochs, and is neither the oldest nor explained by an
    // epoch rise, so only the "unknown epoch" tolerance keeps it from being read as a revoke.
    [Fact]
    public void Merge_PreEpochEntryGoneAmongEpochedEntries_ReadsAsHarmlessNotWithdrawn()
    {
        var doc = new ArchiveDocument();
        ArchiveMerger.Merge(doc, new[]
        {
            Entry(100, "P-1", "itemZ", id: "z"),
            Entry(200, "P-2", "itemA", id: "a"),
            Entry(300, "P-3", "itemC", id: "c", epoch: 1),
        }, Source, Now);

        // itemA (no epoch, not the oldest) drops out.
        ArchiveMerger.Merge(doc, new[]
        {
            Entry(100, "P-1", "itemZ", id: "z"),
            Entry(300, "P-3", "itemC", id: "c", epoch: 1),
        }, Source, Now);

        Assert.False(doc.Awards.Single(a => a.Key == "a").Withdrawn);
    }

    // Review round 2: IMPORTANT 1 — the epoch/wipe branch had zero coverage. Every existing wipe
    // test has the surviving snapshot entries newer than the archived awards, so the cap check
    // ("older than everything that survived") already explains them away before the epoch branch is
    // ever reached. Force the cap check to miss: the new epoch's own history restarts with an entry
    // OLDER than what the wiped epoch had reached, so only the epoch branch can save a and b.
    [Fact]
    public void Merge_WipeWithOlderSurvivingEntries_DoesNotWithdrawPreWipeAwards()
    {
        var doc = new ArchiveDocument();
        ArchiveMerger.Merge(doc, new[]
        {
            Entry(500, "P-1", "itemA", id: "a", epoch: 1),
            Entry(600, "P-2", "itemB", id: "b", epoch: 1),
        }, Source, Now);

        ArchiveMerger.Merge(doc, new[] { Entry(100, "P-3", "itemC", id: "c", epoch: 2) }, Source, Now);

        Assert.All(doc.Awards.Where(x => x.Key is "a" or "b"), x => Assert.False(x.Withdrawn));
    }

    // Review round 2: IMPORTANT 2 — the predicate must ask "did anything at this award's epoch
    // survive", not "is this award below the snapshot's highest epoch". The two agree whenever a
    // saved-variables file holds a single epoch (which is all the addon ever produces), so this is
    // not a behavior change against real files — it is stating the rule we actually mean instead of
    // a proxy for it, so it stays right if that premise ever changes. Covered indirectly by the
    // wipe test above and directly by the regression tests below (Minor 6).

    // Review round 2: IMPORTANT 3 — the un-withdrawal was untested. A previously-withdrawn award
    // reappearing in a later snapshot must have its Withdrawn flag cleared: the game showing it
    // again outranks the earlier inference that it was gone.
    [Fact]
    public void Merge_WithdrawnAwardReappears_ClearsTheWithdrawnFlag()
    {
        var doc = new ArchiveDocument();
        ArchiveMerger.Merge(doc, new[]
        {
            Entry(100, "P-1", "itemA", id: "a", epoch: 1),
            Entry(200, "P-2", "itemB", id: "b", epoch: 1),
            Entry(300, "P-3", "itemC", id: "c", epoch: 1),
        }, Source, Now);

        ArchiveMerger.Merge(doc, new[]
        {
            Entry(100, "P-1", "itemA", id: "a", epoch: 1),
            Entry(300, "P-3", "itemC", id: "c", epoch: 1),
        }, Source, Now); // b withdrawn

        Assert.True(doc.Awards.Single(x => x.Key == "b").Withdrawn);

        ArchiveMerger.Merge(doc, new[]
        {
            Entry(100, "P-1", "itemA", id: "a", epoch: 1),
            Entry(200, "P-2", "itemB", id: "b", epoch: 1),
            Entry(300, "P-3", "itemC", id: "c", epoch: 1),
        }, Source, Now); // b reappears

        Assert.False(doc.Awards.Single(x => x.Key == "b").Withdrawn);
    }

    // Review round 2: IMPORTANT 4 — updating a known award must merge fields, not replace the
    // dictionary wholesale. A later snapshot from a source that doesn't resolve `color` must not
    // erase `color` from an award another snapshot already reported it on.
    [Fact]
    public void Merge_KnownAward_PreservesFieldsTheNewerSnapshotLacks()
    {
        var doc = new ArchiveDocument();
        var color = new Dictionary<string, object?> { ["r"] = 1.0, ["g"] = 0.498, ["b"] = 0.847 };
        var beforeFields = new Dictionary<string, object?>(Entry(100, "P-1", "item:249331", id: "a", epoch: 1).Fields)
        {
            ["color"] = color,
        };
        ArchiveMerger.Merge(doc, new[] { new LootHistoryEntry(beforeFields) }, Source, Now);

        // A later snapshot for the same award, from a source that doesn't carry `color` at all.
        var afterFields = new Dictionary<string, object?>(beforeFields);
        afterFields.Remove("color");
        ArchiveMerger.Merge(doc, new[] { new LootHistoryEntry(afterFields) }, Source, Now);

        var award = doc.Awards.Single();
        var resultColor = Assert.IsType<Dictionary<string, object?>>(award.Fields["color"]);
        Assert.Equal(1.0, resultColor["r"]);
        Assert.Equal(0.498, resultColor["g"]);
        Assert.Equal(0.847, resultColor["b"]);
    }

    // Review round 2: MINOR 5 — Updated must count actual changes, not every already-known award.
    // Against the real file, an idle sync (nothing changed) would otherwise report Updated: 133
    // every single run.
    [Fact]
    public void Merge_IdleRun_ReportsZeroUpdated()
    {
        var doc = new ArchiveDocument();
        var snapshot = new[]
        {
            Entry(100, "P-1", "itemA", id: "a", epoch: 1),
            Entry(200, "P-2", "itemB", id: "b", epoch: 1),
        };
        ArchiveMerger.Merge(doc, snapshot, Source, Now);

        var result = ArchiveMerger.Merge(doc, snapshot, Source, Now);

        Assert.Equal(0, result.Updated);
    }

    // Review round 2: MINOR 6 (part 1) — an epoch that regresses is evidence the file is stale or
    // foreign, not that anything at the higher epoch was revoked. The harmless reading must win,
    // the same way it does for an award with no epoch at all.
    [Fact]
    public void Merge_EpochRegressed_DoesNotWithdrawTheHigherEpochsAwards()
    {
        var doc = new ArchiveDocument();
        ArchiveMerger.Merge(doc, new[] { Entry(300, "P-1", "itemA", id: "a", epoch: 5) }, Source, Now);

        // The new snapshot's epoch is LOWER than what's archived.
        ArchiveMerger.Merge(doc, new[] { Entry(100, "P-2", "itemB", id: "b", epoch: 4) }, Source, Now);

        Assert.False(doc.Awards.Single(x => x.Key == "a").Withdrawn);
    }

    // Review round 2: MINOR 6 (part 2) — a snapshot whose entries carry no epoch at all must read
    // the same as a stale/foreign file, not as a revoke of every epoched award.
    [Fact]
    public void Merge_SnapshotEntirelyLostEpochInfo_DoesNotWithdrawEpochedAwards()
    {
        var doc = new ArchiveDocument();
        ArchiveMerger.Merge(doc, new[] { Entry(300, "P-1", "itemA", id: "a", epoch: 5) }, Source, Now);

        // No entry in this snapshot carries an epoch field at all.
        ArchiveMerger.Merge(doc, new[] { Entry(100, "P-2", "itemB", id: "b") }, Source, Now);

        Assert.False(doc.Awards.Single(x => x.Key == "a").Withdrawn);
    }

    // Review round 2: MINOR 7 — the already-withdrawn guard was untested. An award that stays gone
    // across multiple later merges must not be re-counted in MergeResult.Withdrawn each time.
    [Fact]
    public void Merge_AlreadyWithdrawnAward_IsNotRecountedOnLaterMerges()
    {
        var doc = new ArchiveDocument();
        ArchiveMerger.Merge(doc, new[]
        {
            Entry(100, "P-1", "itemA", id: "a", epoch: 1),
            Entry(200, "P-2", "itemB", id: "b", epoch: 1),
            Entry(300, "P-3", "itemC", id: "c", epoch: 1),
        }, Source, Now);

        var remaining = new[]
        {
            Entry(100, "P-1", "itemA", id: "a", epoch: 1),
            Entry(300, "P-3", "itemC", id: "c", epoch: 1),
        };
        var first = ArchiveMerger.Merge(doc, remaining, Source, Now); // b withdrawn here
        Assert.Equal(1, first.Withdrawn);

        var second = ArchiveMerger.Merge(doc, remaining, Source, Now); // b still gone, already withdrawn

        Assert.Equal(0, second.Withdrawn);
    }

    // Review round 2: MINOR 9 — NOT fixed. The prescribed fix (normalize the item field to its bare
    // item id when building the derived key, so a compact "item:249331" reference and its later
    // resolved chat link agree) was implemented and then reverted after testing it against the real
    // fixture: KARTCompanion.Tests.Fixtures\loot-history-real.lua contains 11 groups (39 of the 133
    // real awards) where the SAME base item id was awarded to the SAME winnerKey in the SAME second
    // with DIFFERENT bonus-id suffixes (item-level test grants, e.g. eight distinct "Litany of
    // Lightblind Wrath" variants to one winner at one timestamp). Collapsing the item field to its
    // bare id merged those into 11 derived keys, regressing
    // LootHistoryReaderTests.Read_RealFile_HasNoIdsAndDerivedKeysAreUnique from 133 unique keys to
    // 94 — the archive would have silently conflated genuinely distinct awards, which is the same
    // class of bug Minor 9 was raised to fix, just worse and silent. A bare reference is always
    // byte-identical to any other bare reference of the same item (there is nothing in it to
    // normalize), so a fix scoped narrowly enough to leave resolved links untouched cannot make a
    // bare and a resolved form agree either — it would be a no-op. No key-normalization fix closes
    // this gap without discarding the exact information that keeps real distinct awards apart. See
    // the round-2 report for the recommendation (handle it below the derived key, not in it).

    // Review round 2: MINOR 10 — a duplicate derived key within one snapshot must not abort the
    // whole merge. The design says this doesn't occur and the real file has none, but an unhandled
    // exception discarding an entire pass over one bad entry is the wrong failure mode.
    [Fact]
    public void Merge_DuplicateKeysWithinOneSnapshot_SkipsInsteadOfThrowing()
    {
        var doc = new ArchiveDocument();
        var duplicate1 = Entry(100, "P-1", "itemA");
        var duplicate2 = Entry(100, "P-1", "itemA"); // same derived key as duplicate1

        var result = ArchiveMerger.Merge(doc, new[] { duplicate1, duplicate2 }, Source, Now);

        Assert.Single(doc.Awards);
        Assert.Equal(1, result.Added);
    }
}
