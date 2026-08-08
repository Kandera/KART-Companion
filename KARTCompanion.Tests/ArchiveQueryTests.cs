using KARTCompanion.Archive;

namespace KARTCompanion.Tests;

public class ArchiveQueryTests
{
    private static ArchivedAward Award(
        string id, long time, string winner, string item, string reason,
        bool addonExported = false, bool withdrawn = false, DateTimeOffset? companionExported = null,
        bool excluded = false, string? editedWinner = null)
    {
        var fields = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["time"] = (double)time,
            ["winner"] = winner,
            ["item"] = item,
            ["reason"] = reason,
        };
        if (addonExported) fields["exported"] = true;

        var award = new ArchivedAward
        {
            Fields = fields,
            Withdrawn = withdrawn,
            ExportedByCompanionAt = companionExported,
            ExcludedFromExport = excluded,
        };
        if (editedWinner != null) AwardEditor.Set(award, "winner", editedWinner);
        return award;
    }

    // "Still open" means NEITHER exporter has taken it. The addon's mark reaches the archive on the
    // next read; the Companion's is set here. Neither can see the other's on the addon side, which
    // is exactly why the Companion has to count both.
    [Fact]
    public void StatusOf_TellsTheFourExportStatesApart()
    {
        Assert.Equal(AwardStatus.Open, ArchiveQuery.StatusOf(Award("a", 1, "A", "i", "BIS")));
        Assert.Equal(AwardStatus.ExportedByAddon,
            ArchiveQuery.StatusOf(Award("b", 1, "A", "i", "BIS", addonExported: true)));
        Assert.Equal(AwardStatus.ExportedByCompanion,
            ArchiveQuery.StatusOf(Award("c", 1, "A", "i", "BIS", companionExported: DateTimeOffset.UnixEpoch)));
        Assert.Equal(AwardStatus.ExportedByBoth,
            ArchiveQuery.StatusOf(Award("d", 1, "A", "i", "BIS", addonExported: true, companionExported: DateTimeOffset.UnixEpoch)));
    }

    // Withdrawn outranks everything: an award that was taken back is not "exported", it is gone.
    [Fact]
    public void StatusOf_WithdrawnOutranksAnyExportMark()
    {
        var award = Award("a", 1, "A", "i", "BIS", addonExported: true, withdrawn: true);

        Assert.Equal(AwardStatus.Withdrawn, ArchiveQuery.StatusOf(award));
    }

    [Fact]
    public void StatusOf_ExcludedOutranksEveryExportMarkButNotWithdrawn()
    {
        var excluded = Award("a1", 100, "Bramblewick", "[Blade]", "BIS", excluded: true);
        var excludedAndExported = Award("a2", 100, "Bramblewick", "[Blade]", "BIS",
            excluded: true, addonExported: true);
        var withdrawnAndExcluded = Award("a3", 100, "Bramblewick", "[Blade]", "BIS",
            excluded: true, withdrawn: true);

        Assert.Equal(AwardStatus.Excluded, ArchiveQuery.StatusOf(excluded));
        Assert.Equal(AwardStatus.Excluded, ArchiveQuery.StatusOf(excludedAndExported));
        // Withdrawn stays the strongest fact: the game itself dropped the row.
        Assert.Equal(AwardStatus.Withdrawn, ArchiveQuery.StatusOf(withdrawnAndExcluded));
    }

    [Fact]
    public void Filter_PlayerMatchesTheCorrectedWinnerNotTheAddonsOwn()
    {
        var award = Award("a1", 100, "Bramblewick", "[Blade]", "BIS", editedWinner: "Thornfell");
        var awards = new[] { award };

        Assert.Single(ArchiveQuery.Filter(awards, "Thornfell", null, null, null, null));
        Assert.Empty(ArchiveQuery.Filter(awards, "Bramblewick", null, null, null, null));
    }

    [Fact]
    public void Filter_SearchMatchesACorrectedReason()
    {
        var award = Award("a1", 100, "Bramblewick", "[Blade]", "BIS");
        AwardEditor.Set(award, "reason", "Offspec");
        var awards = new[] { award };

        Assert.Single(ArchiveQuery.Filter(awards, null, null, null, null, "Offspec"));
        Assert.Empty(ArchiveQuery.Filter(awards, null, null, null, null, "BIS"));
    }

    [Fact]
    public void Filter_ByPlayer_ReturnsOnlyThatPlayersAwards()
    {
        var awards = new[]
        {
            Award("a", 100, "Alric", "gloves", "BIS"),
            Award("b", 200, "Sinja", "boots", "Upgrade"),
        };

        var result = ArchiveQuery.Filter(awards, player: "Alric", from: null, to: null, status: null, search: null);

        Assert.Equal("a", Assert.Single(result).Key);
    }

    [Fact]
    public void Filter_ByTimeframe_IsInclusiveAtBothEnds()
    {
        var awards = new[]
        {
            Award("a", 100, "Alric", "gloves", "BIS"),
            Award("b", 200, "Sinja", "boots", "Upgrade"),
            Award("c", 300, "Alric", "ring", "BIS"),
        };

        var result = ArchiveQuery.Filter(awards, null,
            from: DateTimeOffset.FromUnixTimeSeconds(100), to: DateTimeOffset.FromUnixTimeSeconds(200),
            status: null, search: null);

        Assert.Equal(new[] { "a", "b" }, result.Select(a => a.Key).OrderBy(k => k));
    }

    // The search covers item and reason, and nothing else — a search that also matched the player
    // would make the player filter redundant and surprising.
    [Fact]
    public void Filter_BySearch_MatchesItemAndReasonButNotPlayer()
    {
        var awards = new[]
        {
            Award("a", 100, "Alric", "Lightblood Greaves", "BIS"),
            Award("b", 200, "Sinja", "Lichtlose Klage", "Transmog"),
            Award("c", 300, "Lightblood", "boots", "Upgrade"),
        };

        var byItem = ArchiveQuery.Filter(awards, null, null, null, null, search: "lightblood");

        Assert.Equal("a", Assert.Single(byItem).Key);

        // The reason half of the same claim. Without this the search could stop looking at reason
        // altogether and every test still passed — "lightblood" is an item value, and award c's
        // winner is the only thing standing in for "but not player".
        var byReason = ArchiveQuery.Filter(awards, null, null, null, null, search: "transmog");

        Assert.Equal("b", Assert.Single(byReason).Key);

        // And nothing matches a player name that appears in no item and no reason.
        Assert.Empty(ArchiveQuery.Filter(awards, null, null, null, null, search: "Sinja"));
    }

    // The list shows the item's display name, not the raw hyperlink, so the search has to look at
    // the same text. Searching the link matched item ids, bonus ids and the color code "cff" — none
    // of which is on screen, so a search for "212446" or "cff" returned rows for no visible reason.
    [Fact]
    public void Filter_BySearch_MatchesTheDisplayedItemNameNotTheRawLink()
    {
        const string link = "|cffa335ee|Hitem:212446::::::::80:71::5:3:10356:10353:1485:1:28:2164:::|h[Reverent Gnawer's Fang]|h|r";
        var awards = new[]
        {
            Award("a", 100, "Alric", link, "BIS"),
            Award("b", 200, "Sinja", "boots", "Upgrade"),
        };

        Assert.Equal("a", Assert.Single(ArchiveQuery.Filter(awards, null, null, null, null, search: "gnawer")).Key);
        Assert.Empty(ArchiveQuery.Filter(awards, null, null, null, null, search: "212446"));
        Assert.Empty(ArchiveQuery.Filter(awards, null, null, null, null, search: "cff"));
        Assert.Empty(ArchiveQuery.Filter(awards, null, null, null, null, search: "10356"));
    }

    [Fact]
    public void Filter_ByStatus_SelectsOnlyThatStatus()
    {
        var awards = new[]
        {
            Award("a", 100, "Alric", "gloves", "BIS"),
            Award("b", 200, "Sinja", "boots", "Upgrade", addonExported: true),
        };

        var result = ArchiveQuery.Filter(awards, null, null, null, status: AwardStatus.Open, search: null);

        Assert.Equal("a", Assert.Single(result).Key);
    }

    // Newest first, matching the addon's own history window.
    [Fact]
    public void Filter_ReturnsNewestFirst()
    {
        var awards = new[]
        {
            Award("old", 100, "Alric", "gloves", "BIS"),
            Award("new", 300, "Alric", "ring", "BIS"),
        };

        var result = ArchiveQuery.Filter(awards, null, null, null, null, null);

        Assert.Equal(new[] { "new", "old" }, result.Select(a => a.Key));
    }

    // The mark is Companion-side metadata, so it must round-trip through the archive file like
    // Withdrawn and FirstSeen do — a mark that vanishes on reload would silently re-open awards
    // that were already sent, which is the duplicate this whole effort exists to prevent.
    [Fact]
    public void ExportedByCompanionAt_SurvivesSaveAndLoad()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kart-query-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "loot-history.json");
        try
        {
            var doc = new ArchiveDocument();
            doc.Awards.Add(Award("a", 100, "Alric", "gloves", "BIS",
                companionExported: DateTimeOffset.FromUnixTimeSeconds(1786090821)));
            ArchiveStore.Save(doc, path);

            var back = ArchiveStore.Load(path);

            Assert.Equal(1786090821, Assert.Single(back.Awards).ExportedByCompanionAt!.Value.ToUnixTimeSeconds());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ArchiveMerger.ApplyFields writes only keys the incoming snapshot carries, and the mark is not
    // in Fields — so a later read of the game's file must not clear it. This pins the reason the
    // mark lives outside Fields in the first place.
    [Fact]
    public void Merge_OfANewerSnapshot_DoesNotClearTheCompanionMark()
    {
        var doc = new ArchiveDocument();
        doc.Awards.Add(Award("a", 100, "Alric", "gloves", "BIS",
            companionExported: DateTimeOffset.FromUnixTimeSeconds(1786090821)));
        doc.Awards[0].SourceFile = @"C:\wow\file.lua";

        var snapshot = new[]
        {
            new SavedVariables.LootHistoryEntry(new Dictionary<string, object?>
            {
                ["id"] = "a",
                ["time"] = 100d,
                ["winner"] = "Alric",
                ["item"] = "gloves",
                ["reason"] = "BIS",
                ["exported"] = true,
            }),
        };

        ArchiveMerger.Merge(doc, snapshot, @"C:\wow\file.lua", DateTimeOffset.UtcNow);

        Assert.NotNull(doc.Awards[0].ExportedByCompanionAt);
    }
}
