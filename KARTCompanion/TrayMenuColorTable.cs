namespace KARTCompanion;

/// <summary>
/// Color table for the tray context menu's ToolStripProfessionalRenderer, mapped onto Theme.cs
/// so the right-click menu matches the app's navy/cyan branding instead of the default Windows
/// system menu style.
/// </summary>
public sealed class TrayMenuColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Theme.Panel;
    public override Color ImageMarginGradientBegin => Theme.Panel;
    public override Color ImageMarginGradientMiddle => Theme.Panel;
    public override Color ImageMarginGradientEnd => Theme.Panel;
    public override Color MenuBorder => Theme.AccentDim;
    public override Color MenuItemBorder => Theme.Accent;
    public override Color MenuItemSelected => Theme.AccentDim;
    public override Color MenuItemSelectedGradientBegin => Theme.AccentDim;
    public override Color MenuItemSelectedGradientEnd => Theme.AccentDim;
    public override Color MenuItemPressedGradientBegin => Theme.Accent;
    public override Color MenuItemPressedGradientEnd => Theme.Accent;
    public override Color SeparatorDark => Theme.AccentDim;
    public override Color SeparatorLight => Theme.AccentDim;
}
