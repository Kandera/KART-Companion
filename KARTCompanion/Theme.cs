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
    }

    public static void StyleNumericUpDown(NumericUpDown box)
    {
        box.BackColor = Panel;
        box.ForeColor = Text;
        box.BorderStyle = BorderStyle.FixedSingle;
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
    }
}
