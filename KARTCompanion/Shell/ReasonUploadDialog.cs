using KARTCompanion.Export;

namespace KARTCompanion.Shell;

/// <summary>
/// The switches for one upload. A reason that is on is sent by Export to WowUtils. A reason that
/// is off stays in the list and is left out of the upload. The choice is whatever the maintainer
/// needs this time; nothing here is special about bonus rolls.
/// </summary>
public sealed class ReasonUploadDialog : Form
{
    private const int Pad = 24;
    private const int FieldWidth = 420;

    private readonly List<Theme.ToggleSwitch> _toggles = new();

    public IReadOnlyList<string> ExcludedReasons { get; private set; } = Array.Empty<string>();

    public ReasonUploadDialog(IEnumerable<string> reasonKeys, IReadOnlySet<string> excluded)
    {
        var keys = reasonKeys
            .Distinct(StringComparer.Ordinal)
            .OrderBy(UploadReasons.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(FieldWidth + Pad * 2, 460);
        Theme.StyleForm(this);

        var title = new Label { Left = Pad, Top = Pad, AutoSize = true, Text = "Loot reasons" };
        Theme.StyleLabel(title);
        Theme.MakeDragHandle(title, this);

        var closeGlyph = Theme.CreateCloseGlyph(() => { DialogResult = DialogResult.Cancel; Close(); });
        closeGlyph.Left = ClientSize.Width - closeGlyph.Width - 12;
        closeGlyph.Top = 12;

        var explain = new Label
        {
            Left = Pad,
            Top = Pad + 28,
            Width = FieldWidth,
            Height = 32,
            AutoSize = false,
            Text = "On means Export to WowUtils sends that reason. Off leaves it in the list and out of the upload.",
        };
        Theme.StyleLabel(explain, dim: true);

        var scroll = new Panel
        {
            Left = Pad,
            Top = 92,
            Width = FieldWidth,
            Height = 280,
            AutoScroll = true,
            BackColor = Theme.Background,
        };

        var y = 0;
        foreach (var key in keys)
        {
            var toggle = Theme.CreateToggleSwitch(!excluded.Contains(key));
            toggle.Left = 0;
            toggle.Top = y;
            toggle.Tag = key;
            var label = new Label
            {
                Text = UploadReasons.Label(key),
                AutoSize = true,
                Left = toggle.Width + 10,
                Top = y + 1,
            };
            Theme.StyleLabel(label);
            scroll.Controls.Add(toggle);
            scroll.Controls.Add(label);
            _toggles.Add(toggle);
            y += 28;
        }

        var allOn = Theme.CreateButton("All on");
        allOn.Left = Pad;
        allOn.Top = 388;
        allOn.Width = 90;
        allOn.Click += (_, _) => SetAll(true);

        var allOff = Theme.CreateButton("All off");
        allOff.Left = allOn.Right + 8;
        allOff.Top = 388;
        allOff.Width = 90;
        allOff.Click += (_, _) => SetAll(false);

        var ok = Theme.CreateButton("Save", primary: true);
        ok.Left = FieldWidth + Pad - 200;
        ok.Top = 388;
        ok.Width = 90;
        ok.Click += (_, _) =>
        {
            ExcludedReasons = _toggles.Where(t => !t.IsOn).Select(t => (string)t.Tag!).ToList();
            DialogResult = DialogResult.OK;
            Close();
        };

        var cancel = Theme.CreateButton("Cancel");
        cancel.Left = ok.Right + 8;
        cancel.Top = 388;
        cancel.Width = 90;
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[] { title, closeGlyph, explain, scroll, allOn, allOff, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
        Theme.ApplyRoundedFormRegion(this);
    }

    private void SetAll(bool on)
    {
        foreach (var toggle in _toggles) toggle.IsOn = on;
    }
}
