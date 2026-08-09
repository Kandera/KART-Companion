using System.Drawing;
using System.Windows.Forms;
using KARTCompanion.Archive;
using KARTCompanion.Shell;

namespace KARTCompanion.Tests;

/// <summary>
/// The history screen as real controls: the empty-state label that has to be in front of the list it
/// covers, and the columns that have to be allocated from the list's own numbers.
///
/// The screen is built on its own rather than inside a shell, because it is complete on its own (a
/// screen owns its layout and knows nothing about the frame — see IScreen) and because a control
/// only reports its own Visible while nothing above it is hidden: inside a form that has never been
/// shown, every control reads Visible=false and the one assertion this file most needs would be
/// meaningless.
/// </summary>
[Collection(WinFormsCollection.Name)]
public class HistoryScreenFormTests
{
    private static ArchivedAward Award(string id) => new()
    {
        Fields = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["time"] = 1_700_000_000d,
            ["winner"] = "Winner",
            ["item"] = "Item",
            ["reason"] = "BIS",
        },
    };

    private static void WithHistoryScreen(
        IReadOnlyList<ArchivedAward> awards, Action<HistoryScreen, ListView, Label> assertions) =>
        WinFormsHarness.Run(() =>
        {
            var screen = new HistoryScreen(awards, () => new ArchiveDocument(), _ => { });
            try
            {
                WinFormsHarness.RealiseHandles(screen.View);
                WinFormsHarness.Pump();
                assertions(
                    screen,
                    WinFormsHarness.Find<ListView>(screen.View, "HistoryList"),
                    WinFormsHarness.Find<Label>(screen.View, "HistoryEmptyLabel"));
            }
            finally
            {
                screen.View.Dispose();
                WinFormsHarness.Pump();
            }
        });

    /// <summary>What the screen's columns should be: the allocator, asked with the list's OWN
    /// display scale and the width its columns actually get. Both halves matter — a list has a
    /// one-pixel border each side, so a screen that handed over Width instead of ClientSize.Width
    /// would give the Item column two pixels it does not have.</summary>
    private static void AssertColumnsMatchTheAllocator(ListView list)
    {
        var expected = HistoryListLayout.AllocateForScale(
            list.DeviceDpi / HistoryListLayout.LogicalDpi, list.ClientSize.Width);

        Assert.Equal(expected, list.Columns.Cast<ColumnHeader>().Select(c => c.Width).ToArray());
    }

    // This shipped as a real defect: Controls.AddRange leaves the list in FRONT of the label (WinForms
    // z-order puts index 0 at the front, not the back), so an empty archive showed a blank rectangle
    // where the explanation should be. The label was there, visible, correctly sized — and painted
    // over by an opaque ListView. Nothing but the two controls' actual order can see it.
    [WinFormsFact]
    public void AnEmptyArchive_ShowsItsExplanationInFrontOfTheList()
    {
        WithHistoryScreen(Array.Empty<ArchivedAward>(), (screen, list, empty) =>
        {
            var view = screen.View;

            Assert.True(empty.Visible, "The empty-state label is hidden with nothing in the archive to hide it for.");
            Assert.True(
                view.Controls.GetChildIndex(empty) < view.Controls.GetChildIndex(list),
                "The list is in front of the empty-state label, so the label is painted over: "
                + $"label at z-index {view.Controls.GetChildIndex(empty)}, list at {view.Controls.GetChildIndex(list)} "
                + "(index 0 is the front).");

            // And it stands exactly where the list does, so there is no strip of either showing past
            // the other.
            Assert.Equal(list.Bounds, empty.Bounds);
        });
    }

    [WinFormsFact]
    public void AnArchiveWithAwardsInIt_ShowsTheListAndNotTheExplanation()
    {
        WithHistoryScreen(new[] { Award("a"), Award("b") }, (_, list, empty) =>
        {
            Assert.False(empty.Visible);
            Assert.Equal(2, list.VirtualListSize);
        });
    }

    // The columns are allocated by a pure function that HistoryListLayoutTests pins in full. What no
    // test could see is the one call that has to ask a control anything: the list's own display scale
    // and the width its columns actually get.
    [WinFormsFact]
    public void TheColumns_AreWhatTheAllocatorMakesOfTheListsOwnScaleAndClientWidth()
    {
        WithHistoryScreen(Array.Empty<ArchivedAward>(), (_, list, _) => AssertColumnsMatchTheAllocator(list));
    }

    // The list is anchored to all four edges and re-allocates its columns whenever its client size
    // changes, which is the whole point of a resizable window: more room means a wider Item column,
    // not a wider gap beside the list. Both directions, and the fixed five keep their width — the
    // slack belongs to Item alone.
    [WinFormsFact]
    public void TheColumns_FollowTheListThroughAResizeWithItemTakingTheSlack()
    {
        WithHistoryScreen(Array.Empty<ArchivedAward>(), (screen, list, empty) =>
        {
            var view = screen.View;
            var before = list.Columns.Cast<ColumnHeader>().Select(c => c.Width).ToArray();
            var marginsBefore = MarginsIn(view, list);

            const int wider = 300;
            view.Size = new Size(view.Width + wider, view.Height + 120);
            WinFormsHarness.Pump();

            AssertColumnsMatchTheAllocator(list);
            Assert.Equal(marginsBefore, MarginsIn(view, list));
            Assert.Equal(list.Bounds, empty.Bounds);

            for (var column = 0; column < before.Length; column++)
            {
                var expected = column == HistoryListLayout.ItemColumn ? before[column] + wider : before[column];
                Assert.Equal(expected, list.Columns[column].Width);
            }

            // And back down again: an allocation that only ever grows is one whose clamp has never
            // been exercised.
            view.Size = screen.MinimumViewSize;
            WinFormsHarness.Pump();
            AssertColumnsMatchTheAllocator(list);
            Assert.Equal(marginsBefore, MarginsIn(view, list));
        });
    }

    /// <summary>The gap the list keeps to each of its view's edges — the thing anchoring is actually
    /// about, stated without repeating any of the screen's private layout constants.</summary>
    private static Padding MarginsIn(Control view, Control list) =>
        new(list.Left, list.Top, view.Width - list.Right, view.Height - list.Bottom);
}
