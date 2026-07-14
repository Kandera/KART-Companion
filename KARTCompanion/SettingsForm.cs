using KARTCompanion.Config;
using KARTCompanion.SavedVariables;

namespace KARTCompanion;

/// <summary>Minimal code-only settings dialog: group key, WoW install folder, sync interval,
/// and a Force Sync button to test the config immediately without closing the dialog. Styled to
/// match the addon's own branding (Theme.cs, colors lifted from KAimg.jpg).</summary>
public sealed class SettingsForm : Form
{
    private readonly TextBox _groupKeyBox;
    private readonly TextBox _wowPathBox;
    private readonly NumericUpDown _intervalBox;
    private readonly Label _statusLabel;
    private readonly Button _forceSyncButton;
    private readonly Func<CompanionConfig, Task<SyncResult>> _runSync;

    private string? _resolvedSavedVariablesPath;
    private readonly SyncGate _syncGate = new();

    public CompanionConfig Result { get; private set; }

    public SettingsForm(CompanionConfig current, Func<CompanionConfig, Task<SyncResult>> runSync, Bitmap logo, Icon icon)
    {
        Result = current;
        _runSync = runSync;
        _resolvedSavedVariablesPath = current.SavedVariablesFilePath;

        Text = "KART Companion — Settings";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Theme.StyleForm(this);

        var logoBox = new PictureBox
        {
            Image = logo,
            SizeMode = PictureBoxSizeMode.Zoom,
            Left = 12,
            Top = 12,
            Width = 36,
            Height = 36,
        };

        var titleLabel = new Label
        {
            Text = "KART Companion",
            Left = 58,
            Top = 18,
            Width = 300,
            Font = new Font(Font.FontFamily, 13, FontStyle.Bold),
        };
        Theme.StyleLabel(titleLabel);

        var divider = new Panel { Left = 12, Top = 56, Width = 396, Height = 1, BackColor = Theme.AccentDim };

        var groupKeyLabel = new Label { Text = "WoWUtils group key:", Left = 12, Top = 68, Width = 250 };
        Theme.StyleLabel(groupKeyLabel);
        _groupKeyBox = new TextBox { Left = 12, Top = 88, Width = 396, UseSystemPasswordChar = true, Text = current.GroupKey ?? "" };
        Theme.StyleTextBox(_groupKeyBox);

        var wowPathLabel = new Label { Text = "WoW install folder (contains \"_retail_\"):", Left = 12, Top = 136, Width = 300 };
        Theme.StyleLabel(wowPathLabel);
        _wowPathBox = new TextBox { Left = 12, Top = 156, Width = 316 };
        Theme.StyleTextBox(_wowPathBox);
        var browseButton = new Button { Text = "Browse...", Left = 334, Top = 155, Width = 74 };
        Theme.StyleButton(browseButton);
        browseButton.Click += (_, _) => BrowseForWowFolder();

        var divider2 = new Panel { Left = 12, Top = 186, Width = 396, Height = 1, BackColor = Theme.AccentDim };

        var intervalLabel = new Label { Text = "Sync interval (minutes):", Left = 12, Top = 198, Width = 200 };
        Theme.StyleLabel(intervalLabel);
        _intervalBox = new NumericUpDown { Left = 12, Top = 218, Width = 80, Minimum = 1, Maximum = 240, Value = Math.Clamp(current.SyncIntervalMinutes, 1, 240) };
        Theme.StyleNumericUpDown(_intervalBox);

        // AutoSize + MaximumSize lets this grow downward to however many lines a long path
        // actually needs, instead of clipping it at a guessed fixed height.
        _statusLabel = new Label
        {
            Left = 12,
            Top = 248,
            Width = 396,
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(396, 0),
        };
        SetStatusText(BuildInitialStatusText(current), isError: false);

        _forceSyncButton = new Button { Text = "Force Sync", Left = 12, Width = 100 };
        Theme.StyleButton(_forceSyncButton);
        _forceSyncButton.Click += async (_, _) => await OnForceSyncAsync();

        var okButton = new Button { Text = "OK", Width = 75, DialogResult = DialogResult.OK };
        Theme.StyleButton(okButton, primary: true);
        var cancelButton = new Button { Text = "Cancel", Width = 75, DialogResult = DialogResult.Cancel };
        Theme.StyleButton(cancelButton);
        okButton.Click += (_, _) => OnOk();

        AcceptButton = okButton;
        CancelButton = cancelButton;

        Controls.AddRange(new Control[]
        {
            logoBox, titleLabel, divider,
            groupKeyLabel, _groupKeyBox, wowPathLabel, _wowPathBox, browseButton,
            divider2,
            intervalLabel, _intervalBox, _statusLabel, _forceSyncButton, okButton, cancelButton,
        });

        LayoutBelowStatusLabel();
        _statusLabel.SizeChanged += (_, _) => LayoutBelowStatusLabel();

        void LayoutBelowStatusLabel()
        {
            var y = _statusLabel.Top + _statusLabel.Height + 12;
            _forceSyncButton.Top = y;
            okButton.Top = cancelButton.Top = y;
            okButton.Left = 252;
            cancelButton.Left = 333;
            ClientSize = new System.Drawing.Size(420, y + 40);
        }
    }

    private static string BuildInitialStatusText(CompanionConfig current) =>
        string.IsNullOrWhiteSpace(current.SavedVariablesFilePath)
            ? "No SavedVariables file configured yet — pick your WoW install folder below."
            : "SavedVariables file: " + current.SavedVariablesFilePath;

    private enum StatusKind { Neutral, Error, Success }

    private void SetStatusText(string text, bool isError) =>
        SetStatusText(text, isError ? StatusKind.Error : StatusKind.Neutral);

    private void SetStatusText(string text, StatusKind kind)
    {
        _statusLabel.ForeColor = kind switch
        {
            StatusKind.Error => Theme.Error,
            StatusKind.Success => Theme.Success,
            _ => Theme.TextDim,
        };
        _statusLabel.Text = text;
    }

    private void BrowseForWowFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Select your World of Warcraft install folder (the one containing \"_retail_\") — or \"_retail_\" itself, either works." };
        if (dialog.ShowDialog() != DialogResult.OK) return;

        _wowPathBox.Text = dialog.SelectedPath;
        ResolveSavedVariablesPath(dialog.SelectedPath);
    }

    private void ResolveSavedVariablesPath(string wowRoot)
    {
        var matches = SavedVariablesLocator.FindSavedVariablesFiles(wowRoot);
        if (matches.Count == 0)
        {
            _resolvedSavedVariablesPath = null;
            SetStatusText("No KeineAhnungRaidTools SavedVariables file found there — log into WoW with the addon installed at least once first.", isError: true);
        }
        else
        {
            // Multiple Battle.net accounts under one install: pick whichever was written to
            // most recently as the best guess for "the active one".
            _resolvedSavedVariablesPath = matches.OrderByDescending(File.GetLastWriteTimeUtc).First();
            SetStatusText("SavedVariables file: " + _resolvedSavedVariablesPath, isError: false);
        }
    }

    private CompanionConfig BuildResultFromFields() => new()
    {
        GroupKey = _groupKeyBox.Text.Trim(),
        SavedVariablesFilePath = _resolvedSavedVariablesPath,
        SyncIntervalMinutes = (int)_intervalBox.Value,
        LastSyncUtc = Result.LastSyncUtc,
    };

    private void OnOk()
    {
        Result = BuildResultFromFields();
    }

    private async Task OnForceSyncAsync()
    {
        if (_syncGate.IsRunning) return;

        var config = BuildResultFromFields();
        if (!config.IsComplete)
        {
            SetStatusText("Enter a group key and pick your WoW folder first.", isError: true);
            return;
        }

        _forceSyncButton.Enabled = false;
        SetStatusText("Syncing...", isError: false);

        var result = await _syncGate.RunAsync(() => _runSync(config));

        if (result is null)
        {
            // Another sync was already in progress — leave the "Syncing..." status as-is.
        }
        else if (result.Success)
        {
            config.LastSyncUtc = DateTimeOffset.UtcNow;
            Result = config;
            var skippedNote = result.SkippedCharacters > 0 ? $" ({result.SkippedCharacters} skipped)" : "";
            SetStatusText($"Synced {result.PlayerCount} players{skippedNote}. SavedVariables file: {config.SavedVariablesFilePath}", StatusKind.Success);
        }
        else
        {
            SetStatusText("Sync failed: " + (result.ErrorMessage ?? "unknown error"), isError: true);
        }

        _forceSyncButton.Enabled = true;
    }
}
