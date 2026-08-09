using KARTCompanion.Shell;

namespace KARTCompanion.Tests;

/// <summary>
/// How the history list's six columns divide up the width they are given. The rule lives in a pure
/// function so every case of it can be enumerated cheaply here; that the screen hands its real
/// ListView the answer, at that list's own scale and client width, is pinned in
/// HistoryScreenFormTests.
/// </summary>
public class HistoryListLayoutTests
{
    // The widths at 100% display scaling, as HistoryScreen applies them.
    private static readonly int[] Logical = HistoryListLayout.LogicalColumnWidths.ToArray();

    // The five columns that keep their width. Time 115 + Player 105 + Reason 110 + Raid 205 +
    // Status 165 — everything except Item.
    [Fact]
    public void FixedColumnsWidth_IsEveryColumnExceptItem()
    {
        Assert.Equal(700, HistoryListLayout.FixedColumnsWidth(Logical));
    }

    // The measured widths still add up to the content width the screens lay themselves out at, which
    // is what makes the window's starting size the right one for this list.
    [Fact]
    public void LogicalColumnWidths_SumToTheContentWidth()
    {
        Assert.Equal(950, Logical.Sum());
    }

    // HistoryExportPlanner.FieldForColumn answers by index, so the order these are added in is load
    // bearing: swap two columns and the wrong cell is drawn as corrected.
    [Fact]
    public void Columns_AreInTheOrderFieldForColumnAnswersFor()
    {
        Assert.Equal(
            new[] { "Time", "Player", "Item", "Reason", "Raid", "Status" },
            HistoryListLayout.Columns.Select(c => c.Header));
        Assert.Equal("winner", HistoryExportPlanner.FieldForColumn(1));
        Assert.Equal("reason", HistoryExportPlanner.FieldForColumn(3));
        Assert.Equal("Item", HistoryListLayout.Columns[HistoryListLayout.ItemColumn].Header);
    }

    // A list wider than the columns need hands the surplus to Item and to nobody else: every other
    // column is already sized to the worst value it can hold, and padding those would only move the
    // text further from the column beside it.
    [Fact]
    public void Allocate_ItemTakesWhateverTheListHasLeftOver()
    {
        var widths = HistoryListLayout.Allocate(Logical, minimumItemWidth: 160, listWidth: 1000);

        Assert.Equal(new[] { 115, 105, 300, 110, 205, 165 }, widths);
    }

    // The starting width: a 950-wide list, less the list's own 2px border, is 948 for the columns —
    // so Item gets 248 rather than the 250 it was hard-coded to, and the two-pixel overflow that
    // used to put a horizontal scrollbar under a full list is gone.
    [Fact]
    public void Allocate_AtTheListsOwnClientWidth_ItemFitsExactly()
    {
        var widths = HistoryListLayout.Allocate(Logical, minimumItemWidth: 160, listWidth: 948);

        Assert.Equal(948, widths.Sum());
        Assert.Equal(248, widths[HistoryListLayout.ItemColumn]);
    }

    // Narrower than the columns need, Item stops shrinking and the list scrolls sideways instead.
    // A column squeezed to nothing is not something the user can do anything about; a scrollbar is.
    [Fact]
    public void Allocate_ListTooNarrow_ClampsItemAtItsMinimum()
    {
        var widths = HistoryListLayout.Allocate(Logical, minimumItemWidth: 160, listWidth: 700);

        Assert.Equal(160, widths[HistoryListLayout.ItemColumn]);
        Assert.True(widths.Sum() > 700, "the columns are wider than the list, which is what makes it scroll");
    }

    // One pixel below the minimum list width the clamp is already in force, and at it exactly Item
    // sits on its minimum — this is the number HistoryScreen.MinimumViewSize is derived from.
    [Fact]
    public void MinimumListWidth_IsTheFixedColumnsPlusAWholeItemColumn()
    {
        Assert.Equal(860, HistoryListLayout.MinimumListWidth(Logical));
        Assert.Equal(
            HistoryListLayout.MinimumItemWidth,
            HistoryListLayout.Allocate(Logical, HistoryListLayout.MinimumItemWidth, HistoryListLayout.MinimumListWidth(Logical))
                [HistoryListLayout.ItemColumn]);
    }

    // --- the display scaling itself ---
    //
    // The tests below hand Allocate widths that are ALREADY scaled, which pins what it does with them
    // and nothing about where they came from: the scaling could be dropped from the caller entirely
    // and this suite stayed green. AllocateForScale is that caller, minus the control — HistoryScreen
    // now only supplies its list's own DeviceDpi.

    // At 100% the columns are the widths as measured, and the starting list width still fits them
    // exactly. Written as 96 over LogicalDpi rather than as 1.0, because that is the arithmetic
    // HistoryScreen does with its list's own DeviceDpi — a 96-dpi display is what these widths were
    // measured on, so it must come back out unscaled.
    [Fact]
    public void AllocateForScale_At100Percent_IsTheLogicalWidths()
    {
        Assert.Equal(
            new[] { 115, 105, 248, 110, 205, 165 },
            HistoryListLayout.AllocateForScale(96 / HistoryListLayout.LogicalDpi, listWidth: 948));
    }

    // At 125% every fixed column is 1.25x the width it measures at 100% — 115 -> 144, 105 -> 131,
    // 110 -> 138, 205 -> 256, 165 -> 206. This is the whole reason the columns were touched: a
    // ListView's column widths are the one part of the layout WinForms' own scaling never reaches, so
    // at 125% the font grows into a column that has not, and "exported (companion)" ellipsizes.
    [Fact]
    public void AllocateForScale_At125Percent_ScalesEveryFixedColumnWithTheDisplay()
    {
        var widths = HistoryListLayout.AllocateForScale(1.25, listWidth: 1185);

        Assert.Equal(new[] { 144, 131, 138, 256, 206 }, WithoutItem(widths));
        Assert.Equal(310, widths[HistoryListLayout.ItemColumn]);
    }

    // 150%, to pin that this is a factor and not one hard-coded step: 115 -> 172, 105 -> 158,
    // 110 -> 165, 205 -> 308, 165 -> 248. The halves round to even, exactly as WinForms' own
    // Control.LogicalToDeviceUnits rounds them (checked against it by reflection: 115 at 144 dpi is
    // 172 there too, not 173) — this was that call until the decision was pulled out of the control.
    [Fact]
    public void AllocateForScale_At150Percent_ScalesEveryFixedColumnWithTheDisplay()
    {
        Assert.Equal(
            new[] { 172, 158, 165, 308, 248 },
            WithoutItem(HistoryListLayout.AllocateForScale(1.5, listWidth: 1500)));
    }

    // Item's minimum is scaled with everything else. Left at its logical 160 it would be a fifth
    // narrower at 125% than the width it was measured to need, which is the same as not having
    // measured it.
    [Fact]
    public void AllocateForScale_At125Percent_ClampsItemAtItsScaledMinimum()
    {
        var widths = HistoryListLayout.AllocateForScale(1.25, listWidth: 500);

        Assert.Equal(200, widths[HistoryListLayout.ItemColumn]);
    }

    private static int[] WithoutItem(int[] widths) =>
        widths.Where((_, i) => i != HistoryListLayout.ItemColumn).ToArray();

    // At 125% display scaling the caller hands in widths the control has already scaled — the five
    // fixed ones must come back exactly as given, because that is the whole reason they were scaled:
    // a ListView's columns are untouched by WinForms' font scaling, so "exported (companion)" grows
    // from 125px to 158px against a Status column that would otherwise still be 165.
    [Fact]
    public void Allocate_ScaledWidths_AreHandedBackUnchangedExceptItem()
    {
        var scaled = new[] { 144, 131, 313, 138, 256, 206 };

        var widths = HistoryListLayout.Allocate(scaled, minimumItemWidth: 200, listWidth: 1185);

        Assert.Equal(new[] { 144, 131, 310, 138, 256, 206 }, widths);
    }
}
