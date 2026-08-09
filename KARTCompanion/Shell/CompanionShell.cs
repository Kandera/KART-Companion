namespace KARTCompanion.Shell;

/// <summary>
/// Hosts every screen behind one borderless, rounded window: FormBorderStyle.None, the drag
/// handle, the icon rail, and the close glyph are frame chrome that live here once, shared by
/// whichever screen is current — a screen owns only its own content (see IScreen).
///
/// The rail used to carry exactly one nav glyph, with a comment explaining it was deliberately
/// not a second, unclickable item, "because that would imply a multi-page rail that doesn't
/// exist." This class is what makes that rail real: one clickable glyph per screen, with an
/// accent bar marking whichever one is current.
/// </summary>
public sealed class CompanionShell : Form
{
    private const int RailWidth = 64;
    private const int ContentLeft = RailWidth + 16;
    private const int RightMargin = 12;

    private readonly IReadOnlyList<IScreen> _screens;
    private readonly Panel _rail;
    private readonly Panel _railDivider;
    private readonly Control _closeGlyph;
    private readonly Label _subtitleLabel;
    private readonly Panel _headerDivider;
    private readonly Panel _railStatusDot;
    // Zero-sized and never shown: a Form only answers Escape through a CancelButton, and this window
    // has no native title bar and so no other keyboard way out. Screens that want Escape for
    // something of their own (Settings' Cancel) supply their own and this is not used.
    private readonly Button _escapeCloseButton = new() { Size = Size.Empty, TabStop = false };
    private readonly List<(IScreen Screen, Control Icon, Panel AccentBar)> _navItems = new();
    // False only during the constructor's own first SwitchTo(Current) call, which must run in full
    // (it is what shows the very first screen at all). Once true, SwitchTo can tell "navigating to a
    // different screen" apart from "already on this one" — see SwitchTo's own remarks.
    private bool _started;

    public IScreen Current { get; private set; }

    public CompanionShell(IReadOnlyList<IScreen> screens, Bitmap logo, Icon icon)
    {
        if (screens.Count == 0) throw new ArgumentException("A shell needs at least one screen.", nameof(screens));
        _screens = screens;
        Current = screens[0];

        Icon = icon;
        // No native title bar: the approved mockup is a borderless, rounded floating card with
        // the logo/title drawn inside the body, not a light OS title bar sitting on top of a
        // dark client area. FormBorderStyle.None removes that bar (and, with it, the window's
        // only means of being dragged or closed by mouse — both are rebuilt below).
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Theme.StyleForm(this);

        // The frame opens at whatever the largest screen laid itself out at, and never shrinks below
        // the largest minimum any of them reports (see IScreen.MinimumViewSize). Both are asked of
        // the screens rather than kept as a constant here: a screen owns its own layout, and a
        // constant that has to agree with those layouts is one that silently stops agreeing. This is
        // still the shell deciding one size for every screen, which is what it was doing before —
        // each screen used to dictate ClientSize through its own View.Size, and Settings' 488px and
        // the history screen's 1042px disagreed, so the window changed width on every switch.
        //
        // A size now, not a permanent assertion: the user resizes this window (see WndProc), and
        // nothing re-asserts either number afterwards. FormBorderStyle.None means Size and ClientSize
        // are the same rectangle, so MinimumSize — which is about the outer size — can be set
        // straight from what the screens' views need.
        ClientSize = ShellFrame.LargestOf(screens.Select(s => s.View.Size));
        MinimumSize = ShellFrame.LargestOf(screens.Select(s => s.MinimumViewSize));

        // Icon rail: a narrow navigation-style column separating the logo/nav glance from the
        // screen's own fields. Height tracks the window's own (see LayoutChrome), so it follows a
        // resize.
        _rail = new Panel { Left = 0, Top = 0, Width = RailWidth };
        Theme.StylePanel(_rail, Theme.RailBackground);
        Theme.MakeDragHandle(_rail, this);
        // The rail covers the whole left edge, so without this the left edge could not be grabbed.
        FrameEdgePassThrough.Attach(_rail, this);

        _railDivider = new Panel { Left = RailWidth, Top = 0, Width = 1, BackColor = Theme.BorderStrong };

        _closeGlyph = Theme.CreateCloseGlyph(Close);
        _closeGlyph.Top = 12;

        var logoBox = new PictureBox
        {
            Image = logo,
            SizeMode = PictureBoxSizeMode.Zoom,
            Left = (RailWidth - 34) / 2,
            Top = 20,
            Width = 34,
            Height = 34,
        };
        // Draggable, like the rail behind it. A child control eats the mouse before its parent ever
        // sees it, so making only the rail a drag handle left the window movable in the gaps BETWEEN
        // its children and nowhere else — you had to find the bare background to move the window.
        // Every inert thing sitting on the rail gets the handle; only the nav icons, which have a
        // click of their own, deliberately do not.
        Theme.MakeDragHandle(logoBox, this);
        _rail.Controls.Add(logoBox);

        // Mirrors whichever screen is current's StatusColor — a health-at-a-glance dot the rail
        // renders without knowing what "health" means for that screen (see IScreen). Position
        // tracks the rail's own height (see LayoutChrome), same as before.
        _railStatusDot = Theme.CreateStatusDot(Theme.TextDim);
        _railStatusDot.Left = (RailWidth - _railStatusDot.Width) / 2;
        Theme.MakeDragHandle(_railStatusDot, this);
        _rail.Controls.Add(_railStatusDot);

        // AutoSize (not a fixed Width spanning the whole content column) so the label's hit-test
        // area hugs the short "KART Companion" text instead of silently overlapping the close
        // glyph's hitbox further right, which would swallow its clicks.
        var titleLabel = new Label
        {
            Text = "KART Companion",
            Left = ContentLeft,
            Top = 20,
            AutoSize = true,
            Font = new Font(Font.FontFamily, 14, FontStyle.Bold),
        };
        Theme.StyleLabel(titleLabel);
        Theme.MakeDragHandle(titleLabel, this);

        // Subtitle mirrors whichever screen is current (e.g. "Settings") instead of a fixed
        // string, since the shell itself is generic across screens.
        _subtitleLabel = new Label { Left = ContentLeft, Top = 47, AutoSize = true };
        Theme.StyleLabel(_subtitleLabel, dim: true);
        Theme.MakeDragHandle(_subtitleLabel, this);

        _headerDivider = new Panel { Left = ContentLeft, Top = 78, Height = 1, BackColor = Theme.AccentDim };

        var navTop = 72;
        foreach (var screen in screens)
        {
            var navIcon = Theme.CreateIcon(screen.Glyph, Theme.Text, 16);
            navIcon.Left = (RailWidth - navIcon.Width) / 2;
            navIcon.Top = navTop;
            navIcon.Cursor = Cursors.Hand;
            navIcon.Click += (_, _) => SwitchTo(screen);

            // The bar marking the current screen has no click of its own, so it is a drag handle too
            // (see the logo above). Without it, the strip beside every nav icon was a dead spot the
            // window could not be moved by.
            var accentBar = new Panel { Left = navIcon.Left - 12, Top = navIcon.Top - 1, Width = 3, Height = 18, BackColor = Theme.Accent };
            Theme.MakeDragHandle(accentBar, this);

            _rail.Controls.Add(accentBar);
            _rail.Controls.Add(navIcon);
            _navItems.Add((screen, navIcon, accentBar));
            navTop += 40;

            // Every screen's View lives at the form's own origin — the content column's
            // coordinates (ContentLeft and up) are already baked into each screen's own child
            // controls, so placing the View here means none of those numbers has to change. The
            // rail and header sit in front of it (see the BringToFront calls below) and cover
            // the strip a screen's View leaves blank on its left.
            screen.View.Location = Point.Empty;
            // Size first, anchors second, and the order is the whole point. WinForms anchoring keeps
            // whatever gap a control had to its parent's edges at the moment the parent changes size,
            // so a screen laid out at one size and then jumped to another before its anchors were set
            // would have every anchored control displaced by the difference. The screens have already
            // laid themselves out at their own view size, ClientSize above is the largest of exactly
            // those sizes, and this assignment therefore either changes nothing or gives a smaller
            // screen slack it has not anchored anything against yet.
            screen.View.Size = ClientSize;
            screen.View.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            // A screen's View covers the entire client area, so it would otherwise swallow every
            // hit-test the frame's edges need (see FrameEdgePassThrough).
            FrameEdgePassThrough.Attach(screen.View, this);
            screen.View.Visible = screen == Current;
            screen.StatusChanged += (_, _) => { if (screen == Current) UpdateRailStatusDot(); };
            Controls.Add(screen.View);
        }

        _escapeCloseButton.Click += (_, _) => Close();
        Controls.Add(_escapeCloseButton);

        Controls.AddRange(new Control[] { _rail, _railDivider, titleLabel, _subtitleLabel, _headerDivider, _closeGlyph });
        foreach (var chrome in new Control[] { _rail, _railDivider, titleLabel, _subtitleLabel, _headerDivider, _closeGlyph })
            chrome.BringToFront();

        // The chrome is laid out against the client size, so it has to be re-laid every time the user
        // drags an edge. The screens themselves need nothing here — their views are anchored to all
        // four edges above, and each screen's own controls are anchored inside them.
        Resize += (_, _) => LayoutChrome();

        SwitchTo(Current);
        _started = true;
        // ApplyRoundedFormRegion re-subscribes to Resize internally, so this needs to run only
        // once — every later size change, including the user dragging an edge, raises Resize and
        // re-applies the rounded Region on its own.
        Theme.ApplyRoundedFormRegion(this);
    }

    /// <summary>Shows the shell (or brings it forward if it's already visible) on the given
    /// screen.</summary>
    public void Show(int screenIndex)
    {
        SwitchTo(_screens[screenIndex]);
        Show();
        Activate();
    }

    private void SwitchTo(IScreen screen)
    {
        // Re-clicking the rail icon for whichever screen is already current used to re-run this in
        // full, including OnShown() — harmless for Settings (its OnShown does nothing), but History's
        // OnShown re-reads the archive and resets the list, silently dropping the user's selection for
        // no reason (nothing about the screen actually changed). Guarded on _started, not just on
        // screen == Current: Current is already set to screens[0] before the constructor's own first
        // call here, and that first call is the one that has to run in full.
        if (_started && screen == Current) return;

        foreach (var (s, _, accentBar) in _navItems) accentBar.Visible = s == screen;
        foreach (var s in _screens) s.View.Visible = s == screen;
        Current = screen;
        Text = $"KART Companion — {screen.Title}";
        _subtitleLabel.Text = screen.Title;
        // A Form has one AcceptButton and one CancelButton, so they follow whichever screen is
        // showing rather than being claimed once by whichever screen happened to be constructed
        // first — see IScreen. Escape falls back to closing the window, which is what the close
        // glyph does and what a borderless card is expected to do; Enter has no such default,
        // because there is no action every screen agrees is the safe one.
        AcceptButton = screen.AcceptButton;
        CancelButton = screen.CancelButton ?? _escapeCloseButton;
        LayoutChrome();
        UpdateRailStatusDot();
        screen.OnShown();
    }

    // Rail and divider heights, the header divider's width and the close glyph's position, all
    // against the CURRENT client size. This used to re-assert ClientSize from a constant on every
    // screen switch, which would have undone the user's resize the moment they clicked the other
    // rail icon; the size is now the user's to set and nothing here touches it.
    private void LayoutChrome()
    {
        _rail.Height = ClientSize.Height;
        _railDivider.Height = ClientSize.Height;
        _headerDivider.Width = ClientSize.Width - ContentLeft - RightMargin;
        _closeGlyph.Left = ClientSize.Width - _closeGlyph.Width - 4;
        _railStatusDot.Top = _rail.Height - 30;
    }

    // Renders whatever the current screen reports — null means "no dot for this screen".
    private void UpdateRailStatusDot()
    {
        var color = Current.StatusColor;
        _railStatusDot.Visible = color.HasValue;
        if (color.HasValue) Theme.SetStatusDotColor(_railStatusDot, color.Value);
    }

    private const int WM_NCHITTEST = 0x84;

    /// <summary>
    /// FormBorderStyle.None leaves no resize border for Windows to hit-test, the same way it leaves
    /// no title bar to drag by (see Theme.MakeDragHandle). This is the other half of that: the outer
    /// few pixels of the client area answer as the frame's edges and corners, and Windows' own sizing
    /// loop does the rest — no bespoke drag arithmetic, and the resize cursors come with it.
    ///
    /// Only the ring answers; everything inside it falls through to the base behaviour, so every
    /// control, drag handle and click on the window is untouched.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST)
        {
            var edge = ShellFrame.ResizeEdgeAt(PointToClient(ShellFrame.PointFromLParam(m.LParam)), ClientSize);
            if (edge != ShellFrame.FrameEdge.None)
            {
                m.Result = (IntPtr)ShellFrame.HitTestCode(edge);
                return;
            }
        }

        base.WndProc(ref m);
    }

    /// <summary>
    /// Lets the frame's hit-test reach the form through a control that covers its edge.
    ///
    /// A child control eats the mouse before its parent ever sees it — the fact this class already
    /// documents for the drag handles, and the reason every inert thing on the rail needs one of its
    /// own. It applies to the resize ring just as much: each screen's View covers the entire client
    /// area and the rail covers the left edge, so the form's own WM_NCHITTEST would never once be
    /// asked about the outer six pixels. Answering HTTRANSPARENT there is the documented way to say
    /// "not mine" — Windows keeps looking at the windows underneath, in the same thread, until one
    /// answers something else, which here is the form.
    ///
    /// A NativeWindow rather than a Panel subclass because a screen builds its own View (see IScreen:
    /// a screen knows nothing about the frame), so the frame has to add this to controls it did not
    /// create.
    /// </summary>
    private sealed class FrameEdgePassThrough : NativeWindow
    {
        private const int HTTRANSPARENT = -1;

        private readonly Form _form;

        private FrameEdgePassThrough(Form form) => _form = form;

        public static void Attach(Control control, Form form)
        {
            var passThrough = new FrameEdgePassThrough(form);
            if (control.IsHandleCreated) passThrough.AssignHandle(control.Handle);
            // A WinForms control can destroy and recreate its handle at any time (a BackColor change
            // is enough for some of them), which would leave this subclassing a handle that no longer
            // exists.
            control.HandleCreated += (_, _) => passThrough.AssignHandle(control.Handle);
            control.HandleDestroyed += (_, _) => passThrough.ReleaseHandle();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST
                && ShellFrame.ResizeEdgeAt(_form.PointToClient(ShellFrame.PointFromLParam(m.LParam)), _form.ClientSize)
                    != ShellFrame.FrameEdge.None)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }

            base.WndProc(ref m);
        }
    }
}

/// <summary>
/// The frame's geometry decisions, kept out of CompanionShell so they can be tested without
/// constructing a Form — see HistoryExportPlanner's own remarks on why nothing else there has
/// automated coverage.
/// </summary>
public static class ShellFrame
{
    /// <summary>How far in from an edge counts as grabbing it. Narrow on purpose: the ring is taken
    /// away from whatever sits underneath it — the rail's drag area, a screen's own content — so it
    /// is kept to about the thickness of the native sizing border it stands in for.</summary>
    public const int GripMargin = 6;

    /// <summary>How far ALONG an edge still counts as its corner. Corners reach further than the
    /// edge is deep for the obvious reason: where two 6px margins overlap is a 6x6 target, which is
    /// not a thing a mouse can be expected to find.</summary>
    public const int CornerMargin = 16;

    public enum FrameEdge { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

    /// <summary>Which edge or corner of the frame a client-area point belongs to, or None for
    /// everything the frame does not claim — which is nearly all of the window, and is what leaves
    /// the drag handles and every control alone.</summary>
    public static FrameEdge ResizeEdgeAt(Point point, Size clientSize) =>
        ResizeEdgeAt(point, clientSize, GripMargin, CornerMargin);

    /// <param name="gripMargin">How deep the ring is.</param>
    /// <param name="cornerMargin">How far a corner reaches along each edge.</param>
    public static FrameEdge ResizeEdgeAt(Point point, Size clientSize, int gripMargin, int cornerMargin)
    {
        // Outside the client area is not the frame's business. PointToClient can hand us negatives
        // (a point over the window's own non-client area, or simply a stale position), and without
        // this an x of -3 would still read as "within 6 of the left edge" and claim a resize for a
        // point that is not even on the window.
        if (point.X < 0 || point.Y < 0 || point.X >= clientSize.Width || point.Y >= clientSize.Height)
            return FrameEdge.None;

        var left = point.X < gripMargin;
        var right = point.X >= clientSize.Width - gripMargin;
        var top = point.Y < gripMargin;
        var bottom = point.Y >= clientSize.Height - gripMargin;

        var nearLeft = point.X < cornerMargin;
        var nearRight = point.X >= clientSize.Width - cornerMargin;
        var nearTop = point.Y < cornerMargin;
        var nearBottom = point.Y >= clientSize.Height - cornerMargin;

        // A corner is claimed from both of its edges, so the grabbable shape is an L in each corner
        // rather than the tiny square where the two margins happen to overlap.
        if ((top && nearLeft) || (left && nearTop)) return FrameEdge.TopLeft;
        if ((top && nearRight) || (right && nearTop)) return FrameEdge.TopRight;
        if ((bottom && nearLeft) || (left && nearBottom)) return FrameEdge.BottomLeft;
        if ((bottom && nearRight) || (right && nearBottom)) return FrameEdge.BottomRight;

        if (left) return FrameEdge.Left;
        if (right) return FrameEdge.Right;
        if (top) return FrameEdge.Top;
        if (bottom) return FrameEdge.Bottom;
        return FrameEdge.None;
    }

    /// <summary>The WM_NCHITTEST answer for an edge. These numbers are Windows', not ours — HTLEFT
    /// is 10 and the rest follow it — so they are pinned by test rather than trusted to a rename.</summary>
    public static int HitTestCode(FrameEdge edge) => edge switch
    {
        FrameEdge.Left => 10,
        FrameEdge.Right => 11,
        FrameEdge.Top => 12,
        FrameEdge.TopLeft => 13,
        FrameEdge.TopRight => 14,
        FrameEdge.Bottom => 15,
        FrameEdge.BottomLeft => 16,
        FrameEdge.BottomRight => 17,
        _ => 1, // HTCLIENT
    };

    /// <summary>The screen point packed into a WM_NCHITTEST lParam. Both halves are SIGNED 16-bit:
    /// a monitor arranged to the left of the primary one has negative x, and reading it unsigned
    /// would put the cursor 65,000 pixels to the right instead.</summary>
    public static Point PointFromLParam(IntPtr lParam)
    {
        var packed = lParam.ToInt64();
        return new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF));
    }

    /// <summary>The smallest size that contains every one of these — each dimension taken
    /// independently, so a wide screen and a tall one together give a frame that fits both.</summary>
    public static Size LargestOf(IEnumerable<Size> sizes)
    {
        var largest = Size.Empty;
        foreach (var size in sizes)
            largest = new Size(Math.Max(largest.Width, size.Width), Math.Max(largest.Height, size.Height));
        return largest;
    }
}
