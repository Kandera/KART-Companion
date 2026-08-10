using KARTCompanion.Archive;
using KARTCompanion.SavedVariables;
using KARTCompanion.Shell;

namespace KARTCompanion.Tests;

public class HistoryExportPlannerTests
{
    private static ArchivedAward Award(
        string id, bool withdrawn = false, bool addonExported = false, DateTimeOffset? companionExported = null,
        bool excluded = false)
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
            ExcludedFromExport = excluded,
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
    public void AwardsToExport_DropsExcludedAwards()
    {
        var open = Award("open");
        var excluded = Award("excluded", excluded: true);

        var result = HistoryExportPlanner.AwardsToExport(new[] { open, excluded });

        Assert.Equal(new[] { "open" }, result.Select(a => a.Key));
    }

    [Fact]
    public void ExportableFrom_DropsExcludedAwardsEvenWhenExplicitlySelected()
    {
        var doc = new ArchiveDocument();
        doc.Awards.Add(Award("open"));
        doc.Awards.Add(Award("excluded", excluded: true));

        var result = HistoryExportPlanner.ExportableFrom(doc, new[] { "open", "excluded" });

        Assert.Equal(new[] { "open" }, result.Select(a => a.Key));
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
        // Asked of the marks themselves, not of StatusOf. StatusOf answers "what is this award now",
        // and Withdrawn and Excluded both outrank the export marks there — so routing this through it
        // meant a withdrawn or excluded award never lit the notice, even though the addon still has no
        // idea the Companion sent it and its own export button will send it again. Over-warning for an
        // award that turns out to stay withdrawn is the safe direction; going quiet is not.
        Assert.True(HistoryExportPlanner.ContainsCompanionOnlyExport(new[] { withdrawnCompanion }));

        var excludedCompanion = Award("excluded", excluded: true, companionExported: DateTimeOffset.UnixEpoch);
        Assert.True(HistoryExportPlanner.ContainsCompanionOnlyExport(new[] { excludedCompanion }));
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

    [Fact]
    public void SelectionSummary_NamesWithdrawnAndExcludedSeparately()
    {
        var selected = new[] { Award("a"), Award("b", withdrawn: true), Award("c", excluded: true) };

        var summary = HistoryExportPlanner.SelectionSummary(10, selected);

        Assert.Contains("1 will be exported", summary);
        Assert.Contains("1 withdrawn", summary);
        Assert.Contains("1 excluded", summary);
    }

    // An award can be both. Counting it in both buckets makes the numbers not add up against the
    // selection count, which reads as a bug in the window rather than as two overlapping labels.
    // Withdrawn wins, because that is what StatusOf calls it and what the Status column shows.
    [Fact]
    public void SelectionSummary_AnAwardBothWithdrawnAndExcludedIsCountedOnce()
    {
        var selected = new[] { Award("a"), Award("b", withdrawn: true, excluded: true) };

        var summary = HistoryExportPlanner.SelectionSummary(10, selected);

        Assert.Contains("1 will be exported", summary);
        Assert.Contains("1 withdrawn", summary);
        Assert.DoesNotContain("excluded", summary);
    }

    private static LootHistoryEntry Entry(string? instance = null, long? difficultyId = null, string? difficulty = null, long? rollId = null)
    {
        var fields = new Dictionary<string, object?>();
        if (instance != null) fields["instance"] = instance;
        if (difficultyId != null) fields["difficultyID"] = (double)difficultyId.Value;
        if (difficulty != null) fields["difficulty"] = difficulty;
        if (rollId != null) fields["rollID"] = (double)rollId.Value;
        return new LootHistoryEntry(fields);
    }

    [Fact]
    public void RaidDisplay_BothPresent_JoinsInstanceAndDifficultyWithAnEmDash()
    {
        var entry = Entry(instance: "March on Quel'Danas", difficultyId: 16);

        Assert.Equal("March on Quel'Danas — Mythic", HistoryExportPlanner.RaidDisplay(entry));
    }

    // Today's behaviour, and what every award archived before the addon started logging instance
    // will keep showing forever — instance is absent on those, not present-and-empty.
    [Fact]
    public void RaidDisplay_InstanceAbsent_ShowsOnlyTheDifficulty()
    {
        var entry = Entry(instance: null, difficultyId: 16);

        Assert.Equal("Mythic", HistoryExportPlanner.RaidDisplay(entry));
    }

    [Fact]
    public void RaidDisplay_DifficultyUnknownButInstancePresent_ShowsOnlyTheInstance()
    {
        var entry = Entry(instance: "March on Quel'Danas");

        Assert.Equal("March on Quel'Danas", HistoryExportPlanner.RaidDisplay(entry));
    }

    [Fact]
    public void RaidDisplay_NeitherPresent_ReturnsEmptyString()
    {
        Assert.Equal("", HistoryExportPlanner.RaidDisplay(Entry()));
    }

    // The lootmaster typed /kart add outside the raid, so the addon had no instance to read. The
    // difficulty it did know still has to carry, same as any other award.
    [Fact]
    public void RaidDisplay_ManualRollIdWithoutInstance_SaysManualAddAndKeepsTheDifficulty()
    {
        var entry = Entry(instance: null, difficultyId: 16, rollId: 528998);

        Assert.Equal("manual add — Mythic", HistoryExportPlanner.RaidDisplay(entry));
    }

    [Fact]
    public void RaidDisplay_ManualRollIdWithNoDifficultyEither_SaysManualAddAlone()
    {
        var entry = Entry(instance: null, rollId: 528998);

        Assert.Equal("manual add", HistoryExportPlanner.RaidDisplay(entry));
    }

    // Literals on both sides of the boundary, deliberately NOT written as AddonManualRollIdBase - 1:
    // a bound expressed in terms of the constant moves with it and can never catch the constant
    // being wrong. 500000 is the addon's MANUAL_ROLL_ID_BASE, copied here from LootCouncil.lua.
    //
    // The base itself is a manual roll id — the addon seeds at base + time() % 100000, and that
    // remainder is zero once every 100000 seconds. An exclusive comparison would drop that award.
    [Fact]
    public void RaidDisplay_RollIdExactlyAtTheManualBase_CountsAsAManualAdd()
    {
        Assert.Equal("manual add", HistoryExportPlanner.RaidDisplay(Entry(rollId: 500000)));
    }

    [Fact]
    public void RaidDisplay_RollIdJustBelowTheManualBase_IsNotAManualAdd()
    {
        var entry = Entry(instance: null, difficultyId: 16, rollId: 499999);

        Assert.Equal("Mythic", HistoryExportPlanner.RaidDisplay(entry));
    }

    // The copy itself, against the addon's LootCouncil.lua. The two boundary cases above pin the
    // behaviour at 500000 but would both still pass if the constant and they moved together; this
    // is the one assertion that fails when only the constant moves.
    [Fact]
    public void AddonManualRollIdBase_MatchesTheAddonsConstant()
    {
        Assert.Equal(500000, HistoryExportPlanner.AddonManualRollIdBase);
    }

    // The harmful flank, held down explicitly: Blizzard's roll ids are a per-session counter — 1..29
    // across the maintainer's whole real file — and an award of theirs with no instance is a genuine
    // gap. It must keep looking like one instead of being excused as an ordinary manual add.
    [Fact]
    public void RaidDisplay_BlizzardRollIdWithoutInstance_StillRendersAsAGap()
    {
        Assert.Equal("Mythic", HistoryExportPlanner.RaidDisplay(Entry(difficultyId: 16, rollId: 29)));
    }

    // Separate Fact rather than a second Assert in the one above: a failing assertion ends its test,
    // so a case sharing a test with a case that breaks first is never actually observed failing.
    [Fact]
    public void RaidDisplay_BlizzardRollIdAndNoDifficulty_StillRendersAsAnEmptyCell()
    {
        Assert.Equal("", HistoryExportPlanner.RaidDisplay(Entry(rollId: 29)));
    }

    // No roll id at all — the field is absent, not zero. Lifted comparison must not treat that as
    // manual, and must not throw either.
    [Fact]
    public void RaidDisplay_NoRollIdAtAll_StillRendersAsAGap()
    {
        Assert.Equal("", HistoryExportPlanner.RaidDisplay(Entry(rollId: null)));
    }

    // A manual add made while standing in the raid does have an instance, and the real raid name is
    // the more useful thing to show — the label never displaces one.
    [Fact]
    public void RaidDisplay_ManualRollIdWithAnInstance_ShowsTheInstanceNotTheLabel()
    {
        var entry = Entry(instance: "March on Quel'Danas", difficultyId: 16, rollId: 528998);

        Assert.Equal("March on Quel'Danas — Mythic", HistoryExportPlanner.RaidDisplay(entry));
    }

    [Fact]
    public void FieldForColumn_MapsOnlyThePlayerAndReasonColumns()
    {
        Assert.Null(HistoryExportPlanner.FieldForColumn(0));   // Time
        Assert.Equal("winner", HistoryExportPlanner.FieldForColumn(1));
        Assert.Null(HistoryExportPlanner.FieldForColumn(2));   // Item
        Assert.Equal("reason", HistoryExportPlanner.FieldForColumn(3));
        Assert.Null(HistoryExportPlanner.FieldForColumn(4));   // Raid
        Assert.Null(HistoryExportPlanner.FieldForColumn(5));   // Status
        Assert.Null(HistoryExportPlanner.FieldForColumn(99));
    }

    [Fact]
    public void EmphasisFor_MarksTheEditedCellOnly()
    {
        var award = Award("a");
        AwardEditor.Set(award, "winner", "Thornfell");

        Assert.Equal(HistoryExportPlanner.RowEmphasis.Edited, HistoryExportPlanner.EmphasisFor(award, 1));
        Assert.Equal(HistoryExportPlanner.RowEmphasis.Normal, HistoryExportPlanner.EmphasisFor(award, 3));
        Assert.Equal(HistoryExportPlanner.RowEmphasis.Normal, HistoryExportPlanner.EmphasisFor(award, 0));
    }

    [Fact]
    public void EmphasisFor_AwardsThatCannotBeExportedAreHeldBackWholeRow()
    {
        var withdrawn = Award("w", withdrawn: true);
        var excluded = Award("e", excluded: true);

        Assert.Equal(HistoryExportPlanner.RowEmphasis.Held, HistoryExportPlanner.EmphasisFor(withdrawn, 1));
        Assert.Equal(HistoryExportPlanner.RowEmphasis.Held, HistoryExportPlanner.EmphasisFor(excluded, 3));
    }

    // The combination a defect actually produces: an award corrected AND held back. Both facts must
    // show — the corrected column stays Edited even though the row is Held everywhere else. This is
    // what pins the precedence in EmphasisFor: a naive reorder that decides Edited-vs-Held from
    // AwardEditor.IsEdited (whole award) rather than AwardEditor.IsFieldEdited (this one column) gets
    // the corrected column right by accident but wrongly promotes or fails to dim the OTHER column,
    // which is what the second assertion in each pair below catches.
    [Fact]
    public void EmphasisFor_AnEditedCellKeepsItsEmphasisEvenInsideAHeldRow()
    {
        var excludedAndEdited = Award("e", excluded: true);
        AwardEditor.Set(excludedAndEdited, "winner", "Thornfell");

        Assert.Equal(HistoryExportPlanner.RowEmphasis.Edited, HistoryExportPlanner.EmphasisFor(excludedAndEdited, 1));
        Assert.Equal(HistoryExportPlanner.RowEmphasis.Held, HistoryExportPlanner.EmphasisFor(excludedAndEdited, 3));

        var withdrawnAndEdited = Award("w", withdrawn: true);
        AwardEditor.Set(withdrawnAndEdited, "reason", "Offspec");

        Assert.Equal(HistoryExportPlanner.RowEmphasis.Edited, HistoryExportPlanner.EmphasisFor(withdrawnAndEdited, 3));
        Assert.Equal(HistoryExportPlanner.RowEmphasis.Held, HistoryExportPlanner.EmphasisFor(withdrawnAndEdited, 1));
    }

    [Fact]
    public void EditSummary_NamesWhatWasCorrectedAndWhetherItIsExcluded()
    {
        var untouched = Award("a");
        Assert.Contains("No corrections", HistoryExportPlanner.EditSummary(untouched));

        var corrected = Award("b");
        AwardEditor.Set(corrected, "winner", "Thornfell");
        AwardEditor.Set(corrected, "reason", "Offspec");
        var summary = HistoryExportPlanner.EditSummary(corrected);
        Assert.Contains("player", summary);
        Assert.Contains("reason", summary);
        Assert.DoesNotContain("excluded", summary);

        var held = Award("c", excluded: true);
        Assert.Contains("excluded", HistoryExportPlanner.EditSummary(held));
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

    // Final review: an award withdrawn in the window's display snapshot but un-withdrawn in the
    // archive by the time Copy re-reads it (ArchiveMerger.cs:85 — a withdrawal is reversible) makes
    // MORE go out than the footer promised, not fewer. leftOut alone can never go negative to catch
    // this, so the note needs the other direction too.
    [Fact]
    public void ExportDiscrepancyNote_MoreWentThanTheListShowed_ReportsTheExtra()
    {
        var note = HistoryExportPlanner.ExportDiscrepancyNote(
            selectedCount: 2, shownExportableCount: 1, actuallyExportedCount: 2);

        Assert.Equal(
            " 1 more award(s) were exported than the list showed — a withdrawal was reversed since this list was drawn.",
            note);
    }

    // The pre-existing direction (Task 5 / this wave): an award withdrawn between drawing and
    // pressing Copy is left out, and that must still be reported the same way as before.
    [Fact]
    public void ExportDiscrepancyNote_FewerWentThanWereSelected_ReportsTheLeftOut()
    {
        var note = HistoryExportPlanner.ExportDiscrepancyNote(
            selectedCount: 3, shownExportableCount: 3, actuallyExportedCount: 2);

        Assert.Equal(" 1 withdrawn or excluded award(s) were left out.", note);
    }

    [Fact]
    public void ExportDiscrepancyNote_ExportedMatchesWhatWasShown_IsBlank()
    {
        var note = HistoryExportPlanner.ExportDiscrepancyNote(
            selectedCount: 2, shownExportableCount: 2, actuallyExportedCount: 2);

        Assert.Equal("", note);
    }

    // The end-to-end shape of the defect: the window's display snapshot and the freshly-reloaded
    // archive are TWO SEPARATE ArchivedAward instances that happen to share a Key — exactly what a
    // real screen sees, since ArchiveStore.Load parses the file anew each call. A test built from one
    // shared instance could not reproduce this: mutating the "archive" copy would mutate the
    // "display" copy too, and the staleness this is about would not exist.
    [Fact]
    public void ExportDiscrepancyNote_EndToEnd_AwardUnWithdrawnSinceTheListWasDrawn()
    {
        // What the window drew: "x" shows withdrawn (and carries only the Companion's mark from an
        // earlier export), "y" shows open. Both selected.
        var displaySelected = new[]
        {
            Award("x", withdrawn: true, companionExported: DateTimeOffset.UnixEpoch),
            Award("y"),
        };
        var shownExportableCount = HistoryExportPlanner.AwardsToExport(displaySelected).Count;
        Assert.Equal(1, shownExportableCount); // the footer would have said "1 will be exported"

        // The archive as ExportAndStamp actually reloads it: a separate object graph, same keys, "x"
        // reappeared in a later snapshot and ArchiveMerger un-withdrew it.
        var archive = new ArchiveDocument();
        archive.Awards.Add(Award("x", withdrawn: false, companionExported: DateTimeOffset.UnixEpoch));
        archive.Awards.Add(Award("y"));

        var outcome = HistoryExportPlanner.ExportAndStamp(
            displaySelected.Select(a => a.Key).ToList(), DateTimeOffset.UnixEpoch,
            load: () => archive, deliver: _ => { }, save: _ => { });

        Assert.Equal(2, outcome.Exported.Count); // both genuinely go — "x" is not withdrawn in truth

        var note = HistoryExportPlanner.ExportDiscrepancyNote(
            displaySelected.Length, shownExportableCount, outcome.Exported.Count);
        Assert.Equal(
            " 1 more award(s) were exported than the list showed — a withdrawal was reversed since this list was drawn.",
            note);
    }

    // The screen's edit round-trip end to end, with the Form left out: the dialog's three values go to
    // ApplyAndSave, and the summary line describes what actually ended up in the archive — not what was
    // typed. Typing the addon's own value back in is not a correction, and the line must not claim it was.
    [Fact]
    public void EditRoundTrip_SummaryDescribesTheSavedState()
    {
        var doc = new ArchiveDocument();
        doc.Awards.Add(new ArchivedAward
        {
            Fields = new Dictionary<string, object?> { ["id"] = "a1", ["winner"] = "Bramblewick", ["reason"] = "BIS" },
        });

        var outcome = AwardEditor.ApplyAndSave(
            "a1",
            new Dictionary<string, string> { ["winner"] = "Thornfell", ["reason"] = "BIS" },
            excludedFromExport: true,
            () => doc,
            _ => { });

        var summary = HistoryExportPlanner.EditSummary(outcome.Award);
        Assert.Contains("player", summary);
        Assert.DoesNotContain("reason", summary);
        Assert.Contains("excluded from every export", summary);
    }
}
