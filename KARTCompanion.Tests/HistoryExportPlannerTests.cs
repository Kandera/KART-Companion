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
        // Withdrawn outranks every export mark (see ArchiveQuery.StatusOf) -- a withdrawn award is
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
}
