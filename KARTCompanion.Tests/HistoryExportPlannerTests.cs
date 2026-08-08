using KARTCompanion.Archive;
using KARTCompanion.Shell;

namespace KARTCompanion.Tests;

public class HistoryExportPlannerTests
{
    private static ArchivedAward Award(
        string id, bool withdrawn = false, bool addonExported = false, DateTimeOffset? companionExported = null)
    {
        var fields = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["time"] = 1d,
            ["winner"] = "Alric",
            ["item"] = "gloves",
            ["reason"] = "BIS",
        };
        if (addonExported) fields["exported"] = true;

        return new ArchivedAward
        {
            Fields = fields,
            Withdrawn = withdrawn,
            ExportedByCompanionAt = companionExported,
        };
    }

    [Fact]
    public void EffectiveSelection_NothingSelected_FallsBackToEverythingFiltered()
    {
        var filtered = new[] { Award("a"), Award("b") };

        var effective = HistoryExportPlanner.EffectiveSelection(filtered, selected: Array.Empty<ArchivedAward>());

        Assert.Equal(filtered, effective);
    }

    [Fact]
    public void EffectiveSelection_SomethingSelected_UsesOnlyTheSelection()
    {
        var filtered = new[] { Award("a"), Award("b"), Award("c") };
        var selected = new[] { filtered[1] };

        var effective = HistoryExportPlanner.EffectiveSelection(filtered, selected);

        Assert.Equal(selected, effective);
    }

    // The one rule that must hold no matter what was clicked or selected: a withdrawn award never
    // goes out, because the addon has already deleted its own copy of it.
    [Fact]
    public void AwardsToExport_RemovesWithdrawnAwardsEvenWhenExplicitlySelected()
    {
        var withdrawn = Award("a", withdrawn: true);
        var open = Award("b");

        var toExport = HistoryExportPlanner.AwardsToExport(new[] { withdrawn, open });

        Assert.Equal(new[] { open }, toExport);
    }

    [Fact]
    public void AwardsToExport_NoWithdrawnAwards_KeepsEverything()
    {
        var awards = new[] { Award("a"), Award("b") };

        var toExport = HistoryExportPlanner.AwardsToExport(awards);

        Assert.Equal(awards, toExport);
    }

    [Fact]
    public void ContainsCompanionOnlyExport_TrueOnlyForTheCompanionOnlyStatus()
    {
        var open = Award("open");
        var addonOnly = Award("addon", addonExported: true);
        var companionOnly = Award("companion", companionExported: DateTimeOffset.UnixEpoch);
        var both = Award("both", addonExported: true, companionExported: DateTimeOffset.UnixEpoch);
        var withdrawnCompanion = Award("withdrawn", withdrawn: true, companionExported: DateTimeOffset.UnixEpoch);

        Assert.False(HistoryExportPlanner.ContainsCompanionOnlyExport(new[] { open }));
        Assert.False(HistoryExportPlanner.ContainsCompanionOnlyExport(new[] { addonOnly }));
        Assert.True(HistoryExportPlanner.ContainsCompanionOnlyExport(new[] { companionOnly }));
        Assert.False(HistoryExportPlanner.ContainsCompanionOnlyExport(new[] { both }));
        // Withdrawn outranks every export mark (see ArchiveQuery.StatusOf) — a withdrawn award is
        // never reported as "Companion only", even if the mark is still sitting in its Fields.
        Assert.False(HistoryExportPlanner.ContainsCompanionOnlyExport(new[] { withdrawnCompanion }));
    }

    [Fact]
    public void ContainsCompanionOnlyExport_TrueIfAnySingleAwardQualifies()
    {
        var open = Award("open");
        var companionOnly = Award("companion", companionExported: DateTimeOffset.UnixEpoch);

        Assert.True(HistoryExportPlanner.ContainsCompanionOnlyExport(new[] { open, companionOnly }));
    }

    // Index 0 is the "All statuses" sentinel the status combo box lists before any AwardStatus value,
    // so the real values start at index 1 — an off-by-one here would silently show the wrong status
    // class with nothing visibly wrong to notice.
    [Fact]
    public void StatusForComboIndex_ZeroIsAllStatuses()
    {
        Assert.Null(HistoryExportPlanner.StatusForComboIndex(0));
    }

    [Fact]
    public void StatusForComboIndex_MapsEachIndexToTheStatusOneOffsetEarlierInStatusOptions()
    {
        for (var i = 0; i < HistoryExportPlanner.StatusOptions.Length; i++)
        {
            Assert.Equal(HistoryExportPlanner.StatusOptions[i], HistoryExportPlanner.StatusForComboIndex(i + 1));
        }
    }

    [Fact]
    public void ParseFromDate_ParsesTheTypedDateAtItsStart()
    {
        var from = HistoryExportPlanner.ParseFromDate("2026-08-08");

        Assert.Equal(new DateTimeOffset(2026, 8, 8, 0, 0, 0, from!.Value.Offset), from);
    }

    [Fact]
    public void ParseFromDate_BlankOrUnparsableText_ReturnsNull()
    {
        Assert.Null(HistoryExportPlanner.ParseFromDate(""));
        Assert.Null(HistoryExportPlanner.ParseFromDate("not a date"));
    }

    // "To 2026-08-08" means "including everything that happened ON the 8th" — the end of that day,
    // not the instant at its midnight start (which would exclude the whole day a human just typed).
    [Fact]
    public void ParseToDate_ReturnsTheEndOfTheTypedDayNotItsStart()
    {
        var to = HistoryExportPlanner.ParseToDate("2026-08-08");

        var expected = new DateTimeOffset(2026, 8, 9, 0, 0, 0, to!.Value.Offset).AddTicks(-1);
        Assert.Equal(expected, to);
    }

    [Fact]
    public void ParseToDate_BlankText_ReturnsNull()
    {
        Assert.Null(HistoryExportPlanner.ParseToDate(""));
    }

    // The headline rule Copy for WoWUtils depends on: it must reload before stamping, not write back
    // whatever stale document the window opened with, or a concurrent tray-menu read/merge that saved
    // a newer file while the window sat open gets silently overwritten.
    [Fact]
    public void StampAndSave_StampsOnlyTheGivenKeysInWhateverLoadReturns_NotTheCallersOwnSnapshot()
    {
        // Simulates the window's stale snapshot never being consulted: "c" exists only in the
        // freshly-loaded document, as if it arrived via a concurrent merge after the window opened.
        var loaded = new ArchiveDocument();
        loaded.Awards.Add(Award("a"));
        loaded.Awards.Add(Award("b"));
        loaded.Awards.Add(Award("c"));
        var stampedAt = DateTimeOffset.FromUnixTimeSeconds(1000);

        var result = HistoryExportPlanner.StampAndSave(
            new[] { "a", "c" }, stampedAt, load: () => loaded, save: _ => { });

        Assert.Equal(stampedAt, result.Awards.Single(a => a.Key == "a").ExportedByCompanionAt);
        Assert.Null(result.Awards.Single(a => a.Key == "b").ExportedByCompanionAt);
        Assert.Equal(stampedAt, result.Awards.Single(a => a.Key == "c").ExportedByCompanionAt);
    }

    [Fact]
    public void StampAndSave_PassesTheStampedDocumentToSave()
    {
        var loaded = new ArchiveDocument();
        loaded.Awards.Add(Award("a"));
        ArchiveDocument? saved = null;

        HistoryExportPlanner.StampAndSave(
            new[] { "a" }, DateTimeOffset.UnixEpoch, load: () => loaded, save: doc => saved = doc);

        Assert.Same(loaded, saved);
        Assert.NotNull(Assert.Single(saved!.Awards).ExportedByCompanionAt);
    }

    // The property that replaces manual snapshot/rollback: if the save fails, StampAndSave never
    // returns a document for the caller to adopt, so HistoryScreen's own _doc — which this method
    // never even sees — is left exactly as it was. There is nothing to restore because nothing
    // durable, and nothing the caller already held, was ever touched.
    [Fact]
    public void StampAndSave_SaveThrows_ExceptionPropagatesAndNothingIsReturnedToTheCaller()
    {
        var loaded = new ArchiveDocument();
        loaded.Awards.Add(Award("a"));

        ArchiveDocument? result = null;
        var ex = Record.Exception(() =>
            result = HistoryExportPlanner.StampAndSave(
                new[] { "a" }, DateTimeOffset.UnixEpoch,
                load: () => loaded,
                save: _ => throw new InvalidOperationException("disk full")));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Equal("disk full", ex!.Message);
        Assert.Null(result);
    }
}
