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

    private const int GWL_STYLE = -16;
    private const int WS_VISIBLE = 0x10000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    /// <summary>Whether Windows has this control's OWN window marked as shown.
    ///
    /// Not <see cref="Control.Visible"/>, which cannot answer this here: it reports whether the
    /// control would actually be on screen, so inside a form that has never been shown EVERY control
    /// reads false and the switch this is about is invisible to it. WS_VISIBLE is the control's own
    /// bit, set from its own requested visibility whether or not its parents are showing —
    /// MEASURED, in both directions, through a switch on a form that is never shown.</summary>
    private static bool IsShown(Control control) =>
        (GetWindowLong(control.Handle, GWL_STYLE) & WS_VISIBLE) != 0;

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
    /// <param name="asBuilt">Asked of the shell the moment its constructor returns, BEFORE any
    /// control under it has been given a window. Realising the handles resizes the form, and a
    /// resize re-runs half of what the constructor did — so anything the constructor is the only
    /// thing that does has to be asked about here or not at all. See the rounded-region test.</param>
    private static void WithShell(
        Action<CompanionShell, SettingsScreen, HistoryScreen> assertions,
        Action<CompanionShell>? asBuilt = null)
    {
        SettingsScreen? settings = null;
        HistoryScreen? history = null;
        Bitmap? logo = null;
        Icon? trayIcon = null;
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
                    trayIcon = AppIcon.CreateTrayIcon(logo);
                    var shell = new CompanionShell(new IScreen[] { settings, history }, logo, trayIcon);
                    // Its own try: WithForm only disposes what build() RETURNS, so a failure in here
                    // would leave the window to the finalizer and the next test's windows to whatever
                    // that does to them.
                    try { asBuilt?.Invoke(shell); }
                    catch { shell.Dispose(); throw; }
                    return shell;
                },
                shell => assertions(shell, settings!, history!));
        }
        finally
        {
            // After the form, which holds it as a PictureBox image.
            logo?.Dispose();
            // AppIcon.CreateTrayIcon hands back Icon.FromHandle(bitmap.GetHicon()) and says so:
            // production makes exactly ONE of these for the whole process and deliberately never
            // destroys the HICON. Every test in this file makes another, so each is released here.
            // Icon.FromHandle does not take ownership, so disposing the Icon alone would leak the
            // handle it wraps — the handle is destroyed explicitly, after the form that held it.
            if (trayIcon is not null)
            {
                var handle = trayIcon.Handle;
                trayIcon.Dispose();
                DestroyIcon(handle);
            }
        }
    }

    /// <summary>The chrome the frame owns: everything that is not a screen's own content. Named
    /// controls, so this asks about the six the shell actually builds rather than about whatever
    /// happens to be the right shape.</summary>
    private static Control[] ChromeOf(CompanionShell shell) => new[]
    {
        WinFormsHarness.Find<Panel>(shell, "Rail"),
        WinFormsHarness.Find<Panel>(shell, "RailDivider"),
        WinFormsHarness.Find<Label>(shell, "Title"),
        WinFormsHarness.Find<Label>(shell, "Subtitle"),
        WinFormsHarness.Find<Panel>(shell, "HeaderDivider"),
        WinFormsHarness.Find<Control>(shell, "CloseGlyph"),
    };

    // The shell's own content column: the rail is 64 wide, the content starts 16 past it, and 12 is
    // left at the right. Private constants in CompanionShell, so they are literals here — a test
    // written against the same constant would agree with any value it was given.
    private const int ContentLeft = 80;
    private const int RightMargin = 12;

    // --- driving the window through sizes the machine will actually grant ---

    /// <summary>
    /// How big Windows' client area for this window really is, asked of Windows.
    ///
    /// NOT <see cref="Control.ClientSize"/>, which cannot answer it. Its setter writes the size that
    /// was REQUESTED into the same field the getter reads, and it does so AFTER the SetWindowPos that
    /// Windows may already have refused — so a window that was cut down to fit the display goes on
    /// reporting the size nobody gave it. MEASURED on this project's own shell: asked for 4000x3000
    /// on a machine whose window ceiling is 1461 tall, <c>ClientSize</c> came back 4000x3000 while
    /// <c>Size</c>, <c>GetClientRect</c> and every control anchored to the frame came back 4000x1461.
    ///
    /// That gap is the whole reason five tests in this file were green on a three-monitor desk and red
    /// on CI: they asserted against the size they had ASKED for.
    /// </summary>
    private static Size ClientSizeOf(Form shell)
    {
        Assert.True(GetClientRect(shell.Handle, out var client),
            "Windows would not say how big this window's client area is.");
        return new Size(client.Right - client.Left, client.Bottom - client.Top);
    }

    /// <summary>Resizes the window and answers with the size it ACTUALLY took — which is what every
    /// assertion driven from a resize is made against, and never the size that was asked for.</summary>
    private static Size ResizeTo(Form shell, Size wanted)
    {
        shell.ClientSize = wanted;
        WinFormsHarness.Pump();
        return ClientSizeOf(shell);
    }

    /// <summary>How far above the window's own minimum the largest probe size reaches, when the
    /// machine has the room. Far enough apart that the three sizes are visibly different windows, and
    /// small enough that a desk spanning three monitors is not asked for a 7700-pixel one.</summary>
    private const int ProbeHeadroom = 300;

    /// <summary>
    /// Three client sizes to drive this window through, built from THIS machine instead of from
    /// numbers that happened to fit the author's desk.
    ///
    /// THE CEILING is <see cref="SystemInformation.MaxWindowTrackSize"/>, which is what Windows will
    /// not let a window exceed. MEASURED as the binding constraint, in two places: here it is
    /// 7700x1461 on a virtual desktop of 7680x1441 (the screen plus a frame allowance), and a window
    /// asked for exactly that got exactly that; on windows-latest, whose one display is 1024x768, the
    /// shell asked for 1200x820 and came back 1044x788 — 1024+20 by 768+20, the same rule. The
    /// primary screen's WORKING area is NOT the constraint: it is smaller than the ceiling in both
    /// measurements (2560x1392 here, and CI's window was larger than its whole screen).
    ///
    /// THE FLOOR is the window's own <see cref="Form.MinimumSize"/>, which WinForms will not let it go
    /// under. Asking for less does not fail — the window silently snaps up, and three sizes below the
    /// floor would be three assertions at one size.
    ///
    /// THE ORDER, middle then largest then floor, makes one growth and one shrink certain whatever
    /// size the window opened at: anchors that only work in one direction are the defect this order
    /// exists for, and on a small display "largest" can be barely above the size the window opens at.
    ///
    /// NOT A CLOSED LOOP: these are derived from the ceiling and the floor, while every assertion is
    /// derived from <see cref="ClientSizeOf"/> — what Windows says the window became. A layout wired
    /// to a constant instead of to the client size still goes red (measured; see the report).
    /// </summary>
    private static Size[] ResizeProbeSizes(Form shell)
    {
        var ceiling = SystemInformation.MaxWindowTrackSize;
        var floor = shell.MinimumSize;
        var largest = new Size(
            Math.Min(ceiling.Width, floor.Width + ProbeHeadroom),
            Math.Min(ceiling.Height, floor.Height + ProbeHeadroom));
        var middle = new Size((floor.Width + largest.Width) / 2, (floor.Height + largest.Height) / 2);
        return new[] { middle, largest, floor };
    }

    /// <summary>
    /// Drives <paramref name="shell"/> through <see cref="ResizeProbeSizes"/> and hands
    /// <paramref name="atEachSize"/> the size the window ACTUALLY took each time.
    ///
    /// AND REFUSES TO BE VACUOUS: the three sizes have to come back different from one another in
    /// both dimensions. A display with no room to move this window would otherwise run every
    /// assertion below three times at one size, and "the same gap at three heights" and "the glyph's
    /// place at three widths" would be pinning nothing while reporting green. This is deliberately a
    /// loud failure rather than a skip: every assertion these tests make is relative to the size the
    /// window took, so they stay honest on a display far smaller than the layout's own minimum — the
    /// shell cuts its minimum down to the screen and goes on laying itself out — and the only machine
    /// this can fail on is one with no room between that minimum and the ceiling at all, where the
    /// right answer is to say so and not to pass.
    /// </summary>
    private static void ThroughAResize(Form shell, Action<Size> atEachSize)
    {
        var wantedSizes = ResizeProbeSizes(shell);
        var taken = new List<Size>();
        foreach (var wanted in wantedSizes)
        {
            var actual = ResizeTo(shell, wanted);
            taken.Add(actual);
            atEachSize(actual);
        }

        Assert.True(
            taken.Select(s => s.Width).Distinct().Count() == taken.Count
            && taken.Select(s => s.Height).Distinct().Count() == taken.Count,
            "This display has no room to resize the window: asked for "
            + string.Join(", ", wantedSizes) + " between a minimum of " + shell.MinimumSize
            + " and a ceiling of " + SystemInformation.MaxWindowTrackSize + ", the window took "
            + string.Join(", ", taken) + " — so everything above was asserted more than once at the "
            + "same size and pins nothing about a resize.");
    }

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
            var statusDot = WinFormsHarness.Find<Panel>(shell, "RailStatusDot");
            var title = WinFormsHarness.Find<Label>(shell, "Title");
            var subtitle = WinFormsHarness.Find<Label>(shell, "Subtitle");

            // The rail's own width, which everything in the content column is measured from and which
            // nothing else here asserts: the header divider and the buttons agree with it through
            // ContentLeft, so widening the rail alone moves them all and no test notices.
            Assert.Equal(64, rail.Width);

            // And the status dot is centred ACROSS the rail rather than parked against one of its
            // edges. Stated as "the same gap either side", not as the arithmetic that produces it, so
            // a literal Left cannot satisfy it: at Left 0 the dot is a smudge on the rail's own edge,
            // half of it under the window's left resize ring.
            Assert.Equal(rail.Width - statusDot.Right, statusDot.Left);

            var gapsUnderTheStatusDot = new List<int>();

            ThroughAResize(shell, size =>
            {
                Assert.Equal(size.Height, rail.Height);
                Assert.Equal(size.Height, railDivider.Height);

                // Left as well as width. The divider spans the content column exactly — from where
                // the content starts to the right margin — and a divider that starts anywhere else is
                // a line drawn across the rail, which asserting its width alone cannot see.
                Assert.Equal(ContentLeft, headerDivider.Left);
                Assert.Equal(size.Width - RightMargin, headerDivider.Right);
                // And it is UNDER the header text rather than through it. Its Top is the other
                // number nothing asserted.
                Assert.True(headerDivider.Top >= Math.Max(title.Bottom, subtitle.Bottom),
                    $"The header divider is drawn through the header text: divider top {headerDivider.Top}, "
                    + $"title bottom {title.Bottom}, subtitle bottom {subtitle.Bottom}.");

                // The status dot rides the BOTTOM of the rail, so it keeps the same gap below it at
                // every window height. Stated as "the same gap", not as the constant that produces
                // it, so a literal Top cannot satisfy it at more than one size.
                gapsUnderTheStatusDot.Add(rail.Height - statusDot.Bottom);
                Assert.True(statusDot.Top >= 0 && statusDot.Bottom <= rail.Height,
                    $"The rail's status dot is outside the rail at {size}: dot {statusDot.Bounds}, rail height {rail.Height}.");
            });

            Assert.True(gapsUnderTheStatusDot.Distinct().Count() == 1,
                "The rail's status dot does not follow the rail's bottom edge — the gap below it came out as "
                + string.Join(", ", gapsUnderTheStatusDot) + " at the three window heights.");
        });
    }

    // --- the chrome's z-order ---

    // Each screen's View is added to the form BEFORE the chrome and covers the entire client area,
    // and Controls.Add appends to the BACK of the z-order (index 0 is the FRONT). Without the
    // BringToFront pass at the end of the constructor the rail, the rail divider, the title, the
    // subtitle, the header divider and the close button all sit behind a view that covers them: the
    // window opens with no rail, no logo, no nav icons, no title and no way to close it, while every
    // one of those controls is present, visible and correctly sized.
    //
    // That is the same defect the empty-state label shipped one level down (see
    // HistoryScreenFormTests), one level up the tree. No test that sends a message to a control's own
    // window can see it: a SendMessage straight to a target is z-order-blind by construction, which
    // is exactly why the resize-ring test below cannot stand in for this one.
    [WinFormsFact]
    public void EveryPieceOfChrome_StandsInFrontOfTheScreenViewsThatCoverIt()
    {
        WithShell((shell, settings, history) =>
        {
            // The premise, restated here rather than assumed: the views really do cover the whole
            // window, so anything behind one of them is invisible.
            foreach (var view in new[] { settings.View, history.View })
                Assert.Equal(shell.ClientSize, view.Size);

            var frontmostView = new[] { settings.View, history.View }.Min(v => shell.Controls.GetChildIndex(v));

            foreach (var chrome in ChromeOf(shell))
            {
                Assert.True(shell.Controls.GetChildIndex(chrome) < frontmostView,
                    $"\"{chrome.Name}\" is behind a screen's view, which covers the whole window, so it is "
                    + $"painted over and the window opens without it: {chrome.Name} at z-index "
                    + $"{shell.Controls.GetChildIndex(chrome)}, the frontmost view at {frontmostView} "
                    + "(index 0 is the front).");
            }
        });
    }

    // --- one screen at a time ---

    // Both views are kept alive for the life of the window and all but the current one is hidden. If
    // that switch is ever lost, both are shown at once and whichever is in front is painted over the
    // other — the same "everything is there and none of it can be seen" defect as the z-order above.
    //
    // Asked of Windows rather than of Control.Visible, which cannot answer inside a form that has
    // never been shown: see IsShown.
    [WinFormsFact]
    public void OnlyTheCurrentScreensView_IsShown_AndTheRailSwitchesWhichOne()
    {
        WithShell((shell, settings, history) =>
        {
            Assert.Same(settings, shell.Current);
            Assert.True(IsShown(settings.View), "The screen the window opens on is not shown.");
            Assert.False(IsShown(history.View),
                "Both screens are shown at once, so one of them is painted over the other.");

            var navIcons = WinFormsHarness.Descendants(WinFormsHarness.Find<Panel>(shell, "Rail"))
                .Where(c => c.Name == "NavIcon").ToList();
            Assert.Equal(2, navIcons.Count);

            // One icon per screen, in the order the screens were given to the shell.
            WinFormsHarness.RaiseClick(navIcons[1]);
            WinFormsHarness.Pump();

            Assert.Same(history, shell.Current);
            Assert.True(IsShown(history.View), "The screen just navigated to is not shown.");
            Assert.False(IsShown(settings.View), "The screen navigated away from is still shown behind the new one.");
        });
    }

    // Everything the frame says about WHICH screen you are on, which is all of it: the accent bar
    // beside one nav icon, the subtitle under the title, and the rail's health dot. The test above
    // pins that the right VIEW is shown; none of these three is visible to it, and all three used to
    // survive being switched off — an accent bar on every item, a subtitle that never changes, and a
    // dot that keeps reporting the screen you navigated away from.
    //
    // Asked of Windows rather than of Control.Visible for the same reason as the test above: inside a
    // form that has never been shown every control reads false. See IsShown.
    [WinFormsFact]
    public void TheRail_MarksAndNamesWhicheverScreenIsCurrent()
    {
        WithShell((shell, settings, history) =>
        {
            var rail = WinFormsHarness.Find<Panel>(shell, "Rail");
            var subtitle = WinFormsHarness.Find<Label>(shell, "Subtitle");
            var statusDot = WinFormsHarness.Find<Panel>(shell, "RailStatusDot");
            var accents = WinFormsHarness.Descendants(rail).Where(c => c.Name == "NavAccent").ToList();
            var navIcons = WinFormsHarness.Descendants(rail).Where(c => c.Name == "NavIcon").ToList();
            Assert.Equal(2, accents.Count);
            Assert.Equal(2, navIcons.Count);

            // The premise the dot half rests on, restated rather than assumed: these two screens
            // disagree about whether they have a health to report at all, which is what makes the dot
            // appearing and disappearing observable.
            Assert.True(settings.StatusColor.HasValue, "The settings screen reports no status colour, so the rail dot cannot be seen to follow it.");
            Assert.Null(history.StatusColor);

            Assert.True(IsShown(accents[0]), "Nothing in the rail marks the screen the window opened on.");
            Assert.False(IsShown(accents[1]),
                "Every nav item is marked as current at once, so the rail says nothing about which screen you are on.");
            Assert.Equal(settings.Title, subtitle.Text);
            Assert.True(IsShown(statusDot), "The rail's health dot is hidden on a screen that reports a status colour.");
            Assert.Equal(settings.StatusColor!.Value, (Color)statusDot.Tag!);

            WinFormsHarness.RaiseClick(navIcons[1]);
            WinFormsHarness.Pump();

            Assert.False(IsShown(accents[0]), "The nav item for the screen navigated AWAY from is still marked as current.");
            Assert.True(IsShown(accents[1]), "The nav item for the screen just navigated to is not marked as current.");
            Assert.Equal(history.Title, subtitle.Text);
            Assert.False(IsShown(statusDot),
                "The rail's health dot is still shown on a screen that reports no status, so it is reporting the "
                + "health of the screen you left.");
        });
    }

    // The other half of the same switch, and the half that cannot be seen while the window is the
    // thing being looked at: Form.Text is the window's TASKBAR BUTTON, its Alt-Tab entry and its
    // task-switcher label. It is the only thing that says what this window is when it is not on
    // screen, and nothing asserted it — the subtitle above is the on-screen half and was pinned, this
    // one was named in the last report and left.
    //
    // The whole caption and not just the screen's own title: "Loot History" alone in the task
    // switcher does not say whose, and a tray application's window is found by that entry rather than
    // by being where the user left it. The screen's half is interpolated rather than spelled out, so
    // this says "the caption follows the current screen" and leaves what the screens are called to
    // the test above.
    [WinFormsFact]
    public void TheWindowsCaption_NamesTheApplication_AndWhicheverScreenIsCurrent()
    {
        WithShell((shell, settings, history) =>
        {
            var navIcons = WinFormsHarness.Descendants(WinFormsHarness.Find<Panel>(shell, "Rail"))
                .Where(c => c.Name == "NavIcon").ToList();

            Assert.Equal($"KART Companion — {settings.Title}", shell.Text);

            WinFormsHarness.RaiseClick(navIcons[1]);
            WinFormsHarness.Pump();

            Assert.Equal($"KART Companion — {history.Title}", shell.Text);
        });
    }

    // --- the window's own shape ---

    // FormBorderStyle.None leaves no OS-drawn edge, so the rounded card the mockup asks for is a
    // Region on the form (Theme.ApplyRoundedFormRegion) — without it the window has hard right-angled
    // corners against the desktop. The Region is rebuilt on every Resize, which since the window
    // became draggable by its edges is every frame of a live drag, so this asks at the size the
    // window has been dragged to and not only at the one it opened with.
    //
    // AND at the size it opens with, which is a separate mechanism and needs asking about separately:
    // ApplyRoundedFormRegion rounds the window ONCE as it is built and then rebuilds the shape on
    // every Resize. Removing that first, direct application leaves this test green if it is asked
    // anywhere but in `asBuilt` — giving the form a window handle is itself a resize (MEASURED: the
    // shape comes back even with the constructor's own call deleted), and so is every ClientSize in
    // the loop. So the window a user actually opens would have square corners until they dragged an
    // edge, and only an assertion made before Windows has been involved at all can see it.
    [WinFormsFact]
    public void TheWindow_IsRoundedAtWhateverSizeItHasBeenDraggedTo()
    {
        WithShell(
            (shell, _, _) => ThroughAResize(shell, size => AssertRounded(shell, size)),
            // Size and not ClientSize: the Region is built from the window's OUTER rectangle
            // (Theme.BuildRoundedPath takes form.Width and form.Height), the two are the same
            // rectangle on a borderless form, and there is no window here to ask Windows about yet.
            asBuilt: shell => AssertRounded(shell, shell.Size));
    }

    /// <summary>The window's corners are cut and its edges are not, at the size it is now.</summary>
    private static void AssertRounded(Form shell, Size size)
    {
        var region = shell.Region;
        Assert.True(region is not null, $"The window has no Region at {size}, so its corners are square.");

        Assert.False(region!.IsVisible(new Point(0, 0)),
            $"The window's top-left corner pixel is part of the window at {size}, so the corner is square.");
        Assert.False(region.IsVisible(new Point(size.Width - 1, size.Height - 1)),
            $"The window's bottom-right corner pixel is part of the window at {size}, so the corner is square.");

        // And the shape is the size the window is NOW: a Region applied once and never
        // rebuilt would clip away everything past the size the window opened at.
        Assert.True(region.IsVisible(new Point(size.Width / 2, size.Height - 1)),
            $"The middle of the window's bottom edge is outside its own Region at {size}, so the "
            + "rounded shape is a stale one from another size.");
        Assert.True(region.IsVisible(new Point(size.Width - 1, size.Height / 2)),
            $"The middle of the window's right edge is outside its own Region at {size}, so the "
            + "rounded shape is a stale one from another size.");
    }

    // --- what the window can be dragged by ---

    // FormBorderStyle.None removes the title bar a window is normally moved by, so every inert thing
    // on the frame is wired up as one (Theme.MakeDragHandle). A child control eats the mouse before
    // its parent ever sees it, so each of these is a strip the window CANNOT be moved by if its own
    // handle is missing — which is exactly how the rail once ended up movable only in the bare gaps
    // between its children.
    [WinFormsFact]
    public void EveryInertPieceOfChrome_IsADragHandle_AndTheNavIconsDeliberatelyAreNot()
    {
        WithShell((shell, _, _) =>
        {
            var rail = WinFormsHarness.Find<Panel>(shell, "Rail");

            var inert = new List<Control>
            {
                rail,
                WinFormsHarness.Find<PictureBox>(shell, "LogoBox"),
                WinFormsHarness.Find<Panel>(shell, "RailStatusDot"),
                WinFormsHarness.Find<Label>(shell, "Title"),
                WinFormsHarness.Find<Label>(shell, "Subtitle"),
            };
            inert.AddRange(WinFormsHarness.Descendants(rail).Where(c => c.Name == "NavAccent"));
            Assert.Equal(7, inert.Count);

            foreach (var control in inert)
            {
                Assert.True(WinFormsHarness.IsADragHandle(control),
                    $"\"{control.Name}\" is not a drag handle, so it is a dead strip of the frame the window "
                    + "cannot be moved by, with nothing on screen to say so.");
            }

            // And the nav icons are not, deliberately: they have a click of their own, and a drag
            // handle on top of it would start a window move on every press of them.
            foreach (var icon in WinFormsHarness.Descendants(rail).Where(c => c.Name == "NavIcon"))
            {
                Assert.False(WinFormsHarness.IsADragHandle(icon),
                    "A nav icon is a drag handle, so pressing it starts moving the window instead of navigating.");
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

            ThroughAResize(shell, size =>
            {
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
            });
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

    // --- the minimum size against the screen it is on ---

    // A minimum larger than the screen is a window the user cannot get out of: it cannot be sized to
    // fit, and nothing on screen says why. The shell's minimum is set in logical pixels and WinForms
    // scales it with the display, so a scaled display is what hands it one of these.
    //
    // WinForms does constrain a minimum itself, and MEASURED (not assumed) it constrains it to a
    // working area — but the working area of whichever screen the PROPOSED RECTANGLE overlaps most,
    // which on more than one display is not the screen the window is on. That is the gap this pins:
    // the window sits on the screen with the least room, and is handed a minimum whose rectangle
    // reaches across the whole desktop, so WinForms' own constraint measures it against a roomier
    // screen than the one it is on.
    //
    // WHERE THIS IS VACUOUS, AND IT IS VACUOUS ON CI: it needs a second screen with more room on it.
    // On a single-screen machine WinForms' own constraint already answers with the right screen and
    // this passes whether or not the MinimumSize override below it exists. windows-latest, which is
    // the only place this suite runs unattended, has ONE display — so this test and the one after it
    // are green there for the wrong reason. Read a CI pass on these two as "did not break the
    // build", not as "the clamp works".
    //
    // WHAT THIS STILL BUYS, now that the clamp is also driven over an invented layout further down
    // (…_OnAnInventedLayout): those two supply the shell's working-area lookup themselves, so they
    // pin the WIRING and say nothing about the lookup the application actually uses. This pair is the
    // only thing in the suite that runs the whole thing over REAL monitors — Screen.FromRectangle,
    // real working areas, WinForms' own constraint stacked on top of ours — and on a multi-screen
    // desk it is not vacuous. Keep both: they are the integration end of a mechanism whose unit end
    // now has its own tests, and they cost one form each.
    [WinFormsFact]
    public void AMinimumBiggerThanTheScreenTheWindowIsOn_IsCutDownAsItIsSet()
    {
        WithShell((shell, _, _) =>
        {
            var tightest = Screen.AllScreens
                .OrderBy(s => (long)s.WorkingArea.Width * s.WorkingArea.Height).First();
            shell.Location = tightest.WorkingArea.Location;
            WinFormsHarness.Pump();

            var everyScreen = Screen.AllScreens.Select(s => s.Bounds).Aggregate(Rectangle.Union);
            // Comfortably across every display, so the rectangle this implies is certain to overlap
            // some other screen more than the one the window is standing on.
            shell.MinimumSize = new Size(everyScreen.Width * 2, everyScreen.Height * 2);
            WinFormsHarness.Pump();

            var workingArea = Screen.FromRectangle(shell.Bounds).WorkingArea.Size;
            Assert.True(
                shell.MinimumSize.Width <= workingArea.Width && shell.MinimumSize.Height <= workingArea.Height,
                $"The window cannot be made to fit the screen it is on: minimum {shell.MinimumSize}, working area {workingArea}.");
        });
    }

    // The other half, and the one WinForms does nothing about: a minimum that fitted the screen it
    // was set on does not fit any more once the window has been dragged onto a smaller one, and
    // nothing assigns it again on the way there.
    //
    // WHERE THIS IS VACUOUS, AND IT IS VACUOUS ON CI: it needs a second screen with less room on it
    // than the first. On a single-screen machine the window never arrives anywhere new and this
    // passes either way — including on windows-latest, which has one display. See the test above for
    // what that means for both of these, and for what they are still kept for.
    [WinFormsFact]
    public void AWindowDraggedOntoASmallerScreen_HasItsMinimumCutToThatScreen()
    {
        WithShell((shell, _, _) =>
        {
            var byRoom = Screen.AllScreens
                .OrderBy(s => (long)s.WorkingArea.Width * s.WorkingArea.Height).ToList();
            var tightest = byRoom.First();
            var roomiest = byRoom.Last();

            shell.Location = roomiest.WorkingArea.Location;
            WinFormsHarness.Pump();
            shell.MinimumSize = roomiest.WorkingArea.Size;
            var takenOnTheRoomiestScreen = shell.MinimumSize;

            // One pixel in, so the window really arrives somewhere new even when both of those are
            // the same screen — otherwise the move this is about would not happen at all.
            shell.Location = new Point(tightest.WorkingArea.X + 1, tightest.WorkingArea.Y);
            WinFormsHarness.Pump();

            // The invariant, not an exact size: WinForms applies a constraint of its own on top of
            // this one (to a working area, less two pixels — measured, see the MinimumSize override),
            // and pinning the arithmetic of two clamps stacked would be pinning WinForms' half of it.
            var nowOn = Screen.FromRectangle(shell.Bounds).WorkingArea.Size;
            Assert.True(
                shell.MinimumSize.Width <= nowOn.Width && shell.MinimumSize.Height <= nowOn.Height,
                $"A minimum of {takenOnTheRoomiestScreen} came along to a screen with {nowOn} of room and is still "
                + $"{shell.MinimumSize}: the window cannot be made to fit the screen it is on.");
        });
    }

    /// <summary>
    /// A display layout that does not exist on whatever machine is running this — the seam the two
    /// tests below are built on (see CompanionShell's <c>workingAreaOf</c> parameter).
    ///
    /// It answers the one question the shell asks about the display the way
    /// <see cref="Screen.FromRectangle"/> answers it: with the screen a rectangle OVERLAPS MOST. That
    /// is not decoration — it is the whole defect the shell's MinimumSize override exists for, and a
    /// layout that answered by position alone could not tell the window's own rectangle apart from
    /// the oversized one being proposed.
    ///
    /// Nothing here is a spy: both tests below assert about the size the shell ends up with, so a
    /// layout that always gave the same answer would fail one of them (the first expects the tight
    /// screen's working area, the second expects the roomy one's and then the tight one's).
    /// </summary>
    private sealed class InventedScreens
    {
        /// <summary>Bigger than any minimum these tests hand the shell, so clamping against it is a
        /// no-op. See WorkingAreaOf for when it is used and why it has to be a no-op.</summary>
        private static readonly Size RoomForAnything = new(100_000, 100_000);

        private readonly (Rectangle Bounds, Size WorkingArea)[] _screens;

        public InventedScreens(params (Rectangle Bounds, Size WorkingArea)[] screens) => _screens = screens;

        public Size WorkingAreaOf(Rectangle rectangle)
        {
            (Rectangle Bounds, Size WorkingArea)? best = null;
            var mostOverlap = 0L;
            foreach (var screen in _screens)
            {
                var shared = Rectangle.Intersect(screen.Bounds, rectangle);
                var overlap = (long)shared.Width * shared.Height;
                if (overlap > mostOverlap) { mostOverlap = overlap; best = screen; }
            }

            // A rectangle on none of these is not hypothetical and is not the test's doing: WinForms
            // places the window itself (StartPosition.CenterScreen) as its handle is created, and
            // that first position is somewhere on the REAL desktop — MEASURED, it is where the shell
            // is asked its first question, before any test has said anything. Answering with room
            // enough for anything is the only answer that leaves the minimum where the constructor
            // put it. It cannot make a test pass for the wrong reason either: both tests below expect
            // an EXACT size, and a lookup that fell through to this would produce neither of them.
            return best?.WorkingArea ?? RoomForAnything;
        }
    }

    /// <summary>Two screens side by side, deliberately far smaller than any real display: every
    /// minimum these tests assert about has to survive WinForms' OWN constraint, which is applied on
    /// top of the shell's and measured against the REAL screen (a working area, less two pixels — see
    /// the MinimumSize override). Numbers this small are under that on any machine, so what comes
    /// back is the shell's clamp and nothing else.</summary>
    private static InventedScreens TwoScreens() => new(
        (new Rectangle(0, 0, 400, 200), new Size(400, 200)),
        (new Rectangle(400, 0, 600, 480), new Size(600, 480)));

    // Stub screens small enough that the window fits inside one invented display: the frame opens at
    // the largest of its screens' views, and a window bigger than the layout it is being clamped
    // against would overlap both screens at once and make the arithmetic below meaningless.
    private static StubScreen[] SmallScreens() => new[]
    {
        new StubScreen("Small", new Size(300, 150), new Size(100, 100)),
    };

    // THE CLAMP, DRIVEN OVER GEOMETRY INSTEAD OF HARDWARE — this is the one that is not vacuous on a
    // single-display machine, and so not vacuous on windows-latest.
    //
    // The window sits wholly on the 400x200 screen. The minimum it is then handed implies a rectangle
    // that reaches across both, and overlaps the 600x480 one far more — which is exactly the case
    // WinForms' own constraint gets wrong, because it measures the PROPOSED rectangle. The shell has
    // to measure the window's own.
    //
    // Three separate mutants land on this one assertion: not clamping at all (the minimum comes back
    // at the real screen's working area, which is neither of these numbers), clamping against the
    // proposed rectangle (600x480), and reaching past the seam to ask Windows directly (the real
    // screen again).
    [WinFormsFact]
    public void AMinimumTooBigForTheScreenTheWindowIsOn_IsCutToThatScreen_OnAnInventedLayout()
    {
        WithStubShell(SmallScreens(), (shell, _) =>
        {
            shell.Location = new Point(10, 10);
            WinFormsHarness.Pump();

            shell.MinimumSize = new Size(5000, 5000);
            WinFormsHarness.Pump();

            Assert.Equal(new Size(400, 200), shell.MinimumSize);
        }, TwoScreens().WorkingAreaOf);
    }

    // The other half, over the same invented layout: a minimum that fitted the screen it was set on
    // does not fit any more once the window has moved, and nothing assigns it again on the way there.
    //
    // The first two assertions are half the test, and neither is decoration. Without the first, a
    // shell that clamped against one fixed screen — the primary, the smallest, the first one it found
    // — would still pass the last. Without the SECOND, a re-clamp that asked about the right size but
    // the wrong POSITION would too: a move within the roomy screen has to leave the minimum alone,
    // and that is only visible when the window moves without changing screens. (Measured: with only
    // the first two steps, a re-clamp that asks about a rectangle at the origin instead of the
    // window's own survives this test.)
    [WinFormsFact]
    public void AWindowMovedOntoASmallerScreen_HasItsMinimumCutToIt_OnAnInventedLayout()
    {
        WithStubShell(SmallScreens(), (shell, _) =>
        {
            shell.Location = new Point(400, 0);
            WinFormsHarness.Pump();

            // An override of OnLocationChanged that does not call base swallows the event for
            // everyone else on the window. Nothing subscribes to it in the application today, which
            // is exactly why it would be found by whoever first did.
            var moves = 0;
            shell.LocationChanged += (_, _) => moves++;

            // Fits the roomy screen in both dimensions, and is too tall for the tight one.
            shell.MinimumSize = new Size(380, 300);
            WinFormsHarness.Pump();
            Assert.Equal(new Size(380, 300), shell.MinimumSize);

            // Moved, but still wholly on the roomy screen: nothing to cut.
            shell.Location = new Point(500, 100);
            WinFormsHarness.Pump();
            Assert.Equal(new Size(380, 300), shell.MinimumSize);

            shell.Location = new Point(0, 0);
            WinFormsHarness.Pump();

            // Only the height had to give: the width already fitted, and clamping it too would be
            // taking room away for no reason.
            Assert.Equal(new Size(380, 200), shell.MinimumSize);

            // At least, rather than exactly: both moves above have to be reported, and Windows is
            // free to raise more of its own.
            Assert.True(moves >= 2, $"Both moves should have been announced; {moves} were.");
        }, TwoScreens().WorkingAreaOf);
    }

    /// <summary>
    /// THE DELIBERATE HALF, PINNED SO THAT CHANGING IT IS A DECISION AND NOT A SLIP: the cut is one
    /// way. Nothing remembers what the minimum was before a small screen took it away, so a window
    /// that visits one keeps the smaller minimum for the rest of the session even after it is dragged
    /// back somewhere roomy. CompanionShell's MinimumSize override says exactly that in its own
    /// remarks, and until now said "there is no test either way".
    ///
    /// It is a real behaviour and not an absence: the user's window can be made smaller than its
    /// content wants for the rest of the session, which the override argues is a usable window and
    /// the lesser of the two costs. The alternative — remembering the requested minimum and restoring
    /// it on the way back — is a second stored size that has to be kept in step with the screens' own
    /// minimums, and it is one five-line change away. This test is what makes that change announce
    /// itself: MEASURED, implementing the grow-back turns this red and leaves every other test in
    /// this file green, so without it the design would move without anyone deciding to move it.
    ///
    /// Over the invented layout, so it is not vacuous on a single-display machine and not vacuous on
    /// windows-latest — see InventedScreens.
    /// </summary>
    [WinFormsFact]
    public void AMinimumCutDownOnASmallScreen_IsNotGrownBack_WhenTheWindowLeavesAgain()
    {
        WithStubShell(SmallScreens(), (shell, _) =>
        {
            shell.Location = new Point(400, 0);
            WinFormsHarness.Pump();

            // Fits the roomy screen in both dimensions, and is too tall for the tight one.
            shell.MinimumSize = new Size(380, 300);
            WinFormsHarness.Pump();
            Assert.Equal(new Size(380, 300), shell.MinimumSize);

            shell.Location = new Point(0, 0);
            WinFormsHarness.Pump();
            Assert.Equal(new Size(380, 200), shell.MinimumSize);

            // Back where it started, with room for the whole 300 again.
            shell.Location = new Point(400, 0);
            WinFormsHarness.Pump();

            Assert.Equal(new Size(380, 200), shell.MinimumSize);
        }, TwoScreens().WorkingAreaOf);
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
            ThroughAResize(shell, size =>
            {
                Assert.Equal(size, settings.View.Size);
                Assert.Equal(size, history.View.Size);
            });
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

            ThroughAResize(shell, size =>
            {
                var expectedTop = SettingsScreenText.ActionRowTop(settings.View.Height, ok.Height);
                Assert.Equal(expectedTop, ok.Top);
                Assert.Equal(expectedTop, cancel.Top);
                Assert.Equal(expectedTop, forceSync.Top);

                Assert.Equal(size.Width - RightMargin, ok.Right);
                Assert.Equal(ContentLeft, forceSync.Left);
            });
        });
    }

    // --- the frame's own decisions, against screens that disagree ---

    /// <summary>
    /// A screen that is nothing but the three answers the frame asks a screen for: how big its view
    /// is, how small it may get, and whether Escape belongs to it.
    ///
    /// WHY A DOUBLE HERE AND NOWHERE ELSE IN THIS FILE: the two real screens lay themselves out at
    /// exactly the same size (1042x700) and neither of the two constructor lines below can be seen
    /// through them — "the largest of the screens' sizes" and "the first screen's size" are the same
    /// number, so the mutant that replaces one with the other is alive with the whole rest of this
    /// file green. The defect those lines exist to fix ("the window changed width on every switch")
    /// only exists when screens disagree, so a test of them has to supply screens that do.
    /// </summary>
    private sealed class StubScreen : IScreen
    {
        public StubScreen(
            string title, Size viewSize, Size minimumViewSize, bool wantsEscape = false, bool wantsEnter = false)
        {
            Title = title;
            View = new Panel { Name = "StubView", Size = viewSize };
            MinimumViewSize = minimumViewSize;
            if (wantsEscape) CancelButton = new Button { Name = "StubCancel" };
            if (wantsEnter) AcceptButton = new Button { Name = "StubAccept" };
        }

        public string Title { get; }
        public Theme.IconGlyph Glyph => Theme.IconGlyph.Sliders;
        public Control View { get; }
        public Size MinimumViewSize { get; }
        public Size ViewSizeAsBuilt => View.Size;
        public Color? StatusColor => null;
        public IButtonControl? CancelButton { get; }
        public IButtonControl? AcceptButton { get; }
        public event EventHandler? StatusChanged { add { } remove { } }

        /// <summary>How many times this screen has been told it has become the current one. The real
        /// history screen re-reads the archive from that call, so "how many times" is the whole
        /// question — see the test that counts it.</summary>
        public int TimesShown { get; private set; }

        public void OnShown() => TimesShown++;
    }

    /// <summary>The shell over screens this test supplies, with the icon it was handed available to
    /// assert about. Same disposal contract as WithShell — see there for the HICON.</summary>
    /// <param name="workingAreaOf">The display layout the shell should clamp against, or null to let
    /// it ask Windows as the application does. See <see cref="InventedScreens"/>.</param>
    private static void WithStubShell(
        IReadOnlyList<StubScreen> screens,
        Action<CompanionShell, Icon> assertions,
        Func<Rectangle, Size>? workingAreaOf = null)
    {
        Bitmap? logo = null;
        Icon? trayIcon = null;
        try
        {
            WinFormsHarness.WithForm(
                () =>
                {
                    logo = AppIcon.LoadLogoBitmap();
                    trayIcon = AppIcon.CreateTrayIcon(logo);
                    return new CompanionShell(screens.Cast<IScreen>().ToList(), logo, trayIcon, workingAreaOf);
                },
                shell => assertions(shell, trayIcon!));
        }
        finally
        {
            logo?.Dispose();
            if (trayIcon is not null)
            {
                var handle = trayIcon.Handle;
                trayIcon.Dispose();
                DestroyIcon(handle);
            }
        }
    }

    // The two sizes the constructor decides once, and the defect its own comment says they were
    // built to fix: each screen used to dictate ClientSize through its own View.Size, so the window
    // changed width every time the user clicked the other rail icon.
    //
    // ShellFrameTests pins LargestOf as a function. What it cannot see is whether the constructor
    // calls it, or calls it with the right sizes — and both of those lines survive being replaced by
    // the first screen's numbers when every screen has the same numbers, which the two real ones do.
    //
    // The minimums here are deliberately SMALLER than either view, in both dimensions. WinForms grows
    // a form to its own MinimumSize as that is assigned, and the assignment is two lines after the
    // ClientSize one — so a minimum that reached past a view size would grow the window back and hide
    // exactly the mutant this is here to catch.
    [WinFormsFact]
    public void TheFrame_OpensAtTheLargestOfItsScreens_AndStopsAtTheLargestOfTheirMinimums()
    {
        // Widest first, tallest second, so neither number can come from one screen alone: the frame
        // that fits both is 800 x 400, which is neither screen's own size.
        var widest = new StubScreen("Widest", new Size(800, 300), new Size(500, 100));
        var tallest = new StubScreen("Tallest", new Size(600, 400), new Size(300, 200));

        WithStubShell(new[] { widest, tallest }, (shell, _) =>
        {
            Assert.Equal(new Size(800, 400), shell.ClientSize);
            Assert.Equal(new Size(500, 200), shell.MinimumSize);
        });
    }

    // Escape. A Form answers it only through its CancelButton, and this window has no native title
    // bar, so a screen that does not want Escape for something of its own leaves the user with no
    // keyboard way out at all — the close glyph and the mouse, on a window that cannot be closed by
    // the keyboard. The fallback is a zero-sized button on the frame; without it, Escape does nothing
    // on the history screen, and nothing on screen says so.
    [WinFormsFact]
    public void EscapeClosesTheWindow_OnAScreenThatDoesNotWantEscapeForItself()
    {
        var wantsEscape = new StubScreen("Cancels", new Size(600, 400), new Size(300, 200), wantsEscape: true);
        var doesNot = new StubScreen("Does not", new Size(600, 400), new Size(300, 200));

        WithStubShell(new[] { wantsEscape, doesNot }, (shell, _) =>
        {
            // A screen that wants Escape keeps it: the fallback must not take it away.
            Assert.Same(wantsEscape.CancelButton, shell.CancelButton);

            var navIcons = WinFormsHarness.Descendants(WinFormsHarness.Find<Panel>(shell, "Rail"))
                .Where(c => c.Name == "NavIcon").ToList();
            WinFormsHarness.RaiseClick(navIcons[1]);
            WinFormsHarness.Pump();

            Assert.NotNull(shell.CancelButton);
            Assert.NotSame(wantsEscape.CancelButton, shell.CancelButton);

            // And it is not merely non-null: pressing it closes the window, which is what Escape on a
            // borderless card is expected to do. Raised as a click rather than as a keystroke — a real
            // Escape needs a message loop and a focused, shown window, and this suite shows none.
            WinFormsHarness.RaiseClick((Control)shell.CancelButton!);
            WinFormsHarness.Pump();
            Assert.True(shell.IsDisposed, "Escape's fallback button did not close the window.");
        });
    }

    // Enter, the other half of the same two lines — and deliberately NOT the same shape as Escape.
    // A Form has one AcceptButton, so it follows whichever screen is showing; but there is no
    // frame-level fallback behind it, because closing is safe on every screen and no action every
    // screen agrees on exists for Enter. So a screen that supplies none leaves Enter doing nothing,
    // and that absence is as much a decision as the fallback next to it.
    //
    // MEASURED BEFORE IT WAS WRITTEN: deleting the AcceptButton line altogether left the whole suite
    // green, so the settings screen's OK button — which the product really does hand over — was
    // Enter's target only by luck of nobody having changed the line.
    [WinFormsFact]
    public void EnterBelongsToWhicheverScreenWantsIt_AndToNothingWhenTheCurrentScreenDoesNot()
    {
        var wantsEnter = new StubScreen("Accepts", new Size(600, 400), new Size(300, 200), wantsEnter: true);
        var doesNot = new StubScreen("Does not", new Size(600, 400), new Size(300, 200));

        WithStubShell(new[] { wantsEnter, doesNot }, (shell, _) =>
        {
            Assert.Same(wantsEnter.AcceptButton, shell.AcceptButton);

            var navIcons = WinFormsHarness.Descendants(WinFormsHarness.Find<Panel>(shell, "Rail"))
                .Where(c => c.Name == "NavIcon").ToList();
            WinFormsHarness.RaiseClick(navIcons[1]);
            WinFormsHarness.Pump();

            // Not merely "something else": nothing at all. The screen just left keeps its own button,
            // and a frame that went on offering it would fire the previous screen's OK on Enter.
            Assert.Null(shell.AcceptButton);
        });
    }

    /// <summary>
    /// Navigating to a screen tells it so; navigating to the one you are already on does not; and
    /// coming back to a screen you left tells it again. Nothing watched any of the three.
    ///
    /// Every one of them is load-bearing. The history screen re-reads the archive from
    /// <see cref="IScreen.OnShown"/>, because what it draws is a snapshot of a file the tray and the
    /// background sync write while this window is open — a screen never told it has become current
    /// shows a stale list for the rest of the session. Re-clicking the rail icon for the screen you
    /// are ALREADY on used to run the whole switch again, which re-read the archive and threw the
    /// user's selection away for no reason at all. And the guard that fixed that is written on
    /// <c>_started</c> rather than on <c>screen == Current</c> alone, because Current is already the
    /// first screen before the constructor's own switch runs and that one has to run in full or the
    /// window opens showing nothing.
    ///
    /// Counted rather than flagged: "was it told" cannot tell a screen told once from a screen told
    /// three times, and the defect this is about is the second telling.
    /// </summary>
    [WinFormsFact]
    public void AScreenIsToldItHasBecomeCurrent_OnceEachTimeItDoes_AndNotWhenItAlreadyWas()
    {
        var first = new StubScreen("First", new Size(600, 400), new Size(300, 200));
        var second = new StubScreen("Second", new Size(600, 400), new Size(300, 200));

        WithStubShell(new[] { first, second }, (shell, _) =>
        {
            var navIcons = WinFormsHarness.Descendants(WinFormsHarness.Find<Panel>(shell, "Rail"))
                .Where(c => c.Name == "NavIcon").ToList();

            // The constructor's own switch: the screen the window opens on has been shown, the other
            // has not.
            Assert.Equal(1, first.TimesShown);
            Assert.Equal(0, second.TimesShown);

            WinFormsHarness.RaiseClick(navIcons[1]);
            WinFormsHarness.Pump();
            Assert.Equal(1, second.TimesShown);

            // The icon for the screen already showing, clicked again: nothing happens, and in the
            // real history screen "nothing" is what keeps the user's selection.
            WinFormsHarness.RaiseClick(navIcons[1]);
            WinFormsHarness.Pump();
            Assert.Equal(1, second.TimesShown);
            Assert.Equal(1, first.TimesShown);

            // And back: a screen returned to is re-read, because the file behind it may have moved on
            // while the other screen was up.
            WinFormsHarness.RaiseClick(navIcons[0]);
            WinFormsHarness.Pump();
            Assert.Equal(2, first.TimesShown);
        });
    }

    // The icon Windows shows for this window — in the task switcher, in Alt-Tab, and on the taskbar
    // button — and where the window first appears. Neither is visible to any other test here: the
    // suite never shows the window, which is the whole point of the harness, so these are asserted as
    // the properties Windows will read when it eventually is shown.
    [WinFormsFact]
    public void TheWindow_CarriesTheIconItWasGiven_AndOpensCentredOnItsScreen()
    {
        var only = new StubScreen("Only", new Size(600, 400), new Size(300, 200));

        WithStubShell(new[] { only }, (shell, icon) =>
        {
            Assert.Same(icon, shell.Icon);
            // Not Windows' default placement: FormBorderStyle.None gives no title bar to drag the
            // window back by if it opens somewhere unhelpful, and a tray application's window is
            // summoned rather than found.
            Assert.Equal(FormStartPosition.CenterScreen, shell.StartPosition);
        });
    }

    // Neither box, on a window that draws no box to press. FormBorderStyle.None means there is no
    // caption bar and so no maximise or minimise button to disable — what these two still govern is
    // everything the SYSTEM offers without one: the Alt+Space window menu's Maximize and Minimize
    // entries, Win+Up and Win+Down, and double-clicking a taskbar preview. Both are false so that a
    // borderless card cannot be put into a state it has no chrome to get out of — there is no title
    // bar to restore a maximised one by, and the close glyph is the only affordance the window
    // carries. Nothing observed either of them before this test.
    //
    // The premise is asserted with them rather than assumed, because the sentence above is only true
    // while the frame is borderless: with a border back these two would be visible buttons and their
    // being false would be a different decision about a different window.
    [WinFormsFact]
    public void TheWindow_OffersNeitherMaximiseNorMinimise()
    {
        var only = new StubScreen("Only", new Size(600, 400), new Size(300, 200));

        WithStubShell(new[] { only }, (shell, _) =>
        {
            Assert.Equal(FormBorderStyle.None, shell.FormBorderStyle);
            Assert.False(shell.MaximizeBox, "The card can be maximised, and has no title bar to restore itself by.");
            Assert.False(shell.MinimizeBox, "The card can be minimised by the system, and draws nothing that would bring it back.");
        });
    }

    // The frame's own two colours. Every panel, label and glyph on this window picks its colour from
    // Theme itself, so they would all keep theirs through a regression that left the FORM at the OS
    // default — and the result on screen is a light grey window ringing a dark interior, in the strip
    // the rail does not cover and behind every AutoSize label. Nothing here asserted the window's own
    // colours at all.
    //
    // WHAT THIS CANNOT CATCH, and is not meant to: a change to what Theme.Background IS. Both sides
    // of the assertion move together, and pinning the ARGB here would be pinning a design decision in
    // a shell test. What it catches is the form being styled from the wrong token, or — the mutant
    // that matters — not styled at all.
    [WinFormsFact]
    public void TheWindow_WearsTheThemesOwnBackgroundAndText()
    {
        var only = new StubScreen("Only", new Size(600, 400), new Size(300, 200));

        WithStubShell(new[] { only }, (shell, _) =>
        {
            Assert.Equal(Theme.Background, shell.BackColor);
            Assert.Equal(Theme.Text, shell.ForeColor);
        });
    }
}
