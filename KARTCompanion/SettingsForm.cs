using KARTCompanion.Config;
using KARTCompanion.SavedVariables;

namespace KARTCompanion;

/// <summary>Minimal code-only settings dialog: group key, WoW install folder, sync interval.</summary>
public sealed class SettingsForm : Form
{
    private readonly TextBox _groupKeyBox;
    private readonly TextBox _wowPathBox;
    private readonly NumericUpDown _intervalBox;
    private readonly Label _statusLabel;

    public CompanionConfig Result { get; private set; }

    public SettingsForm(CompanionConfig current)
    {
        Result = current;

        Text = "KART Companion — Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new System.Drawing.Size(420, 220);

        var groupKeyLabel = new Label { Text = "WoWUtils group key:", Left = 12, Top = 15, Width = 200 };
        _groupKeyBox = new TextBox { Left = 12, Top = 35, Width = 396, UseSystemPasswordChar = true, Text = current.GroupKey ?? "" };

        var wowPathLabel = new Label { Text = "WoW install folder (contains \"_retail_\"):", Left = 12, Top = 70, Width = 300 };
        _wowPathBox = new TextBox { Left = 12, Top = 90, Width = 316 };
        var browseButton = new Button { Text = "Browse...", Left = 334, Top = 89, Width = 74 };
        browseButton.Click += (_, _) => BrowseForWowFolder();

        var intervalLabel = new Label { Text = "Sync interval (minutes):", Left = 12, Top = 125, Width = 200 };
        _intervalBox = new NumericUpDown { Left = 12, Top = 145, Width = 80, Minimum = 1, Maximum = 240, Value = Math.Clamp(current.SyncIntervalMinutes, 1, 240) };

        _statusLabel = new Label { Left = 12, Top = 175, Width = 396, Height = 20, ForeColor = System.Drawing.Color.Gray };
        if (!string.IsNullOrWhiteSpace(current.SavedVariablesFilePath))
            _statusLabel.Text = "SavedVariables file: " + current.SavedVariablesFilePath;

        var okButton = new Button { Text = "OK", Left = 252, Top = 185, Width = 75, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", Left = 333, Top = 185, Width = 75, DialogResult = DialogResult.Cancel };
        okButton.Click += (_, _) => OnOk();

        AcceptButton = okButton;
        CancelButton = cancelButton;

        Controls.AddRange(new Control[]
        {
            groupKeyLabel, _groupKeyBox, wowPathLabel, _wowPathBox, browseButton,
            intervalLabel, _intervalBox, _statusLabel, okButton, cancelButton,
        });

        // Re-position OK/Cancel below the status label since it's taller than the rest.
        okButton.Top = cancelButton.Top = _statusLabel.Top + 25;
        ClientSize = new System.Drawing.Size(420, okButton.Top + 40);
    }

    private void BrowseForWowFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Select your World of Warcraft install folder (the one containing \"_retail_\")." };
        if (dialog.ShowDialog() != DialogResult.OK) return;

        _wowPathBox.Text = dialog.SelectedPath;
        ResolveSavedVariablesPath(dialog.SelectedPath);
    }

    private string? _resolvedSavedVariablesPath;

    private void ResolveSavedVariablesPath(string wowRoot)
    {
        var matches = SavedVariablesLocator.FindSavedVariablesFiles(wowRoot);
        if (matches.Count == 0)
        {
            _resolvedSavedVariablesPath = null;
            _statusLabel.ForeColor = System.Drawing.Color.IndianRed;
            _statusLabel.Text = "No KeineAhnungRaidTools SavedVariables file found there — log into WoW with the addon installed at least once first.";
        }
        else
        {
            // Multiple Battle.net accounts under one install: pick whichever was written to
            // most recently as the best guess for "the active one".
            _resolvedSavedVariablesPath = matches.OrderByDescending(File.GetLastWriteTimeUtc).First();
            _statusLabel.ForeColor = System.Drawing.Color.Gray;
            _statusLabel.Text = "SavedVariables file: " + _resolvedSavedVariablesPath;
        }
    }

    private void OnOk()
    {
        if (string.IsNullOrWhiteSpace(_wowPathBox.Text))
        {
            // Keep whatever was already configured if the user didn't touch the path field.
            _resolvedSavedVariablesPath ??= Result.SavedVariablesFilePath;
        }

        Result = new CompanionConfig
        {
            GroupKey = _groupKeyBox.Text.Trim(),
            SavedVariablesFilePath = _resolvedSavedVariablesPath ?? Result.SavedVariablesFilePath,
            SyncIntervalMinutes = (int)_intervalBox.Value,
            LastSyncUtc = Result.LastSyncUtc,
        };
    }
}
