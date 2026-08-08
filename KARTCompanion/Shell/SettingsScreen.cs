using KARTCompanion.Config;
using KARTCompanion.SavedVariables;

namespace KARTCompanion.Shell;

/// <summary>Settings screen: group key, WoW install folder, sync interval, and a Force Sync
/// button to test the config immediately without leaving the screen. Layout is unchanged
/// hand-placed pixel coordinates (see IScreen's doc comment for why that stays), styled to match
/// the addon's own branding (Theme.cs, colors lifted from KAimg.jpg).</summary>
public sealed class SettingsScreen : IScreen
{
    private const int RailWidth = 64;
    private const int ContentLeft = RailWidth + 16;
    private const int ContentWidth = 396;

    private readonly Panel _view;
    private readonly TextBox _groupKeyBox;
    private readonly TextBox _wowPathBox;
    private readonly TextBox _intervalBox;
    private readonly Theme.ToggleSwitch _autoSyncToggle;
    private readonly Theme.ToggleSwitch _autoStartToggle;
    private readonly Label _statusLabel;
    private readonly Button _forceSyncButton;
    private readonly Func<CompanionConfig, Task<SyncResult>> _runSync;
    private readonly Panel _liveStatusDot;

    private string? _resolvedSavedVariablesPath;
    private readonly SyncGate _syncGate = new();

    public string Title => "Settings";
    public Theme.IconGlyph Glyph => Theme.IconGlyph.Sliders;
    public Control View => _view;

    public Color? StatusColor { get; private set; }
    public event EventHandler? StatusChanged;

    public CompanionConfig Result { get; private set; }

    /// <summary>Raised once, when OK is pressed, carrying the config to persist. Cancel raises
    /// nothing — Result stays whatever it was before this session (or whatever a successful
    /// Force Sync already set it to), so a caller that persists only on this event never
    /// persists a cancelled edit.</summary>
    public event Action<CompanionConfig>? Saved;

    public SettingsScreen(CompanionConfig current, Func<CompanionConfig, Task<SyncResult>> runSync)
    {
        Result = current;
        _runSync = runSync;
        _resolvedSavedVariablesPath = current.SavedVariablesFilePath;

        _view = new Panel();

        var groupKeyLabel = new Label { Text = "WoWUtils group key:", Left = ContentLeft, Top = 92, AutoSize = true };
        Theme.StyleLabel(groupKeyLabel, dim: true);
        var groupKeyRow = Theme.CreateInputRow(ContentWidth, 38, Theme.IconGlyph.Key, out _groupKeyBox, passwordChar: true);
        groupKeyRow.Left = ContentLeft;
        groupKeyRow.Top = 112;
        _groupKeyBox.Text = current.GroupKey ?? "";

        var wowPathLabel = new Label { Text = "WoW install folder (contains \"_retail_\"):", Left = ContentLeft, Top = 168, AutoSize = true };
        Theme.StyleLabel(wowPathLabel, dim: true);
        var browseButton = Theme.CreateButton("Browse...", surfaceColor: Theme.Panel);
        browseButton.Width = 74;
        browseButton.Height = 26;
        browseButton.Click += (_, _) => BrowseForWowFolder();
        var wowPathRow = Theme.CreateInputRow(ContentWidth, 38, Theme.IconGlyph.Folder, out _wowPathBox, rightPadding: browseButton.Width + 6);
        wowPathRow.Left = ContentLeft;
        wowPathRow.Top = 188;
        browseButton.Left = ContentWidth - browseButton.Width - 6;
        browseButton.Top = (wowPathRow.Height - browseButton.Height) / 2;
        wowPathRow.Controls.Add(browseButton);
        _wowPathBox.Text = current.WowInstallPath ?? "";

        var divider2 = new Panel { Left = ContentLeft, Top = 242, Width = ContentWidth, Height = 1, BackColor = Theme.AccentDim };

        var intervalLabel = new Label { Text = "Sync interval:", Left = ContentLeft, Top = 256, AutoSize = true };
        Theme.StyleLabel(intervalLabel, dim: true);

        // A clock-icon field for the number, restricted to digits, instead of a native
        // NumericUpDown — its built-in spinner buttons don't exist in the approved design and
        // can't be restyled to match the rest of the rounded, icon-prefixed fields.
        var intervalRow = Theme.CreateInputRow(140, 38, Theme.IconGlyph.Clock, out _intervalBox, rightPadding: 30);
        intervalRow.Left = ContentLeft;
        intervalRow.Top = 276;
        _intervalBox.Text = Math.Clamp(current.SyncIntervalMinutes, 1, 240).ToString();
        _intervalBox.KeyPress += (_, e) => { if (!char.IsDigit(e.KeyChar) && e.KeyChar != (char)Keys.Back) e.Handled = true; };
        var minLabel = new Label { Text = "Min", AutoSize = true };
        Theme.StyleLabel(minLabel, dim: true);
        minLabel.Left = intervalRow.Width - minLabel.PreferredWidth - 10;
        minLabel.Top = (intervalRow.Height - minLabel.PreferredHeight) / 2;
        intervalRow.Controls.Add(minLabel);

        _autoSyncToggle = Theme.CreateToggleSwitch(current.AutoSyncEnabled);
        _autoSyncToggle.Left = ContentLeft + 180;
        _autoSyncToggle.Top = intervalRow.Top + (intervalRow.Height - _autoSyncToggle.Height) / 2;
        var autoSyncLabel = new Label { Text = "Automatisch synchronisieren", AutoSize = true };
        Theme.StyleLabel(autoSyncLabel, dim: true);
        autoSyncLabel.Left = _autoSyncToggle.Right + 8;
        autoSyncLabel.Top = intervalRow.Top + (intervalRow.Height - autoSyncLabel.PreferredHeight) / 2;

        // Windows-Autostart lives in the registry Run key, not in config.json — the toggle's
        // initial state is read straight from there so it always reflects reality (e.g. the user
        // removed the entry via Task Manager's Startup tab).
        _autoStartToggle = Theme.CreateToggleSwitch(AutoStart.IsEnabled());
        _autoStartToggle.Left = ContentLeft;
        _autoStartToggle.Top = 328;
        var autoStartLabel = new Label { Text = "Mit Windows starten", AutoSize = true };
        Theme.StyleLabel(autoStartLabel, dim: true);
        autoStartLabel.Left = _autoStartToggle.Right + 8;
        autoStartLabel.Top = _autoStartToggle.Top + (_autoStartToggle.Height - autoStartLabel.PreferredHeight) / 2;

        // A compact "live" row (small dot + one status line) instead of a bare block of text.
        _liveStatusDot = Theme.CreateStatusDot(Theme.TextDim);
        _liveStatusDot.Left = ContentLeft;
        _liveStatusDot.Top = 372;

        // AutoSize + MaximumSize lets this grow downward to however many lines a long path
        // actually needs, instead of clipping it at a guessed fixed height.
        _statusLabel = new Label
        {
            Left = ContentLeft + _liveStatusDot.Width + 8,
            Top = 368,
            Width = ContentWidth - _liveStatusDot.Width - 8,
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(ContentWidth - _liveStatusDot.Width - 8, 0),
            Font = new Font(_view.Font.FontFamily, 8f),
        };
        SetStatusText(BuildInitialStatusText(current), isError: false);
        UpdateStatusDot(isError: false);

        _forceSyncButton = Theme.CreateButton("Force Sync");
        _forceSyncButton.Left = ContentLeft;
        _forceSyncButton.Width = 100;
        _forceSyncButton.Click += async (_, _) => await OnForceSyncAsync();

        var cancelButton = Theme.CreateButton("Cancel");
        cancelButton.Width = 75;
        // No native title bar means no DialogResult/ShowDialog() magic to close the window for
        // us (that only auto-closes a modally-shown Form) — the shell now hosts more than one
        // screen and stays open across them, so Close() is a request routed through whatever
        // Form is currently hosting this screen's View, found dynamically, rather than a direct
        // form close.
        cancelButton.Click += (_, _) => _view.FindForm()?.Close();

        var okButton = Theme.CreateButton("OK", primary: true);
        okButton.Width = 75;
        okButton.Click += (_, _) => { OnOk(); _view.FindForm()?.Close(); };

        _view.ParentChanged += (_, _) =>
        {
            if (_view.FindForm() is { } form)
            {
                form.AcceptButton = okButton;
                form.CancelButton = cancelButton;
            }
        };

        _view.Controls.AddRange(new Control[]
        {
            groupKeyLabel, groupKeyRow, wowPathLabel, wowPathRow,
            divider2,
            intervalLabel, intervalRow, _autoSyncToggle, autoSyncLabel,
            _autoStartToggle, autoStartLabel,
            _liveStatusDot, _statusLabel, _forceSyncButton, cancelButton, okButton,
        });

        LayoutBelowStatusLabel();
        _statusLabel.SizeChanged += (_, _) => LayoutBelowStatusLabel();

        void LayoutBelowStatusLabel()
        {
            var y = Math.Max(_statusLabel.Top + _statusLabel.Height, _liveStatusDot.Bottom) + 14;
            _forceSyncButton.Top = y;
            cancelButton.Top = okButton.Top = y;
            _forceSyncButton.Left = ContentLeft;
            cancelButton.Left = ContentLeft + _forceSyncButton.Width + 8;
            okButton.Left = ContentLeft + ContentWidth - okButton.Width;
            _view.Size = new System.Drawing.Size(ContentLeft + ContentWidth + 12, y + 40);
        }
    }

    // Colors the inline live-status dot next to the status text: green once a SavedVariables
    // file is resolved, red on error, dim gray while still unconfigured. Also pushes the same
    // color out as StatusColor/StatusChanged so the shell's rail dot — which has no idea what
    // "sync health" means — can mirror it without this class reaching into rail-owned chrome.
    private void UpdateStatusDot(bool isError)
    {
        var color = isError
            ? Theme.Error
            : _resolvedSavedVariablesPath is not null
                ? Theme.Success
                : Theme.TextDim;
        Theme.SetStatusDotColor(_liveStatusDot, color);
        StatusColor = color;
        StatusChanged?.Invoke(this, EventArgs.Empty);
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
        UpdateStatusDot(isError: _resolvedSavedVariablesPath is null);
    }

    // Copy the config this screen was opened with and overwrite only the fields it actually
    // edits. Building a fresh CompanionConfig out of named properties dropped everything the
    // dialog does not name: LootHistoryReadAt already was one of those, so pressing OK — or
    // forcing a sync from in here — persisted a config with empty loot-history read stamps. This
    // shape cannot omit a field, so the next one anyone adds survives without touching this method.
    private CompanionConfig BuildResultFromFields()
    {
        var config = Result.Copy();
        config.GroupKey = _groupKeyBox.Text.Trim();
        config.WowInstallPath = string.IsNullOrWhiteSpace(_wowPathBox.Text) ? null : _wowPathBox.Text.Trim();
        config.SavedVariablesFilePath = _resolvedSavedVariablesPath;
        config.SyncIntervalMinutes = Math.Clamp(int.TryParse(_intervalBox.Text, out var minutes) ? minutes : 15, 1, 240);
        config.AutoSyncEnabled = _autoSyncToggle.IsOn;
        return config;
    }

    private void OnOk()
    {
        Result = BuildResultFromFields();
        // Applied only on OK (not live on toggle click) so Cancel really cancels. Written
        // unconditionally: re-enabling refreshes a stale exe path after the app was moved.
        AutoStart.SetEnabled(_autoStartToggle.IsOn);
        Saved?.Invoke(Result);
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
            UpdateStatusDot(isError: false);
        }
        else
        {
            SetStatusText("Sync failed: " + (result.ErrorMessage ?? "unknown error"), isError: true);
            UpdateStatusDot(isError: true);
        }

        _forceSyncButton.Enabled = true;
    }
}
