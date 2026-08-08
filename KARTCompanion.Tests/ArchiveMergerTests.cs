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
}
