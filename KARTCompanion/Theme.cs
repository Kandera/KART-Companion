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

    private const int CornerRadius = 6;

    // WinForms controls don't support a corner-radius property — clipping the control to a
    // rounded-rect Region is the standard workaround. The Region is sized to the control's
    // bounds at the point it's created, so it must be re-applied on Resize or it'll be stale
    // (e.g. wrong size) after any layout pass that changes the control's Width/Height.
    private static void ApplyRoundedRegion(Control control)
    {
        void Apply()
        {
            if (control.Width <= 0 || control.Height <= 0) return;

            var d = CornerRadius * 2;
            var rect = new Rectangle(0, 0, control.Width, control.Height);
            using var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            control.Region?.Dispose();
            control.Region = new Region(path);
        }

        Apply();
        control.Resize += (_, _) => Apply();
    }

    public static void StyleForm(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = Text;
    }

    public static void StyleLabel(Label label, bool dim = false)
    {
        label.BackColor = Color.Transparent;
        label.ForeColor = dim ? TextDim : Text;
    }

    public static void StyleTextBox(TextBox box)
    {
        box.BackColor = Panel;
        box.ForeColor = Text;
        box.BorderStyle = BorderStyle.FixedSingle;
        ApplyRoundedRegion(box);
    }

    public static void StyleNumericUpDown(NumericUpDown box)
    {
        box.BackColor = Panel;
        box.ForeColor = Text;
        box.BorderStyle = BorderStyle.FixedSingle;
        ApplyRoundedRegion(box);
    }

    // Primary: solid bright-cyan fill with dark text — the "call to action" look. Secondary:
    // dark fill with a cyan outline — everything else (Cancel, Browse, ...).
    public static void StyleButton(Button button, bool primary = false)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Accent;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = primary ? Accent : Panel;
        button.ForeColor = primary ? Background : Text;
        button.FlatAppearance.MouseOverBackColor = primary
            ? Color.FromArgb(90, 235, 255)
            : Color.FromArgb(30, 45, 58);
        button.Cursor = Cursors.Hand;
        ApplyRoundedRegion(button);
    }
}
