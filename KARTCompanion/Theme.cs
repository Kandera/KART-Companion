using System.Drawing.Drawing2D;

namespace KARTCompanion;

/// <summary>Colors lifted from KAimg.jpg (the addon's own icon: dark navy circle, cyan ring,
/// white "K"), so the companion app's dialog matches the addon's branding instead of looking
/// like a generic WinForms tool.</summary>
public static class Theme
{
    public static readonly Color Background = Color.FromArgb(13, 20, 28);
    public static readonly Color Panel = Color.FromArgb(20, 30, 40);
    public static readonly Color Accent = Color.FromArgb(34, 224, 255);
    public static readonly Color AccentDim = Color.FromArgb(20, 120, 138);
    public static readonly Color Text = Color.FromArgb(235, 240, 244);
    public static readonly Color TextDim = Color.FromArgb(150, 165, 175);
    public static readonly Color Error = Color.FromArgb(255, 90, 90);
    public static readonly Color Success = Color.FromArgb(110, 230, 150);

    /// <summary>Darker than Background — used for the icon rail so it reads as a distinct
    /// navigation column rather than a flat continuation of the content area.</summary>
    public static readonly Color RailBackground = Color.FromArgb(10, 16, 23);

    /// <summary>Faint translucent-white line color for field borders and panel dividers — gives
    /// flat-filled shapes a visible edge without a harsh solid outline.</summary>
    public static readonly Color BorderStrong = Color.FromArgb(28, 235, 240, 244);

    private const int CornerRadius = 9;
    private const int FormCornerRadius = 14;

    // WinForms controls don't support a corner-radius property. An earlier version of this file
    // clipped controls with a rounded-rect Region and painted a separately anti-aliased border on
    // top — but Region clipping is a hard, non-anti-aliased pixel mask, so its curved edge never
    // quite lines up with the smooth border stroke drawn just inside it, leaving a jagged,
    // "bitten into" notch at every corner. Every rounded shape in this file is now drawn purely
    // with anti-aliased fills/strokes instead (see RoundedButton, CreateInputRow, ToggleSwitch):
    // the control's BackColor is set to whatever's actually behind it (its parent's fill color),
    // and only the rounded interior is filled on top — no Region involved, so corners blend
    // seamlessly instead of being clipped. The one exception is the top-level Form itself
    // (ApplyRoundedFormRegion): there's no "parent color" to blend against there, since whatever
    // is behind the window is the arbitrary desktop, so Region is unavoidable for that one case.
    private static GraphicsPath BuildRoundedPath(int width, int height, int radius = CornerRadius)
    {
        var d = radius * 2;
        var rect = new Rectangle(0, 0, width, height);
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void StyleForm(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = Text;
    }

    // A borderless Form has no OS-drawn edge, so its true rectangular corners would otherwise
    // show as hard right angles against the desktop — this rounds the whole window the same way
    // ApplyRoundedRegion rounds individual controls, just at a larger radius that reads as a
    // floating card rather than a form field.
    public static void ApplyRoundedFormRegion(Form form)
    {
        void Apply()
        {
            if (form.Width <= 0 || form.Height <= 0) return;
            using var path = BuildRoundedPath(form.Width, form.Height, FormCornerRadius);
            form.Region?.Dispose();
            form.Region = new Region(path);
        }

        Apply();
        form.Resize += (_, _) => Apply();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HT_CAPTION = 0x2;

    // FormBorderStyle.None removes the native title bar we'd normally drag by, so any control
    // that's just inert background (the rail, the title text) is wired to act like one via the
    // classic "tell Windows this mouse-down is on the caption" trick.
    public static void MakeDragHandle(Control control, Form form) =>
        control.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(form.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        };

    // Small "x" glyph button standing in for the native close button that FormBorderStyle.None
    // removes. Deliberately not routed through StyleButton — it has no fill/border of its own,
    // just a hover tint, so it reads as chrome rather than a third action button next to OK/Cancel.
    public static Control CreateCloseGlyph(Action onClick)
    {
        var host = new Panel { Width = 24, Height = 24, BackColor = Color.Transparent, Cursor = Cursors.Hand };
        var hovered = false;
        host.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (hovered)
            {
                using var hoverBrush = new SolidBrush(Color.FromArgb(18, Text));
                e.Graphics.FillEllipse(hoverBrush, 1, 1, 22, 22);
            }
            using var pen = new Pen(hovered ? Text : TextDim, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.DrawLine(pen, 8, 8, 16, 16);
            e.Graphics.DrawLine(pen, 16, 8, 8, 16);
        };
        host.MouseEnter += (_, _) => { hovered = true; host.Invalidate(); };
        host.MouseLeave += (_, _) => { hovered = false; host.Invalidate(); };
        host.Click += (_, _) => onClick();
        return host;
    }

    public static void StyleLabel(Label label, bool dim = false)
    {
        label.BackColor = Color.Transparent;
        label.ForeColor = dim ? TextDim : Text;
    }

    // Primary: solid bright-cyan fill with dark text — the "call to action" look. Secondary:
    // dark fill with a cyan outline — everything else (Cancel, Browse, ...). Fully custom-painted
    // (UserPaint, base painting skipped) rather than a styled native Button: a native Button's
    // OnPaint always draws its rectangular background and text as one inseparable step, so there
    // was no way to layer a rounded fill in without either covering the text or falling back to
    // Region clipping's jagged corners (see BuildRoundedPath's comment).
    public sealed class RoundedButton : Button
    {
        public bool Primary { get; set; }

        // The color immediately behind this button — its parent's fill color, painted first so
        // the corners outside the rounded shape blend into whatever they're sitting on (the Form
        // background for footer buttons, the input row's panel color for the nested Browse
        // button).
        public Color SurfaceColor { get; set; } = Background;

        private bool _hovered;

        public RoundedButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            Height = 34;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hovered = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 1 || Height <= 1) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(SurfaceColor);

            var fill = Primary
                ? (_hovered ? Color.FromArgb(90, 235, 255) : Accent)
                : (_hovered ? Color.FromArgb(30, 45, 58) : Panel);
            using (var path = BuildRoundedPath(Width, Height))
            using (var brush = new SolidBrush(fill))
                e.Graphics.FillPath(brush, path);

            using (var pen = new Pen(Accent, 1))
            using (var strokePath = BuildRoundedPath(Width - 1, Height - 1))
                e.Graphics.DrawPath(pen, strokePath);

            var foreColor = Primary ? Background : Theme.Text;
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, foreColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    public static RoundedButton CreateButton(string text, bool primary = false, Color? surfaceColor = null) =>
        new() { Text = text, Primary = primary, SurfaceColor = surfaceColor ?? Background };

    public static void StylePanel(Panel panel, Color backColor) => panel.BackColor = backColor;

    // A pill-shaped on/off switch — WinForms has no built-in toggle control, so this owner-draws
    // one (rounded track + circular knob) matching the mockup instead of falling back to a
    // CheckBox, which would look like a completely different, older control style next to the
    // rounded fields around it.
    public sealed class ToggleSwitch : Panel
    {
        private bool _isOn;
        public bool IsOn
        {
            get => _isOn;
            set { if (_isOn == value) return; _isOn = value; Invalidate(); IsOnChanged?.Invoke(this, EventArgs.Empty); }
        }
        public event EventHandler? IsOnChanged;

        public ToggleSwitch()
        {
            Width = 36;
            Height = 20;
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var trackBrush = new SolidBrush(_isOn ? Accent : AccentDim);
            using var trackPath = BuildRoundedPath(Width, Height, Height / 2);
            e.Graphics.FillPath(trackBrush, trackPath);

            var knobDiameter = Height - 4;
            var knobLeft = _isOn ? Width - knobDiameter - 2 : 2;
            using var knobBrush = new SolidBrush(_isOn ? Background : Theme.Text);
            e.Graphics.FillEllipse(knobBrush, knobLeft, 2, knobDiameter, knobDiameter);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            IsOn = !IsOn;
        }
    }

    public static ToggleSwitch CreateToggleSwitch(bool initial) => new() { IsOn = initial };

    public enum IconGlyph { Key, Folder, Sliders, Clock }

    // Minimal monoline glyphs (1.6px stroke, rounded caps) drawn directly with GraphicsPath —
    // there's no icon font/SVG pipeline in this WinForms app, so these stand in for the field
    // icons from the approved design mockup without pulling in an image asset per glyph.
    public static Control CreateIcon(IconGlyph glyph, Color color, int size = 15)
    {
        var icon = new Panel { Width = size, Height = size, BackColor = Color.Transparent };
        icon.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            float s = size;
            switch (glyph)
            {
                case IconGlyph.Key:
                    e.Graphics.DrawEllipse(pen, s * 0.06f, s * 0.42f, s * 0.42f, s * 0.42f);
                    e.Graphics.DrawLine(pen, s * 0.44f, s * 0.56f, s * 0.94f, s * 0.06f);
                    e.Graphics.DrawLine(pen, s * 0.68f, s * 0.32f, s * 0.86f, s * 0.5f);
                    break;
                case IconGlyph.Folder:
                    using (var path = new GraphicsPath())
                    {
                        path.AddLine(s * 0.08f, s * 0.26f, s * 0.4f, s * 0.26f);
                        path.AddLine(s * 0.4f, s * 0.26f, s * 0.48f, s * 0.4f);
                        path.AddLine(s * 0.48f, s * 0.4f, s * 0.92f, s * 0.4f);
                        path.AddLine(s * 0.92f, s * 0.4f, s * 0.92f, s * 0.86f);
                        path.AddLine(s * 0.92f, s * 0.86f, s * 0.08f, s * 0.86f);
                        path.CloseFigure();
                        e.Graphics.DrawPath(pen, path);
                    }
                    break;
                case IconGlyph.Sliders:
                    e.Graphics.DrawLine(pen, s * 0.1f, s * 0.32f, s * 0.9f, s * 0.32f);
                    e.Graphics.DrawLine(pen, s * 0.1f, s * 0.7f, s * 0.9f, s * 0.7f);
                    using (var knob1 = new SolidBrush(color)) e.Graphics.FillEllipse(knob1, s * 0.56f, s * 0.24f, s * 0.16f, s * 0.16f);
                    using (var knob2 = new SolidBrush(color)) e.Graphics.FillEllipse(knob2, s * 0.28f, s * 0.62f, s * 0.16f, s * 0.16f);
                    break;
                case IconGlyph.Clock:
                    e.Graphics.DrawEllipse(pen, s * 0.08f, s * 0.08f, s * 0.84f, s * 0.84f);
                    e.Graphics.DrawLine(pen, s * 0.5f, s * 0.5f, s * 0.5f, s * 0.26f);
                    e.Graphics.DrawLine(pen, s * 0.5f, s * 0.5f, s * 0.68f, s * 0.6f);
                    break;
            }
        };
        return icon;
    }

    // Rounded, icon-prefixed field — wraps an icon + borderless TextBox so the icon sits inside
    // the field like the mockup instead of floating separately beside it. BackColor is left
    // Transparent (Panel, unlike Button, supports this reliably) so the true parent background
    // shows through the corners outside the rounded fill — no Region, so no jagged edge.
    public static Panel CreateInputRow(int width, int height, IconGlyph glyph, out TextBox textBox, bool passwordChar = false, int rightPadding = 0)
    {
        var row = new Panel { Width = width, Height = height, BackColor = Color.Transparent };
        row.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var fillPath = BuildRoundedPath(width, height);
            using var fillBrush = new SolidBrush(Panel);
            e.Graphics.FillPath(fillBrush, fillPath);
            using var strokePath = BuildRoundedPath(width - 1, height - 1);
            using var pen = new Pen(BorderStrong, 1);
            e.Graphics.DrawPath(pen, strokePath);
        };

        var icon = CreateIcon(glyph, TextDim);
        icon.Left = 10;
        icon.Top = (height - icon.Height) / 2;

        textBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Panel,
            ForeColor = Text,
            Left = icon.Right + 8,
            Width = width - icon.Right - 8 - rightPadding - 8,
            UseSystemPasswordChar = passwordChar,
        };
        textBox.Top = (height - textBox.Height) / 2;

        row.Controls.Add(icon);
        row.Controls.Add(textBox);
        return row;
    }

    // A small filled circle with a soft halo ring, used on the settings dialog's icon rail to
    // show sync health at a glance (green = SavedVariables path resolved, dim = not yet
    // configured, red = last sync failed) without adding a second status label.
    public static Panel CreateStatusDot(Color initial)
    {
        var dot = new Panel { Width = 10, Height = 10, BackColor = Color.Transparent, Tag = initial };
        dot.Paint += (_, e) =>
        {
            var color = (Color)dot.Tag!;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var halo = new SolidBrush(Color.FromArgb(45, color));
            e.Graphics.FillEllipse(halo, 0, 0, 10, 10);
            using var core = new SolidBrush(color);
            e.Graphics.FillEllipse(core, 2, 2, 6, 6);
        };
        return dot;
    }

    public static void SetStatusDotColor(Panel dot, Color color)
    {
        dot.Tag = color;
        dot.Invalidate();
    }
}
