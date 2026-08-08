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

    // The cap excuse now requires the snapshot to actually be at the addon's MAX_HISTORY_ENTRIES —
    // below it the cap has evicted nothing. Pads a snapshot up to that size with awards newer than
    // anything these tests care about, so the padding can never become the oldest survivor and can
    // never change a verdict. Only the snapshot of the merge under test needs padding: the pads are
    // simply added, and they are still there on every later merge in the same test.
    private static LootHistoryEntry[] PaddedToCap(params LootHistoryEntry[] entries)
    {
        var pads = Enumerable.Range(0, ArchiveMerger.AddonHistoryCap - entries.Length)
            .Select(i => Entry(1_000_000 + i, "P-pad", "itemPad", id: "pad-" + i, epoch: 1));
        return entries.Concat(pads).ToArray();
    }

    [Fact]
    public void Merge_SameSnapshotTwice_ChangesNothing()
    {
        var doc = new ArchiveDocument();
        var snapshot = new[] { Entry(100, "P-1", "itemA", id: "a"), Entry(200, "P-2", "itemB", id: "b") };

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
        ArchiveMerger.Merge(doc, new[] { Entry(200, "P-2", "itemB", id: "b") }, Source, Now);

        ArchiveMerger.Merge(doc, new[] { Entry(100, "P-1", "itemA", id: "a"), Entry(200, "P-2", "itemB", id: "b") },
            Source, Now);

        Assert.Equal(2, doc.Awards.Count);
        Assert.All(doc.Awards, a => Assert.False(a.Withdrawn));
    }

    // Only the oldest ever falls to the cap. TrimHistory says so itself: "Enforces
    // MAX_HISTORY_ENTRIES by dropping the entry with the OLDEST timestamp, not index 1."
    //
    // Final review, IMPORTANT 1: the surviving snapshot has to be AT the cap for that sentence to
    // apply, and both awards carry an epoch the snapshot still has, so the cap branch is the only
    // thing that can excuse "old" here.
    [Fact]
    public void Merge_OldestEntryGone_ReadsAsCapEvictionNotWithdrawal()
    {
        var doc = new ArchiveDocument();
        ArchiveMerger.Merge(doc, new[]
        {
            Entry(100, "P-1", "itemA", id: "old", epoch: 1),
            Entry(200, "P-2", "itemB", id: "new", epoch: 1),
        }, Source, Now);

        ArchiveMerger.Merge(doc, PaddedToCap(Entry(200, "P-2", "itemB", id: "new", epoch: 1)), Source, Now);

        var gone = doc.Awards.Single(a => a.Key == "old");
        Assert.False(gone.Withdrawn);
    }

    // Final review, IMPORTANT 1 — the other side of the same gate, and the reviewer's own probe.
    // The cap branch used to test ORDERING ONLY, so ANY revoke of the oldest surviving award was
    // read as a cap eviction at any file size. Here the snapshot is 498 entries short of the cap:
    // nothing can have been evicted, so the disappearance of the oldest award is a revoke, and an
    // export that credited that player would hand them an item that was taken back off them.
    [Fact]
    public void Merge_OldestAwardGoneFromASnapshotFarBelowTheCap_ReadsAsWithdrawn()
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
            Entry(200, "P-2", "itemB", id: "b", epoch: 1),
            Entry(300, "P-3", "itemC", id: "c", epoch: 1),
        }, Source, Now);

        Assert.Equal(1, result.Withdrawn);
        Assert.True(doc.Awards.Single(a => a.Key == "a").Withdrawn);
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

    // An award with no epoch at all (nothing in the file has resolved one yet, or this build never
    // populates it for some other reason) must not crash the wipe check, and the harmless reading
    // wins: treated as evicted, so the award stays exportable.
    [Fact]
    public void Merge_NoEpochAnywhere_DoesNotCrashAndPrefersTheHarmlessReading()
    {
        var doc = new ArchiveDocument();
        ArchiveMerger.Merge(doc, new[] { Entry(100, "P-1", "itemA", id: "a"), Entry(200, "P-2", "itemB", id: "b") },
            Source, Now);

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

        // itemD drops out; itemA, at the same timestamp, is what survived as the new oldest. At the
        // cap, so the cap excuse applies at all (final review, IMPORTANT 1).
        ArchiveMerger.Merge(doc, PaddedToCap(
            Entry(100, "P-1", "itemA", id: "a", epoch: 1),
            Entry(200, "P-2", "itemB", id: "b", epoch: 1)), Source, Now);

        Assert.False(doc.Awards.Single(a => a.Key == "d").Withdrawn);
    }

    // Merge_NoEpochAnywhere_DoesNotCrashAndPrefersTheHarmlessReading merges an EMPTY snapshot, which
    // short-circuits before the withdrawal logic ever runs — it never actually reaches the
    // epoch-is-null bail-out. Exercise the bail-out for real: a no-epoch award disappears from a
    // non-empty snapshot that otherwise carries epochs, and is neither the oldest nor explained by an
    // epoch rise, so only the "unknown epoch" tolerance keeps it from being read as a revoke.
    [Fact]
    public void Merge_NoEpochEntryGoneAmongEpochedEntries_ReadsAsHarmlessNotWithdrawn()
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

    // Review round 2 revisited: MINOR 9 turned out not to be a key-normalization problem at all.
    // The re-review established that the 133 no-id entries in the real fixture are not distinct
    // awards under any key — they are the SAME award observed once per syncing client, from bugs the
    // id-minting release fixed (see LootHistoryReaderTests.Read_RealFile_PreReleaseEntriesHaveNoId).
    // The maintainer ruled those entries are not archived at all. See MINOR 9 (ROUND 3) below for
    // what replaced it, and the round-3 report section for the corrected root cause — the round-2
    // report and commit message still record the original, wrong theory ("distinct item variants")
    // and are deliberately left unedited; the correction lives in the report instead.

    // Review round 3: id-less entries are never archived. There is no key that can tell a genuine
    // repeat apart from the same award observed once per syncing client without the id, so an entry
    // without one is counted and skipped rather than given a synthetic key.
    [Fact]
    public void Merge_SnapshotOfIdLessEntries_ArchivesNothingAndReportsSkipped()
    {
        var doc = new ArchiveDocument();

        var result = ArchiveMerger.Merge(doc, new[]
        {
            Entry(100, "P-1", "itemA"),
            Entry(200, "P-2", "itemB"),
        }, Source, Now);

        Assert.Empty(doc.Awards);
        Assert.Equal(0, result.Added);
        Assert.Equal(2, result.Skipped);
    }

    // A snapshot can mix id-less entries (skipped) with proper ones (archived normally) — the two
    // don't interfere with each other's counts.
    [Fact]
    public void Merge_MixOfIdAndIdLessEntries_ArchivesOnlyTheIdedOnes()
    {
        var doc = new ArchiveDocument();

        var result = ArchiveMerger.Merge(doc, new[]
        {
            Entry(100, "P-1", "itemA"),
            Entry(200, "P-2", "itemB", id: "b"),
        }, Source, Now);

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Skipped);
        Assert.Equal("b", Assert.Single(doc.Awards).Key);
    }

    // Review round 3, MINOR 10 (rescoped): a duplicate real id within one snapshot must not abort
    // the whole merge, the same defensive reasoning as before — just against ids instead of the
    // now-removed derived key. This case only arises against real ids now.
    [Fact]
    public void Merge_DuplicateIdWithinOneSnapshot_SkipsInsteadOfThrowing()
    {
        var doc = new ArchiveDocument();
        var duplicate1 = Entry(100, "P-1", "itemA", id: "a");
        var duplicate2 = Entry(100, "P-1", "itemA", id: "a"); // same id as duplicate1

        var result = ArchiveMerger.Merge(doc, new[] { duplicate1, duplicate2 }, Source, Now);

        Assert.Single(doc.Awards);
        Assert.Equal(1, result.Added);
    }

    // Review round 3, the re-review's time-less hole: Time() defaults to 0 for an entry missing
    // "time" (nil, non-numeric, or absent), and the cap boundary used to be computed as
    // snapshot.Min(Time), so a single such entry dragged oldestSurvivingTime down to 0 and silently
    // disabled the cap check for the WHOLE pass — turning a correct cap-eviction excuse into a
    // wrongful withdrawal. "old" here is genuinely cap-evicted (older than every entry that has a
    // usable time in the new snapshot); the time-less entry must not be able to prevent that reading.
    [Fact]
    public void Merge_SnapshotHasEntryWithoutTime_DoesNotDisableTheCapCheck()
    {
        var doc = new ArchiveDocument();
        ArchiveMerger.Merge(doc, new[]
        {
            Entry(100, "P-1", "itemA", id: "old", epoch: 1),
            Entry(200, "P-2", "itemB", id: "b", epoch: 1),
        }, Source, Now);

        var timeless = new Dictionary<string, object?>
        {
            ["winnerKey"] = "P-3",
            ["item"] = "itemC",
            ["id"] = "c",
            ["epoch"] = 1.0,
        }; // deliberately no "time" key at all

        ArchiveMerger.Merge(doc,
            PaddedToCap(Entry(200, "P-2", "itemB", id: "b", epoch: 1), new LootHistoryEntry(timeless)),
            Source, Now);

        Assert.False(doc.Awards.Single(x => x.Key == "old").Withdrawn);
    }

    // Final review, IMPORTANT 1 (the same gate's other half): if NOTHING in a non-empty snapshot
    // carries a usable time, the cap boundary cannot be established at all. That tolerance is
    // separate from the at-the-cap gate — it must keep holding regardless of snapshot size, or a
    // file whose times this build cannot read would withdraw every award it does not list.
    [Fact]
    public void Merge_SnapshotHasNoUsableTimeAtAll_DoesNotWithdrawAnything()
    {
        var doc = new ArchiveDocument();
        ArchiveMerger.Merge(doc, new[]
        {
            Entry(100, "P-1", "itemA", id: "a", epoch: 1),
            Entry(200, "P-2", "itemB", id: "b", epoch: 1),
        }, Source, Now);

        var timeless = new Dictionary<string, object?>
        {
            ["winnerKey"] = "P-3",
            ["item"] = "itemC",
            ["id"] = "c",
            ["epoch"] = 1.0,
        }; // deliberately no "time" key at all — and it is the whole snapshot

        ArchiveMerger.Merge(doc, new[] { new LootHistoryEntry(timeless) }, Source, Now);

        Assert.All(doc.Awards.Where(x => x.Key is "a" or "b"), x => Assert.False(x.Withdrawn));
    }
}
