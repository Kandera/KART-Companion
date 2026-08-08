using KARTCompanion.Archive;
using KARTCompanion.SavedVariables;

namespace KARTCompanion.Shell;

/// <summary>
/// Corrects one award, for the export's sake. Two fields and a switch — see AwardEditor.EditableFields
/// for why the list is short, and the design spec for why nothing here writes towards the game.
///
/// The dialog shows what the addon itself wrote next to each box it differs from. That is the point of
/// keeping the original: a corrected value is an assertion by the maintainer rather than a record from
/// the game, and the two must stay tellable apart.
///
/// Reverting is not a separate path. "Use the addon's values" simply puts the original text back in the
/// boxes; AwardEditor.Set then removes the correction, because a value equal to the addon's own is not
/// a correction at all.
/// </summary>
public sealed class AwardEditDialog : Form
{
    private const int Pad = 24;
    private const int FieldWidth = 400;

    private readonly TextBox _winnerBox;
    private readonly TextBox _reasonBox;
    private readonly Theme.ToggleSwitch _excludeToggle;
    private readonly string _originalWinner;
    private readonly string _originalReason;

    public string Winner => _winnerBox.Text;
    public string Reason => _reasonBox.Text;
    public bool ExcludedFromExport => _excludeToggle.IsOn;

    public AwardEditDialog(ArchivedAward award)
    {
        _originalWinner = Original(award, "winner");
        _originalReason = Original(award, "reason");

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(FieldWidth + Pad * 2, 330);
        Theme.StyleForm(this);

        var title = new Label { Left = Pad, Top = Pad, AutoSize = true, Text = "Correct this award" };
        Theme.StyleLabel(title);
        Theme.MakeDragHandle(title, this);

        var item = new Label
        {
            Left = Pad,
            Top = Pad + 26,
            Width = FieldWidth,
            AutoSize = false,
            Height = 18,
            Text = ArchiveQuery.ItemDisplayName(new LootHistoryEntry(award.EffectiveFields).Item),
        };
        Theme.StyleLabel(item, dim: true);

        var closeGlyph = Theme.CreateCloseGlyph(() => { DialogResult = DialogResult.Cancel; Close(); });
        closeGlyph.Left = ClientSize.Width - closeGlyph.Width - 12;
        closeGlyph.Top = 12;

        var winnerLabel = FieldLabel("Player", 84);
        _winnerBox = FieldBox(104, new LootHistoryEntry(award.EffectiveFields).Winner ?? "");
        var winnerOriginal = OriginalLabel(132, _originalWinner, _winnerBox.Text);

        var reasonLabel = FieldLabel("Reason", 160);
        _reasonBox = FieldBox(180, new LootHistoryEntry(award.EffectiveFields).Reason ?? "");
        var reasonOriginal = OriginalLabel(208, _originalReason, _reasonBox.Text);

        _winnerBox.TextChanged += (_, _) => UpdateOriginalLabel(winnerOriginal, _originalWinner, _winnerBox.Text);
        _reasonBox.TextChanged += (_, _) => UpdateOriginalLabel(reasonOriginal, _originalReason, _reasonBox.Text);

        // Theme.ToggleSwitch, not a CheckBox. Theme.cs says why in its own words: a CheckBox "would
        // look like a completely different, older control style next to the rounded fields around it".
        _excludeToggle = Theme.CreateToggleSwitch(award.ExcludedFromExport);
        _excludeToggle.Left = Pad;
        _excludeToggle.Top = 236;
        var excludeLabel = new Label { Text = "Never export this award", AutoSize = true };
        Theme.StyleLabel(excludeLabel, dim: true);
        excludeLabel.Left = _excludeToggle.Right + 8;
        excludeLabel.Top = _excludeToggle.Top + (_excludeToggle.Height - excludeLabel.PreferredHeight) / 2;

        var save = Theme.CreateButton("Save", primary: true);
        save.Left = Pad;
        save.Top = 274;
        save.Width = 110;
        save.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };

        var useAddon = Theme.CreateButton("Use the addon's values");
        useAddon.Left = save.Right + 10;
        useAddon.Top = 274;
        useAddon.Width = 160;
        useAddon.Click += (_, _) =>
        {
            _winnerBox.Text = _originalWinner;
            _reasonBox.Text = _originalReason;
        };

        var cancel = Theme.CreateButton("Cancel");
        cancel.Left = useAddon.Right + 10;
        cancel.Top = 274;
        cancel.Width = 90;
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[]
        {
            title, item, closeGlyph, winnerLabel, _winnerBox, winnerOriginal,
            reasonLabel, _reasonBox, reasonOriginal, _excludeToggle, excludeLabel,
            save, useAddon, cancel,
        });

        AcceptButton = save;
        CancelButton = cancel;

        // Same order as CompanionShell: the rounded region is applied after the controls are in, and it
        // re-subscribes to Resize itself.
        Theme.ApplyRoundedFormRegion(this);
    }

    private static string Original(ArchivedAward award, string field) =>
        award.Fields.TryGetValue(field, out var v) && v is string s ? s : "";

    private static Label FieldLabel(string text, int top)
    {
        var label = new Label { Text = text, Left = Pad, Top = top, AutoSize = true };
        Theme.StyleLabel(label, dim: true);
        return label;
    }

    private static TextBox FieldBox(int top, string text) => new()
    {
        Left = Pad,
        Top = top,
        Width = FieldWidth,
        Text = text,
        BackColor = Theme.Panel,
        ForeColor = Theme.Text,
        BorderStyle = BorderStyle.FixedSingle,
    };

    private static Label OriginalLabel(int top, string original, string current)
    {
        var label = new Label
        {
            Left = Pad,
            Top = top,
            Width = FieldWidth,
            AutoSize = false,
            Height = 16,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 8f),
        };
        Theme.StyleLabel(label, dim: true);
        UpdateOriginalLabel(label, original, current);
        return label;
    }

    private static void UpdateOriginalLabel(Label label, string original, string current)
    {
        label.Text = current.Trim() == original ? "" : $"The addon wrote: {(original.Length == 0 ? "nothing" : original)}";
    }
}
