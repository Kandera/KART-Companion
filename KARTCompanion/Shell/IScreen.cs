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

    /// <summary>
    /// The smallest this screen's layout still survives at. The window is resizable, and the shell
    /// takes the largest of its screens' minimums as the window's own MinimumSize — it cannot work
    /// that out itself without knowing what a screen contains, which is exactly what this interface
    /// keeps it from knowing.
    ///
    /// The default is "no smaller than the size I laid myself out at", which is always safe and
    /// simply means such a screen can only grow.
    /// </summary>
    Size MinimumViewSize => View.Size;

    /// <summary>
    /// What Enter and Escape should do while this screen is showing, or null for "nothing".
    ///
    /// A Form has exactly one of each, so they belong to whichever screen is current — the shell
    /// sets them on every switch (see CompanionShell.SwitchTo). The settings screen used to set
    /// them on the host form itself, once, which left Enter bound to its OK button on every other
    /// screen: pressing Enter on the history list saved and closed the settings screen, and Escape
    /// did the same via Cancel, from a screen showing neither button.
    /// </summary>
    IButtonControl? AcceptButton => null;
    IButtonControl? CancelButton => null;

    /// <summary>
    /// Called by the shell whenever this screen becomes the current one. A screen showing data that
    /// another code path writes while the window is open (the archive) re-reads it here; a screen
    /// whose data only it can change does nothing.
    /// </summary>
    void OnShown() { }
}
