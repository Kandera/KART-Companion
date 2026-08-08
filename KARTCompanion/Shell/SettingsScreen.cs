using KARTCompanion.Config;
using KARTCompanion.SavedVariables;

namespace KARTCompanion.Shell;

/// <summary>Settings screen: group key, WoW install folder, sync interval, and a Force Sync
/// button to test the config immediately without leaving the screen. Layout is unchanged
/// hand-placed pixel coordinates (see IScreen's doc comment for why that stays), styled to match
/// the addon's own branding (Theme.cs, colors lifted from KAimg.jpg).
///
/// Two cards side by side (what you set once, on the left; what runs, on the right), a full-width
/// status card below them, then the action buttons — filling the shell's own fixed 1042x700
/// (see CompanionShell.ScreenSize) instead of the single narrow column this used to be. The status
/// card has a fixed height: with a fixed frame there is no room left for a long SavedVariables path
/// to grow the view downward the way it used to, so the status line is a single ellipsized line with
/// a ToolTip carrying the untruncated text instead.</summary>
public sealed class SettingsScreen : IScreen
{
    private const int RailWidth = 64;
    private const int ContentLeft = RailWidth + 16;
    private const int ContentWidth = 950;
    private const int CardGap = 20;
    private const int CardWidth = (ContentWidth - CardGap) / 2;
    private const int CardTop = 114;
    private const int CardHeight = 180;
    private const int CardPadding = 20;

    private readonly Panel _view;
    private readonly TextBox _groupKeyBox;
    private readonly TextBox _wowPathBox;
    private readonly TextBox _intervalBox;
    private readonly Theme.ToggleSwitch _autoSyncToggle;
    private readonly Theme.ToggleSwitch _autoStartToggle;
    private readonly Label _statusLabel;
    private readonly Label _lastSyncLabel;
    private readonly ToolTip _statusTooltip = new();
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

    /// <summary>Enter is OK, Escape is Cancel — but only while this screen is the one showing. The
    /// shell applies them on every switch (see IScreen); this screen used to set them on the host
    /// form itself, which left them bound to buttons no other screen displays.</summary>
    public IButtonControl? AcceptButton { get; }
    public IButtonControl? CancelButton { get; }

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

        var connectionLeft = ContentLeft;
        var syncLeft = ContentLeft + CardWidth + CardGap;

        const int captionTop = 92;
        var connectionCaption = SectionCaption("CONNECTION", connectionLeft, captionTop);
        var syncCaption = SectionCaption("SYNC", syncLeft, captionTop);

        var connectionCard = new Panel { Left = connectionLeft, Top = CardTop, Width = CardWidth, Height = CardHeight };
        Theme.StylePanel(connectionCard, Theme.Panel);
        var syncCard = new Panel { Left = syncLeft, Top = CardTop, Width = CardWidth, Height = CardHeight };
        Theme.StylePanel(syncCard, Theme.Panel);

        var fieldWidth = CardWidth - CardPadding * 2;

        // --- CONNECTION card: group key + WoW install folder ---
        var groupKeyLabel = new Label { Text = "WoWUtils group key:", Left = CardPadding, Top = CardPadding, AutoSize = true };
        Theme.StyleLabel(groupKeyLabel, dim: true);
        var groupKeyRow = Theme.CreateInputRow(fieldWidth, 38, Theme.IconGlyph.Key, out _groupKeyBox, passwordChar: true);
        groupKeyRow.Left = CardPadding;
        groupKeyRow.Top = groupKeyLabel.Top + 22;
        _groupKeyBox.Text = current.GroupKey ?? "";

        var wowPathLabel = new Label { Text = "WoW install folder (contains \"_retail_\"):", Left = CardPadding, Top = groupKeyRow.Top + groupKeyRow.Height + 20, AutoSize = true };
        Theme.StyleLabel(wowPathLabel, dim: true);
        var browseButton = Theme.CreateButton("Browse...", surfaceColor: Theme.Panel);
        browseButton.Width = 74;
        browseButton.Height = 26;
        browseButton.Click += (_, _) => BrowseForWowFolder();
        var wowPathRow = Theme.CreateInputRow(fieldWidth, 38, Theme.IconGlyph.Folder, out _wowPathBox, rightPadding: browseButton.Width + 6);
        wowPathRow.Left = CardPadding;
        wowPathRow.Top = wowPathLabel.Top + 22;
        browseButton.Left = fieldWidth - browseButton.Width - 6;
        browseButton.Top = (wowPathRow.Height - browseButton.Height) / 2;
        wowPathRow.Controls.Add(browseButton);
        _wowPathBox.Text = current.WowInstallPath ?? "";

        connectionCard.Controls.AddRange(new Control[] { groupKeyLabel, groupKeyRow, wowPathLabel, wowPathRow });

        // --- SYNC card: interval + the two toggles ---
        var intervalLabel = new Label { Text = "Sync interval:", Left = CardPadding, Top = CardPadding, AutoSize = true };
        Theme.StyleLabel(intervalLabel, dim: true);

        // A clock-icon field for the number, restricted to digits, instead of a native
        // NumericUpDown — its built-in spinner buttons don't exist in the approved design and
        // can't be restyled to match the rest of the rounded, icon-prefixed fields.
        var intervalRow = Theme.CreateInputRow(140, 38, Theme.IconGlyph.Clock, out _intervalBox, rightPadding: 30);
        intervalRow.Left = CardPadding;
        intervalRow.Top = intervalLabel.Top + 22;
        _intervalBox.Text = Math.Clamp(current.SyncIntervalMinutes, 1, 240).ToString();
        _intervalBox.KeyPress += (_, e) => { if (!char.IsDigit(e.KeyChar) && e.KeyChar != (char)Keys.Back) e.Handled = true; };
        var minLabel = new Label { Text = "Min", AutoSize = true };
        Theme.StyleLabel(minLabel, dim: true);
        minLabel.Left = intervalRow.Width - minLabel.PreferredWidth - 10;
        minLabel.Top = (intervalRow.Height - minLabel.PreferredHeight) / 2;
        intervalRow.Controls.Add(minLabel);

        _autoSyncToggle = Theme.CreateToggleSwitch(current.AutoSyncEnabled);
        _autoSyncToggle.Left = CardPadding;
        _autoSyncToggle.Top = intervalRow.Top + intervalRow.Height + 20;
        var autoSyncLabel = new Label { Text = "Sync automatically", AutoSize = true };
        Theme.StyleLabel(autoSyncLabel, dim: true);
        autoSyncLabel.Left = _autoSyncToggle.Right + 8;
        autoSyncLabel.Top = _autoSyncToggle.Top + (_autoSyncToggle.Height - autoSyncLabel.PreferredHeight) / 2;

        // Windows-Autostart lives in the registry Run key, not in config.json — the toggle's
        // initial state is read straight from there so it always reflects reality (e.g. the user
        // removed the entry via Task Manager's Startup tab).
        _autoStartToggle = Theme.CreateToggleSwitch(AutoStart.IsEnabled());
        _autoStartToggle.Left = CardPadding;
        _autoStartToggle.Top = _autoSyncToggle.Top + _autoSyncToggle.Height + 20;
        var autoStartLabel = new Label { Text = "Start with Windows", AutoSize = true };
        Theme.StyleLabel(autoStartLabel, dim: true);
        autoStartLabel.Left = _autoStartToggle.Right + 8;
        autoStartLabel.Top = _autoStartToggle.Top + (_autoStartToggle.Height - autoStartLabel.PreferredHeight) / 2;

        syncCard.Controls.AddRange(new Control[] { intervalLabel, intervalRow, _autoSyncToggle, autoSyncLabel, _autoStartToggle, autoStartLabel });

        // --- STATUS card: live dot + status line + last-sync line, full width, fixed height ---
        const int statusCardHeight = 64;
        var statusCardTop = CardTop + CardHeight + 40;
        var statusCaption = SectionCaption("STATUS", ContentLeft, statusCardTop - 22);
        var statusCard = new Panel { Left = ContentLeft, Top = statusCardTop, Width = ContentWidth, Height = statusCardHeight };
        Theme.StylePanel(statusCard, Theme.Panel);

        _liveStatusDot = Theme.CreateStatusDot(Theme.TextDim);
        _liveStatusDot.Left = CardPadding;

        var statusTextLeft = _liveStatusDot.Right + 10;
        var statusTextWidth = ContentWidth - statusTextLeft - CardPadding;

        // AutoEllipsis (not AutoSize) so a long SavedVariables path is cut to one line with a
        // trailing "…" instead of either wrapping (there is no room left to grow into — see this
        // class's own remarks) or running past the card. The full text always goes on the ToolTip
        // (see SetStatusText), whether or not it actually got truncated.
        _statusLabel = new Label
        {
            Left = statusTextLeft,
            Top = 15,
            Width = statusTextWidth,
            Height = 16,
            AutoSize = false,
            AutoEllipsis = true,
            Font = new Font(_view.Font.FontFamily, 9f),
        };
        _liveStatusDot.Top = _statusLabel.Top + (_statusLabel.Height - _liveStatusDot.Height) / 2;

        _lastSyncLabel = new Label
        {
            Left = statusTextLeft,
            Top = _statusLabel.Bottom + 4,
            Width = statusTextWidth,
            Height = 14,
            AutoSize = false,
            Font = new Font(_view.Font.FontFamily, 8f),
        };
        Theme.StyleLabel(_lastSyncLabel, dim: true);
        _lastSyncLabel.Text = SettingsScreenText.BuildLastSyncText(current.LastSyncUtc);

        SetStatusText(SettingsScreenText.BuildWatchingText(current.SavedVariablesFilePath), isError: false);
        UpdateStatusDot(isError: false);

        statusCard.Controls.AddRange(new Control[] { _liveStatusDot, _statusLabel, _lastSyncLabel });

        // --- buttons ---
        var buttonsTop = statusCardTop + statusCardHeight + 30;

        _forceSyncButton = Theme.CreateButton("Force Sync");
        _forceSyncButton.Left = ContentLeft;
        _forceSyncButton.Top = buttonsTop;
        _forceSyncButton.Width = 100;
        _forceSyncButton.Click += async (_, _) => await OnForceSyncAsync();

        var cancelButton = Theme.CreateButton("Cancel");
        cancelButton.Left = _forceSyncButton.Right + 8;
        cancelButton.Top = buttonsTop;
        cancelButton.Width = 75;
        // No native title bar means no DialogResult/ShowDialog() magic to close the window for
        // us (that only auto-closes a modally-shown Form) — the shell now hosts more than one
        // screen and stays open across them, so Close() is a request routed through whatever
        // Form is currently hosting this screen's View, found dynamically, rather than a direct
        // form close.
        cancelButton.Click += (_, _) => _view.FindForm()?.Close();

        var okButton = Theme.CreateButton("OK", primary: true);
        okButton.Width = 75;
        okButton.Left = ContentLeft + ContentWidth - okButton.Width;
        okButton.Top = buttonsTop;
        okButton.Click += (_, _) => { OnOk(); _view.FindForm()?.Close(); };

        AcceptButton = okButton;
        CancelButton = cancelButton;

        _view.Controls.AddRange(new Control[]
        {
            connectionCaption, connectionCard, syncCaption, syncCard,
            statusCaption, statusCard,
            _forceSyncButton, cancelButton, okButton,
        });
    }

    private static Label SectionCaption(string text, int left, int top)
    {
        var label = new Label { Text = text, Left = left, Top = top, AutoSize = true, Font = new Font("Segoe UI", 8f, FontStyle.Bold) };
        Theme.StyleLabel(label, dim: true);
        return label;
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
        // Always the full, untruncated text — whether or not AutoEllipsis actually cut it, so
        // nothing here has to duplicate WinForms' own decision about where a line stops fitting.
        _statusTooltip.SetToolTip(_statusLabel, text);
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
            SetStatusText(SettingsScreenText.BuildWatchingText(_resolvedSavedVariablesPath), isError: false);
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
            _lastSyncLabel.Text = SettingsScreenText.BuildLastSyncText(config.LastSyncUtc);
        }
        else
        {
            SetStatusText("Sync failed: " + (result.ErrorMessage ?? "unknown error"), isError: true);
            UpdateStatusDot(isError: true);
        }

        _forceSyncButton.Enabled = true;
    }
}

/// <summary>
/// The wording decisions behind SettingsScreen's status card, pulled out so they can be tested
/// without constructing a Form — see HistoryExportPlanner's own remarks on why nothing else there
/// has automated coverage.
/// </summary>
public static class SettingsScreenText
{
    /// <summary>The status line's idle-state text: what SavedVariables file the Companion is
    /// watching, or that none is configured yet. Transient states (syncing, an error, a completed
    /// sync's result) are set directly by SettingsScreen and do not go through this.</summary>
    public static string BuildWatchingText(string? savedVariablesPath) =>
        string.IsNullOrWhiteSpace(savedVariablesPath)
            ? "No SavedVariables file configured yet — pick your WoW install folder below."
            : "Watching " + savedVariablesPath;

    /// <summary>The status card's second line, or "" (hidden) if a sync has never completed.
    ///
    /// Takes an explicit time zone the same way HistoryExportPlanner.ParseFromDate does, rather
    /// than always reading TimeZoneInfo.Local: a test that takes its expected string from the same
    /// local-time conversion under test cannot fail, no matter which zone the machine running it
    /// happens to be in.</summary>
    public static string BuildLastSyncText(DateTimeOffset? lastSyncUtc, TimeZoneInfo? timeZone = null)
    {
        if (lastSyncUtc is not { } syncedAt) return "";
        var zone = timeZone ?? TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTime(syncedAt, zone);
        return $"Last sync {local:HH:mm}";
    }
}
