namespace KARTCompanion.Shell;

/// <summary>
/// One screen in the shell. The shell owns the frame — rounded border, drag handle, close glyph,
/// rail — and knows nothing about what a screen contains; a screen owns its own layout and knows
/// nothing about the frame. That split is what lets Settings keep its hand-placed pixel coordinates
/// while the history list uses rules that suit a list.
///
/// StatusColor/StatusChanged is the one place that split bends slightly: the rail renders a
/// health dot for whichever screen is current, but has no idea what "health" means for a given
/// screen. A screen reports a color (or null for "no dot") and raises StatusChanged whenever it
/// changes; the shell only ever paints what it's told.
/// </summary>
public interface IScreen
{
    string Title { get; }
    Theme.IconGlyph Glyph { get; }
    Control View { get; }
    Color? StatusColor { get; }
    event EventHandler? StatusChanged;
}
