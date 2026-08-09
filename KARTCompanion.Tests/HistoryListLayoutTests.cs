using KARTCompanion.Shell;

namespace KARTCompanion.Tests;

/// <summary>
/// How the history list's six columns divide up the width they are given. Nothing in this suite
/// constructs a Form (see HistoryExportPlanner's remarks), so the rule lives in a pure function and
/// the ListView is only ever handed its answer.
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
