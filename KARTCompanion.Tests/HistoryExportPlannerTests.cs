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

    // The maintainer's ruling, replacing the old "nothing selected falls back to everything shown":
    // that fallback made one press of the primary button stamp an entire archive exported, with no
    // unmark and no undo, and a second press to check the first one worked did it again.
    [Fact]
    public void AwardsToExport_NothingSelected_ExportsNothing()
    {
        Assert.Empty(HistoryExportPlanner.AwardsToExport(Array.Empty<ArchivedAward>()));
    }

    [Fact]
    public void AwardsToExport_UsesOnlyTheSelection_NotWhateverElseWasShown()
    {
        var shown = new[] { Award("a"), Award("b"), Award("c") };
        var selected = new[] { shown[1] };

        Assert.Equal(selected, HistoryExportPlanner.AwardsToExport(selected));
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

    // A zone the test states, not the machine's — and above all not the offset of the value under
    // test. Both date tests used to build their expected value from from!.Value.Offset, so the day
    // could have started in any zone at all and they still passed: turning Local into Utc left the
    // whole suite green, on a machine two hours off UTC.
    private static readonly TimeZoneInfo PlusNine =
        TimeZoneInfo.CreateCustomTimeZone("kart-test-plus-9", TimeSpan.FromHours(9), "UTC+09", "UTC+09");

    [Fact]
    public void ParseFromDate_ParsesTheTypedDateAtItsStartInTheGivenZone()
    {
        var from = HistoryExportPlanner.ParseFromDate("2026-08-08", PlusNine);

        Assert.NotNull(from);
        Assert.Equal(TimeSpan.FromHours(9), from!.Value.Offset);
        Assert.Equal(new DateTime(2026, 8, 8, 0, 0, 0), from.Value.DateTime);
        // Same claim as one instant: 2026-08-08 00:00 in UTC+09 is 2026-08-07 15:00 UTC.
        Assert.Equal(new DateTime(2026, 8, 7, 15, 0, 0, DateTimeKind.Utc), from.Value.UtcDateTime);
    }

    // The zone defaults to the machine's, because the Time column the user reads the date off is
    // rendered local. Stated against TimeZoneInfo.Local directly, never against the result.
    [Fact]
    public void ParseFromDate_NoZoneGiven_UsesTheMachinesLocalZone()
    {
        var from = HistoryExportPlanner.ParseFromDate("2026-08-08");

        var expected = new DateTimeOffset(
            new DateTime(2026, 8, 8, 0, 0, 0), TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 8, 8)));
        Assert.Equal(expected.Offset, from!.Value.Offset);
        Assert.Equal(expected, from);
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
        var to = HistoryExportPlanner.ParseToDate("2026-08-08", PlusNine);

        Assert.NotNull(to);
        Assert.Equal(TimeSpan.FromHours(9), to!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.FromHours(9)).AddTicks(-1), to);
        Assert.Equal(new DateTime(2026, 8, 8, 23, 59, 59), to.Value.DateTime.AddTicks(1).AddSeconds(-1));
    }

    [Fact]
    public void ParseToDate_BlankText_ReturnsNull()
    {
        Assert.Null(HistoryExportPlanner.ParseToDate(""));
    }

    // An unparsable date is treated as no filter at all, which on its own is silent: a mistyped date
    // narrows nothing and looks exactly like a date that happened to match everything.
    [Fact]
    public void DateFilterWarning_NamesWhicheverFieldDoesNotParse()
    {
        Assert.Equal("", HistoryExportPlanner.DateFilterWarning("", ""));
        Assert.Equal("", HistoryExportPlanner.DateFilterWarning("2026-08-08", "2026-08-09"));
        Assert.Equal("From is not a date — that filter is being ignored. Use a date like 2026-08-08.",
            HistoryExportPlanner.DateFilterWarning("yesterday", "2026-08-09"));
        Assert.Equal("To is not a date — that filter is being ignored. Use a date like 2026-08-08.",
            HistoryExportPlanner.DateFilterWarning("2026-08-08", "soon"));
        Assert.Equal("From and To are not dates — both filters are being ignored. Use a date like 2026-08-08.",
            HistoryExportPlanner.DateFilterWarning("yesterday", "soon"));
    }

    [Fact]
    public void IsUnparsableDate_BlankIsNotAMistake_GarbageIs()
    {
        Assert.False(HistoryExportPlanner.IsUnparsableDate(""));
        Assert.False(HistoryExportPlanner.IsUnparsableDate("   "));
        Assert.False(HistoryExportPlanner.IsUnparsableDate("2026-08-08"));
        Assert.True(HistoryExportPlanner.IsUnparsableDate("2026-13-40"));
        Assert.True(HistoryExportPlanner.IsUnparsableDate("last tuesday"));
    }

    // M5: four rows shown of which one is withdrawn used to read "3 shown" — the exportable count
    // wearing the shown count's label, describing neither correctly.
    [Fact]
    public void SelectionSummary_CountsRowsShownAsRowsShown_AndExportableSeparately()
    {
        var withdrawn = Award("w", withdrawn: true);
        var selected = new[] { Award("a"), Award("b"), Award("c"), withdrawn };

        Assert.Equal("4 of 4 shown award(s) selected — 3 will be exported, 1 withdrawn.",
            HistoryExportPlanner.SelectionSummary(shownCount: 4, selected));
    }

    [Fact]
    public void SelectionSummary_NoWithdrawnAwardsSelected_SaysOnlyWhatWasSelected()
    {
        Assert.Equal("2 of 9 shown award(s) selected.",
            HistoryExportPlanner.SelectionSummary(shownCount: 9, new[] { Award("a"), Award("b") }));
    }

    // Nothing selected now exports nothing, so the line says what to do instead of offering a count
    // of what one press would send.
    [Fact]
    public void SelectionSummary_NothingSelected_SaysWhatToDoAndOffersNoExportCount()
    {
        var text = HistoryExportPlanner.SelectionSummary(shownCount: 4, Array.Empty<ArchivedAward>());

        Assert.Equal("Nothing selected — select the awards to export. 4 shown; Ctrl+A selects all of them.", text);
    }

    [Fact]
    public void SelectionSummary_NothingShown_SaysSo()
    {
        Assert.Equal("No awards shown — nothing to select.",
            HistoryExportPlanner.SelectionSummary(shownCount: 0, Array.Empty<ArchivedAward>()));
    }

    // The headline rule Copy for WoWUtils depends on: it must reload before stamping, not write back
    // whatever stale document the window opened with, or a concurrent tray-menu read/merge that saved
    // a newer file while the window sat open gets silently overwritten.
    [Fact]
    public void ExportAndStamp_StampsOnlyTheGivenKeysInWhateverLoadReturns_NotTheCallersOwnSnapshot()
    {
        // Simulates the window's stale snapshot never being consulted: "c" exists only in the
        // freshly-loaded document, as if it arrived via a concurrent merge after the window opened.
        var loaded = new ArchiveDocument();
        loaded.Awards.Add(Award("a"));
        loaded.Awards.Add(Award("b"));
        loaded.Awards.Add(Award("c"));
        var stampedAt = DateTimeOffset.FromUnixTimeSeconds(1000);

        var result = HistoryExportPlanner.ExportAndStamp(
            new[] { "a", "c" }, stampedAt, load: () => loaded, deliver: _ => { }, save: _ => { }).Document;

        Assert.Equal(stampedAt, result.Awards.Single(a => a.Key == "a").ExportedByCompanionAt);
        Assert.Null(result.Awards.Single(a => a.Key == "b").ExportedByCompanionAt);
        Assert.Equal(stampedAt, result.Awards.Single(a => a.Key == "c").ExportedByCompanionAt);
    }

    [Fact]
    public void ExportAndStamp_PassesTheStampedDocumentToSave()
    {
        var loaded = new ArchiveDocument();
        loaded.Awards.Add(Award("a"));
        ArchiveDocument? saved = null;

        HistoryExportPlanner.ExportAndStamp(
            new[] { "a" }, DateTimeOffset.UnixEpoch, load: () => loaded, deliver: _ => { }, save: doc => saved = doc);

        Assert.Same(loaded, saved);
        Assert.NotNull(Assert.Single(saved!.Awards).ExportedByCompanionAt);
    }

    // The property that replaces manual snapshot/rollback: if the save fails, ExportAndStamp never
    // returns an outcome for the caller to adopt, so the window's own display snapshot — which this
    // method never even sees — is left exactly as it was. There is nothing to restore because nothing
    // durable, and nothing the caller already held, was ever touched.
    [Fact]
    public void ExportAndStamp_SaveThrows_ExceptionPropagatesAndNothingIsReturnedToTheCaller()
    {
        var loaded = new ArchiveDocument();
        loaded.Awards.Add(Award("a"));

        HistoryExportPlanner.ExportOutcome? result = null;
        var ex = Record.Exception(() =>
            result = HistoryExportPlanner.ExportAndStamp(
                new[] { "a" }, DateTimeOffset.UnixEpoch,
                load: () => loaded,
                deliver: _ => { },
                save: _ => throw new InvalidOperationException("disk full")));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Equal("disk full", ex!.Message);
        Assert.Null(result);
    }

    // Important 1, the sequence that got a withdrawn award onto the clipboard: the window is open,
    // the tray's "Read loot history now" merges a snapshot the addon has deleted the row from, the
    // merge marks the award Withdrawn — and the window's own list still says it is open. Deciding
    // from the caller's snapshot exported it anyway. The decision now happens in the same document
    // the stamp is written to, so a mark that landed after the window opened is seen.
    [Fact]
    public void ExportAndStamp_AwardWithdrawnSinceTheWindowLoaded_IsNeitherDeliveredNorStamped()
    {
        var fresh = new ArchiveDocument();
        fresh.Awards.Add(Award("withdrawn-since", withdrawn: true));
        fresh.Awards.Add(Award("still-open"));
        IReadOnlyList<ArchivedAward>? delivered = null;

        // Both keys selected — the window's stale list showed both as open.
        var outcome = HistoryExportPlanner.ExportAndStamp(
            new[] { "withdrawn-since", "still-open" }, DateTimeOffset.FromUnixTimeSeconds(1000),
            load: () => fresh, deliver: awards => delivered = awards, save: _ => { });

        Assert.Equal(new[] { "still-open" }, delivered!.Select(a => a.Key));
        Assert.Equal(new[] { "still-open" }, outcome.Exported.Select(a => a.Key));
        Assert.Null(fresh.Awards.Single(a => a.Key == "withdrawn-since").ExportedByCompanionAt);
    }

    // Only what actually went is marked — an award left out for being withdrawn must not come back
    // later looking like it was already sent.
    [Fact]
    public void ExportAndStamp_StampsOnlyTheAwardsThatWent()
    {
        var fresh = new ArchiveDocument();
        fresh.Awards.Add(Award("gone", withdrawn: true));
        fresh.Awards.Add(Award("sent"));

        HistoryExportPlanner.ExportAndStamp(
            new[] { "gone", "sent" }, DateTimeOffset.FromUnixTimeSeconds(77),
            load: () => fresh, deliver: _ => { }, save: _ => { });

        Assert.Null(fresh.Awards.Single(a => a.Key == "gone").ExportedByCompanionAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(77),
            fresh.Awards.Single(a => a.Key == "sent").ExportedByCompanionAt);
    }

    // Delivery is the clipboard. If it fails the awards did not go anywhere, so marking them
    // exported would be a lie that silently suppresses them from the next real export.
    [Fact]
    public void ExportAndStamp_DeliveryThrows_NothingIsStampedAndNothingIsSaved()
    {
        var fresh = new ArchiveDocument();
        fresh.Awards.Add(Award("a"));
        var saved = false;

        var ex = Record.Exception(() => HistoryExportPlanner.ExportAndStamp(
            new[] { "a" }, DateTimeOffset.UnixEpoch,
            load: () => fresh,
            deliver: _ => throw new InvalidOperationException("clipboard busy"),
            save: _ => saved = true));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Null(Assert.Single(fresh.Awards).ExportedByCompanionAt);
        Assert.False(saved);
    }

    // Nothing selected reaches here only if the disabled button is bypassed, but the rule holds at
    // this level too: no delivery, no stamp, no save — in particular no save of an untouched
    // document over a file another process may have just written.
    [Fact]
    public void ExportAndStamp_NoKeys_DeliversNothingAndSavesNothing()
    {
        var fresh = new ArchiveDocument();
        fresh.Awards.Add(Award("a"));
        var delivered = false;
        var saved = false;

        var outcome = HistoryExportPlanner.ExportAndStamp(
            Array.Empty<string>(), DateTimeOffset.UnixEpoch,
            load: () => fresh, deliver: _ => delivered = true, save: _ => saved = true);

        Assert.Empty(outcome.Exported);
        Assert.False(delivered);
        Assert.False(saved);
        Assert.Null(Assert.Single(fresh.Awards).ExportedByCompanionAt);
    }

    // Save a copy… uses this on its own (it stamps nothing), so the withdrawn rule has to live here
    // rather than in the stamping path.
    [Fact]
    public void ExportableFrom_TakesTheSelectedKeysMinusWhateverIsWithdrawnInThatDocument()
    {
        var doc = new ArchiveDocument();
        doc.Awards.Add(Award("a"));
        doc.Awards.Add(Award("b", withdrawn: true));
        doc.Awards.Add(Award("c"));

        Assert.Equal(new[] { "a", "c" },
            HistoryExportPlanner.ExportableFrom(doc, new[] { "a", "b", "c" }).Select(a => a.Key));
        Assert.Equal(new[] { "c" }, HistoryExportPlanner.ExportableFrom(doc, new[] { "c" }).Select(a => a.Key));
        Assert.Empty(HistoryExportPlanner.ExportableFrom(doc, Array.Empty<string>()));
    }
}
