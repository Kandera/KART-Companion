using KARTCompanion.Config;
using KARTCompanion.SavedVariables;

namespace KARTCompanion;

/// <summary>Minimal code-only settings dialog: group key, WoW install folder, sync interval,
/// and a Force Sync button to test the config immediately without closing the dialog. Styled to
/// match the addon's own branding (Theme.cs, colors lifted from KAimg.jpg).</summary>
public sealed class SettingsForm : Form
{
    private const int RailWidth = 64;
    private const int ContentLeft = RailWidth + 16;
    private const int ContentWidth = 396;

    private readonly TextBox _groupKeyBox;
    private readonly TextBox _wowPathBox;
    private readonly TextBox _intervalBox;
    private readonly Theme.ToggleSwitch _autoSyncToggle;
    private readonly Theme.ToggleSwitch _autoStartToggle;
    private readonly Label _statusLabel;
    private readonly Button _forceSyncButton;
    private readonly Func<CompanionConfig, Task<SyncResult>> _runSync;
    private readonly Panel _rail;
    private readonly Panel _railStatusDot;
    private readonly Panel _liveStatusDot;

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
        // No native title bar: the approved mockup is a borderless, rounded floating card with
        // the logo/title drawn inside the body, not a light OS title bar sitting on top of a
        // dark client area. FormBorderStyle.None removes that bar (and, with it, the window's
        // only means of being dragged or closed by mouse — both are rebuilt below).
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Theme.StyleForm(this);

        // Icon rail: a narrow navigation-style column separating the logo/status glance from the
        // form fields, instead of the logo sitting inline with the title. Height is set to the
        // dialog's final height once the rest of the layout is measured (see LayoutBelowStatusLabel).
        _rail = new Panel { Left = 0, Top = 0, Width = RailWidth };
        Theme.StylePanel(_rail, Theme.RailBackground);
        Theme.MakeDragHandle(_rail, this);

        var railDivider = new Panel { Left = RailWidth, Top = 0, Width = 1, BackColor = Theme.BorderStrong };

        var closeGlyph = Theme.CreateCloseGlyph(Close);
        closeGlyph.Left = ContentLeft + ContentWidth - closeGlyph.Width + 8;
        closeGlyph.Top = 12;

        var logoBox = new PictureBox
        {
            Image = logo,
            SizeMode = PictureBoxSizeMode.Zoom,
            Left = (RailWidth - 34) / 2,
            Top = 20,
            Width = 34,
            Height = 34,
        };

        // Single active nav glyph under the logo — a "you are here" marker for the one screen
        // this app has, with a thin accent bar to its left, not a second, unclickable nav item
        // (that would imply a multi-page rail that doesn't exist).
        var activeIcon = Theme.CreateIcon(Theme.IconGlyph.Sliders, Theme.Text, 16);
        activeIcon.Left = (RailWidth - activeIcon.Width) / 2;
        activeIcon.Top = 72;
        var activeBar = new Panel { Left = activeIcon.Left - 12, Top = activeIcon.Top - 1, Width = 3, Height = 18, BackColor = Theme.Accent };

        _railStatusDot = Theme.CreateStatusDot(Theme.TextDim);
        _railStatusDot.Left = (RailWidth - _railStatusDot.Width) / 2;
        _rail.Controls.AddRange(new Control[] { logoBox, activeBar, activeIcon, _railStatusDot });

        // AutoSize (not a fixed Width spanning the whole content column) so the label's hit-test
        // area hugs the short "KART Companion" text instead of silently overlapping the close
        // glyph's hitbox further right, which would swallow its clicks.
        var titleLabel = new Label
        {
            Text = "KART Companion",
            Left = ContentLeft,
            Top = 20,
            AutoSize = true,
            Font = new Font(Font.FontFamily, 14, FontStyle.Bold),
        };
        Theme.StyleLabel(titleLabel);

        var subtitleLabel = new Label { Text = "Settings", Left = ContentLeft, Top = 47, AutoSize = true };
        Theme.StyleLabel(subtitleLabel, dim: true);
        Theme.MakeDragHandle(titleLabel, this);
        Theme.MakeDragHandle(subtitleLabel, this);

        var divider = new Panel { Left = ContentLeft, Top = 78, Width = ContentWidth, Height = 1, BackColor = Theme.AccentDim };

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

        // A compact "live" row (small dot + one status line) instead of a bare block of text —
        // mirrors the rail's health dot right next to the text it explains.
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
            Font = new Font(Font.FontFamily, 8f),
        };
        SetStatusText(BuildInitialStatusText(current), isError: false);
        UpdateRailStatusDot(isError: false);

        _forceSyncButton = Theme.CreateButton("Force Sync");
        _forceSyncButton.Left = ContentLeft;
        _forceSyncButton.Width = 100;
        _forceSyncButton.Click += async (_, _) => await OnForceSyncAsync();

        var cancelButton = Theme.CreateButton("Cancel");
        cancelButton.Width = 75;
        cancelButton.DialogResult = DialogResult.Cancel;

        var okButton = Theme.CreateButton("OK", primary: true);
        okButton.Width = 75;
        okButton.DialogResult = DialogResult.OK;
        okButton.Click += (_, _) => OnOk();

        AcceptButton = okButton;
        CancelButton = cancelButton;

        Controls.AddRange(new Control[]
        {
            _rail, railDivider, titleLabel, subtitleLabel, closeGlyph, divider,
            groupKeyLabel, groupKeyRow, wowPathLabel, wowPathRow,
            divider2,
            intervalLabel, intervalRow, _autoSyncToggle, autoSyncLabel,
            _autoStartToggle, autoStartLabel,
            _liveStatusDot, _statusLabel, _forceSyncButton, cancelButton, okButton,
        });

        LayoutBelowStatusLabel();
        // ApplyRoundedFormRegion re-subscribes to Resize internally, so this needs to run only
        // once — later ClientSize changes from LayoutBelowStatusLabel already trigger Resize,
        // which re-applies the rounded Region on its own.
        Theme.ApplyRoundedFormRegion(this);
        _statusLabel.SizeChanged += (_, _) => LayoutBelowStatusLabel();

        void LayoutBelowStatusLabel()
        {
            var y = Math.Max(_statusLabel.Top + _statusLabel.Height, _liveStatusDot.Bottom) + 14;
            _forceSyncButton.Top = y;
            cancelButton.Top = okButton.Top = y;
            _forceSyncButton.Left = ContentLeft;
            cancelButton.Left = ContentLeft + _forceSyncButton.Width + 8;
            okButton.Left = ContentLeft + ContentWidth - okButton.Width;
            ClientSize = new System.Drawing.Size(ContentLeft + ContentWidth + 12, y + 40);
            _rail.Height = ClientSize.Height;
            railDivider.Height = ClientSize.Height;
            _railStatusDot.Top = _rail.Height - 30;
        }
    }

    // Mirrors the status text's color-coding on both the rail dot and the inline live-status
    // dot, so sync health reads at a glance without having to read the status text: green once a
    // SavedVariables file is resolved, red on error, dim gray while still unconfigured.
    private void UpdateRailStatusDot(bool isError)
    {
        var color = isError
            ? Theme.Error
            : _resolvedSavedVariablesPath is not null
                ? Theme.Success
                : Theme.TextDim;
        Theme.SetStatusDotColor(_railStatusDot, color);
        Theme.SetStatusDotColor(_liveStatusDot, color);
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
        UpdateRailStatusDot(isError: _resolvedSavedVariablesPath is null);
    }

    private CompanionConfig BuildResultFromFields() => new()
    {
        GroupKey = _groupKeyBox.Text.Trim(),
        WowInstallPath = string.IsNullOrWhiteSpace(_wowPathBox.Text) ? null : _wowPathBox.Text.Trim(),
        SavedVariablesFilePath = _resolvedSavedVariablesPath,
        SyncIntervalMinutes = Math.Clamp(int.TryParse(_intervalBox.Text, out var minutes) ? minutes : 15, 1, 240),
        AutoSyncEnabled = _autoSyncToggle.IsOn,
        LastSyncUtc = Result.LastSyncUtc,
    };

    private void OnOk()
    {
        Result = BuildResultFromFields();
        // Applied only on OK (not live on toggle click) so Cancel really cancels. Written
        // unconditionally: re-enabling refreshes a stale exe path after the app was moved.
        AutoStart.SetEnabled(_autoStartToggle.IsOn);
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
            UpdateRailStatusDot(isError: false);
        }
        else
        {
            SetStatusText("Sync failed: " + (result.ErrorMessage ?? "unknown error"), isError: true);
            UpdateRailStatusDot(isError: true);
        }

        _forceSyncButton.Enabled = true;
    }
}
