using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using KARTCompanion.Archive;
using KARTCompanion.Config;
using KARTCompanion.Shell;
using static KARTCompanion.Shell.ShellFrame;

namespace KARTCompanion.Tests;

/// <summary>
/// The shell as a real window: the chrome that is laid out against the client size, the controls
/// that have to let the frame's resize ring through them, and the minimum size that has to fit the
/// screen it is on.
///
/// Every one of these is wiring — a line that connects a decision already pinned by
/// <see cref="ShellFrameTests"/> to the control it is about. That is exactly the class of defect this
/// project kept shipping: the geometry was right and the control was placed by hand next to it.
/// </summary>
[Collection(WinFormsCollection.Name)]
public class CompanionShellFormTests
{
    private const int WM_NCHITTEST = 0x84;

    /// <summary>"Not my window — keep looking underneath." Windows', not ours, so it is asserted as a
    /// literal.</summary>
    private const int HTTRANSPARENT = -1;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Asks a control's REAL window procedure what it makes of a point, the same question
    /// Windows asks while deciding what the mouse is over. Sent straight to the target rather than
    /// left to Windows' own walk down the z-order, so the answer is about that one control and does
    /// not depend on the window being visible, on top, or under the cursor.</summary>
    private static int HitTest(Control target, Form frame, Point framePoint)
    {
        var screenPoint = frame.PointToScreen(framePoint);
        var lParam = (IntPtr)(((screenPoint.Y & 0xFFFF) << 16) | (screenPoint.X & 0xFFFF));
        return (int)SendMessage(target.Handle, WM_NCHITTEST, IntPtr.Zero, lParam).ToInt64();
    }

    /// <summary>The shell as the tray builds it — both screens, in the same order — with real
    /// windows for everything under it.</summary>
    private static void WithShell(Action<CompanionShell, SettingsScreen, HistoryScreen> assertions)
    {
        SettingsScreen? settings = null;
        HistoryScreen? history = null;
        Bitmap? logo = null;
        try
        {
            WinFormsHarness.WithForm(
                () =>
                {
                    settings = new SettingsScreen(
                        new CompanionConfig(), _ => Task.FromResult(new SyncResult(true, 0, 0, null)));
                    history = new HistoryScreen(
                        Array.Empty<ArchivedAward>(), () => new ArchiveDocument(), _ => { });
                    logo = AppIcon.LoadLogoBitmap();
                    return new CompanionShell(
                        new IScreen[] { settings, history }, logo, AppIcon.CreateTrayIcon(logo));
                },
                shell => assertions(shell, settings!, history!));
        }
        finally
        {
            // After the form, which holds it as a PictureBox image.
            logo?.Dispose();
        }
    }

    // The shell's own content column: the rail is 64 wide, the content starts 16 past it, and 12 is
    // left at the right. Private constants in CompanionShell, so they are literals here — a test
    // written against the same constant would agree with any value it was given.
    private const int ContentLeft = 80;
    private const int RightMargin = 12;

    // --- chrome against the current client size ---

    // Every one of these used to be re-asserted from a constant on every screen switch, which is
    // fine until the window can be resized. They are laid out from ClientSize now, and this is what
    // says they still are after the user has dragged an edge.
    [WinFormsFact]
    public void Chrome_IsLaidOutAgainstWhateverSizeTheWindowHasBeenDraggedTo()
    {
        WithShell((shell, _, _) =>
        {
            var rail = WinFormsHarness.Find<Panel>(shell, "Rail");
            var railDivider = WinFormsHarness.Find<Panel>(shell, "RailDivider");
            var headerDivider = WinFormsHarness.Find<Panel>(shell, "HeaderDivider");

            foreach (var size in new[] { new Size(1200, 820), new Size(980, 520), new Size(1042, 700) })
            {
                shell.ClientSize = size;
                WinFormsHarness.Pump();

                Assert.Equal(size.Height, rail.Height);
                Assert.Equal(size.Height, railDivider.Height);
                Assert.Equal(size.Width - ContentLeft - RightMargin, headerDivider.Width);
            }
        });
    }

    // The one that shipped: the close glyph sat four pixels from the right edge, which put its two
    // rightmost pixel columns inside the six-pixel resize ring — and a child control wins the hit
    // test before the form is ever asked, so a 2x24 strip of the right edge, and part of the
    // top-right corner grip, CLOSED THE WINDOW instead of resizing it.
    //
    // ShellFrameTests already pins the arithmetic that avoids this. What it cannot see is whether
    // LayoutChrome uses it, which is the half that was broken. Asserted from the glyph's ACTUAL
    // bounds against the frame's ACTUAL ring, so a literal in LayoutChrome cannot satisfy it — and
    // at three different window widths, because the glyph's position is a function of one.
    [WinFormsFact]
    public void CloseGlyph_KeepsEveryPixelOfItselfOutOfTheResizeRing_AtEveryWindowSize()
    {
        WithShell((shell, _, _) =>
        {
            var glyph = WinFormsHarness.Find<Control>(shell, "CloseGlyph");

            foreach (var size in new[] { new Size(1042, 700), new Size(1200, 820), new Size(980, 520) })
            {
                shell.ClientSize = size;
                WinFormsHarness.Pump();

                var rightmostColumn = glyph.Right - 1;

                // Its own top and bottom rows, at its rightmost pixel column: still the form's
                // client area, so the glyph is not standing in the ring anywhere.
                Assert.Equal(FrameEdge.None, ResizeEdgeAt(new Point(rightmostColumn, glyph.Top), size));
                Assert.Equal(FrameEdge.None, ResizeEdgeAt(new Point(rightmostColumn, glyph.Bottom - 1), size));

                // And nothing is given away either: one pixel further right is already the ring, so
                // the glyph is hard against it rather than parked somewhere safe. This is the half
                // that fails if the glyph is never moved at all.
                Assert.Equal(FrameEdge.TopRight, ResizeEdgeAt(new Point(rightmostColumn + 1, glyph.Top), size));
                Assert.Equal(FrameEdge.Right, ResizeEdgeAt(new Point(rightmostColumn + 1, glyph.Bottom - 1), size));
            }
        });
    }

    // --- the resize ring, through the real window procedures ---

    // A child control eats the mouse before its parent ever sees it, so the form's own WM_NCHITTEST
    // is never asked about a pixel some control covers — and the rail covers the whole left edge,
    // the rail divider stands in the top and bottom ones, and each screen's view covers all four.
    // FrameEdgePassThrough is what makes them answer "not mine"; without it on any one of them, that
    // part of the frame's edge silently stops being grabbable, with nothing on screen to show for it.
    //
    // Asked of each control's real window procedure, so this is Windows' own answer and not a
    // restatement of the geometry.
    [WinFormsFact]
    public void EveryControlStandingInTheFramesEdge_AnswersTheRingWithHtTransparent()
    {
        WithShell((shell, settings, history) =>
        {
            var size = shell.ClientSize;
            var overTheRail = new Point(2, size.Height / 2);
            var overTheDivider = new Point(64, 2);
            var overTheViews = new Point(size.Width / 2, size.Height - 2);
            var wellInside = new Point(size.Width / 2, size.Height / 2);

            var standIns = new (string What, Control Control, Point Ring)[]
            {
                ("the rail", WinFormsHarness.Find<Panel>(shell, "Rail"), overTheRail),
                ("the rail divider", WinFormsHarness.Find<Panel>(shell, "RailDivider"), overTheDivider),
                ("the settings screen's view", settings.View, overTheViews),
                ("the history screen's view", history.View, overTheViews),
            };

            foreach (var (what, control, ring) in standIns)
            {
                Assert.True(HTTRANSPARENT == HitTest(control, shell, ring),
                    $"A point in the frame's resize ring does not fall through {what}, so that part of the edge cannot be grabbed.");

                // And only there: a pass-through that answered HTTRANSPARENT everywhere would take
                // every click, drag handle and control on the window with it.
                Assert.NotEqual(HTTRANSPARENT, HitTest(control, shell, wellInside));
            }
        });
    }

    // The other end of the same mechanism: once a stand-in has said "not mine", the form has to
    // answer with the sizing code for that edge. This is CompanionShell.WndProc, which was left to
    // the maintainer's own pass over the window until there was a way to build one here.
    [WinFormsFact]
    public void TheForm_AnswersEachEdgeOfTheRingWithItsWin32SizingCode()
    {
        WithShell((shell, _, _) =>
        {
            var size = shell.ClientSize;

            Assert.Equal(HitTestCode(FrameEdge.Left), HitTest(shell, shell, new Point(2, size.Height / 2)));
            Assert.Equal(HitTestCode(FrameEdge.Right), HitTest(shell, shell, new Point(size.Width - 2, size.Height / 2)));
            Assert.Equal(HitTestCode(FrameEdge.Top), HitTest(shell, shell, new Point(size.Width / 2, 2)));
            Assert.Equal(HitTestCode(FrameEdge.Bottom), HitTest(shell, shell, new Point(size.Width / 2, size.Height - 2)));
            Assert.Equal(HitTestCode(FrameEdge.TopRight), HitTest(shell, shell, new Point(size.Width - 2, 2)));
            Assert.Equal(HitTestCode(FrameEdge.BottomLeft), HitTest(shell, shell, new Point(2, size.Height - 2)));

            // The middle of the window is nobody's edge, which is what leaves every control and drag
            // handle alone.
            Assert.Equal(HitTestCode(FrameEdge.None), HitTest(shell, shell, new Point(size.Width / 2, size.Height / 2)));
        });
    }

    // --- the screens inside the frame ---

    // Each screen's view is anchored to all four edges, and the anchors are set AFTER the view has
    // been given the frame's size — the other order displaces every anchored control by the
    // difference. Both directions of resize, because a screen that only grows correctly is a screen
    // whose anchors were never exercised.
    [WinFormsFact]
    public void EveryScreensView_TakesTheWholeClientAreaThroughAResize()
    {
        WithShell((shell, settings, history) =>
        {
            foreach (var size in new[] { new Size(1200, 820), new Size(980, 520), new Size(1042, 700) })
            {
                shell.ClientSize = size;
                WinFormsHarness.Pump();

                Assert.Equal(size, settings.View.Size);
                Assert.Equal(size, history.View.Size);
            }
        });
    }

    // Settings positions its action row from its own view's height (it must not ask the frame — see
    // IScreen), and hangs OK off the right edge while the other two stay at the content column's
    // left. Both halves are layout the screen re-runs on every resize, and neither is visible to a
    // test of SettingsScreenText's arithmetic alone.
    [WinFormsFact]
    public void SettingsActionRow_FollowsTheBottomEdge_WithOnlyOkFollowingTheRight()
    {
        WithShell((shell, settings, _) =>
        {
            var buttons = WinFormsHarness.Descendants(settings.View).OfType<Button>().ToList();
            var ok = buttons.Single(b => b.Text == "OK");
            var cancel = buttons.Single(b => b.Text == "Cancel");
            var forceSync = buttons.Single(b => b.Text == "Force Sync");

            foreach (var size in new[] { new Size(1200, 820), new Size(980, 520) })
            {
                shell.ClientSize = size;
                WinFormsHarness.Pump();

                var expectedTop = SettingsScreenText.ActionRowTop(settings.View.Height, ok.Height);
                Assert.Equal(expectedTop, ok.Top);
                Assert.Equal(expectedTop, cancel.Top);
                Assert.Equal(expectedTop, forceSync.Top);

                Assert.Equal(size.Width - RightMargin, ok.Right);
                Assert.Equal(ContentLeft, forceSync.Left);
            }
        });
    }
}
