namespace KARTCompanion.Shell;

/// <summary>
/// One screen in the shell. The shell owns the frame — rounded border, drag handle, close glyph,
/// rail — and knows nothing about what a screen contains; a screen owns its own layout and knows
/// nothing about the frame. That split is what lets Settings keep its hand-placed pixel coordinates
/// while the history list uses rules that suit a list.
/// </summary>
public interface IScreen
{
    string Title { get; }
    Theme.IconGlyph Glyph { get; }
    Control View { get; }
}
