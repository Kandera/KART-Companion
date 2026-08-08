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

        // Icon rail: a narrow navigation-style column separating the logo/nav glance from the
        // screen's own fields. Height tracks whatever the current screen needs (see
        // SyncFrameToCurrentScreen), same as it tracked the dialog's own final height before.
        _rail = new Panel { Left = 0, Top = 0, Width = RailWidth };
        Theme.StylePanel(_rail, Theme.RailBackground);
        Theme.MakeDragHandle(_rail, this);

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
        _rail.Controls.Add(logoBox);

        // Mirrors whichever screen is current's StatusColor — a health-at-a-glance dot the rail
        // renders without knowing what "health" means for that screen (see IScreen). Position
        // tracks the rail's own height (see SyncFrameToCurrentScreen), same as before.
        _railStatusDot = Theme.CreateStatusDot(Theme.TextDim);
        _railStatusDot.Left = (RailWidth - _railStatusDot.Width) / 2;
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

            var accentBar = new Panel { Left = navIcon.Left - 12, Top = navIcon.Top - 1, Width = 3, Height = 18, BackColor = Theme.Accent };

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
            screen.View.Visible = screen == Current;
            screen.View.SizeChanged += (_, _) => { if (screen == Current) SyncFrameToCurrentScreen(); };
            screen.StatusChanged += (_, _) => { if (screen == Current) UpdateRailStatusDot(); };
            Controls.Add(screen.View);
        }

        _escapeCloseButton.Click += (_, _) => Close();
        Controls.Add(_escapeCloseButton);

        Controls.AddRange(new Control[] { _rail, _railDivider, titleLabel, _subtitleLabel, _headerDivider, _closeGlyph });
        foreach (var chrome in new Control[] { _rail, _railDivider, titleLabel, _subtitleLabel, _headerDivider, _closeGlyph })
            chrome.BringToFront();

        SwitchTo(Current);
        _started = true;
        // ApplyRoundedFormRegion re-subscribes to Resize internally, so this needs to run only
        // once — later ClientSize changes from SwitchTo/SyncFrameToCurrentScreen already trigger
        // Resize, which re-applies the rounded Region on its own.
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
        SyncFrameToCurrentScreen();
        UpdateRailStatusDot();
        screen.OnShown();
    }

    // The shell sizes itself to whatever the current screen needs, rather than assuming a fixed
    // size — the same way the old SettingsForm grew its own ClientSize when a long SavedVariables
    // path wrapped the status label onto more lines. Re-run on every SizeChanged of the current
    // screen's View, not just on navigation, so that still works.
    private void SyncFrameToCurrentScreen()
    {
        ClientSize = Current.View.Size;
        _rail.Height = ClientSize.Height;
        _railDivider.Height = ClientSize.Height;
        _headerDivider.Width = Current.View.Width - ContentLeft - 12;
        _closeGlyph.Left = Current.View.Width - _closeGlyph.Width - 4;
        _railStatusDot.Top = _rail.Height - 30;
    }

    // Renders whatever the current screen reports — null means "no dot for this screen".
    private void UpdateRailStatusDot()
    {
        var color = Current.StatusColor;
        _railStatusDot.Visible = color.HasValue;
        if (color.HasValue) Theme.SetStatusDotColor(_railStatusDot, color.Value);
    }
}
