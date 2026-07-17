using KARTCompanion.Config;
using KARTCompanion.SavedVariables;
using KARTCompanion.Simulations;
using KARTCompanion.WowUtils;

namespace KARTCompanion;

/// <summary>
/// Owns the tray icon, its context menu, and the background sync timer. No main window — this
/// app lives entirely in the notification area.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly HttpClient _httpClient;
    private readonly IReadOnlyList<ISimReportFetcher> _simFetchers;
    private readonly NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.Timer _syncTimer = new();
    private readonly Bitmap _logo;
    private readonly Icon _appIcon;
    private readonly Icon _syncingIcon;
    private readonly Icon _errorIcon;

    private CompanionConfig _config;
    private readonly SyncGate _syncGate = new();
    private bool _errorShown;

    public TrayApplicationContext(HttpClient httpClient, IReadOnlyList<ISimReportFetcher> simFetchers)
    {
        _httpClient = httpClient;
        _simFetchers = simFetchers;
        _config = ConfigStore.Load();

        _logo = AppIcon.LoadLogoBitmap();
        _appIcon = AppIcon.CreateTrayIcon(_logo);
        _syncingIcon = AppIcon.CreateTrayIcon(_logo, Theme.Accent);
        _errorIcon = AppIcon.CreateTrayIcon(_logo, Theme.Error);

        if (string.IsNullOrWhiteSpace(_config.SavedVariablesFilePath))
        {
            var found = SavedVariablesLocator.ScanCommonInstallPaths();
            if (found.Count == 1)
            {
                _config.SavedVariablesFilePath = found[0];
                ConfigStore.Save(_config);
            }
        }

        var menu = new ContextMenuStrip
        {
            Renderer = new ToolStripProfessionalRenderer(new TrayMenuColorTable()),
            ForeColor = Theme.Text,
        };
        menu.Items.Add("Sync now", null, async (_, _) => await SyncNowAsync());
        menu.Items.Add("Open WoW folder", null, (_, _) => OpenWowFolder());
        menu.Items.Add("Settings...", null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _trayIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "KART Companion",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => OpenSettings();

        UpdateTooltip();

        _syncTimer.Tick += async (_, _) => await SyncNowAsync();
        ApplyIntervalToTimer();

        if (!_config.IsComplete)
        {
            OpenSettings();
        }
    }

    private void ApplyIntervalToTimer()
    {
        _syncTimer.Stop();
        if (!_config.AutoSyncEnabled) return;
        _syncTimer.Interval = Math.Max(1, _config.SyncIntervalMinutes) * 60 * 1000;
        _syncTimer.Start();
    }

    private void OpenSettings()
    {
        // Modal ShowDialog() still pumps timer ticks, so without this the background timer and
        // the dialog's own Force Sync button could sync concurrently.
        _syncTimer.Stop();
        try
        {
            using var form = new SettingsForm(_config, RunSyncWithConfigAsync, _logo, _appIcon);
            if (form.ShowDialog() == DialogResult.OK)
            {
                _config = form.Result;
                try
                {
                    ConfigStore.Save(_config);
                }
                catch (Exception ex)
                {
                    ShowUnexpectedError($"Failed to save settings: {ex.Message}");
                }
                UpdateTooltip();
            }
        }
        finally
        {
            ApplyIntervalToTimer();
        }
    }

    private void OpenWowFolder()
    {
        if (string.IsNullOrWhiteSpace(_config.SavedVariablesFilePath)) return;
        var dir = Path.GetDirectoryName(_config.SavedVariablesFilePath);
        if (dir is not null && Directory.Exists(dir))
        {
            System.Diagnostics.Process.Start("explorer.exe", dir);
        }
    }

    private async Task SyncNowAsync()
    {
        if (_syncGate.IsRunning) return;
        if (!_config.IsComplete)
        {
            OpenSettings();
            return;
        }

        _trayIcon.Text = "KART Companion — syncing...";
        _trayIcon.Icon = _syncingIcon;

        var result = await _syncGate.RunAsync(() => RunSyncWithConfigAsync(_config));
        if (result is null) return; // another sync was already in progress

        if (result.Success)
        {
            _errorShown = false;
            UpdateTooltip();
            if (result.SkippedCharacters > 0)
            {
                _trayIcon.BalloonTipTitle = "KART Companion";
                _trayIcon.BalloonTipText = $"Synced {result.PlayerCount} players ({result.SkippedCharacters} skipped — no readable sim data).";
                _trayIcon.ShowBalloonTip(4000);
            }
        }
        else
        {
            ShowError(result.ErrorMessage ?? "Unknown sync error.");
        }
    }

    // Shared by the tray "Sync now" menu item, the background timer, and the Settings dialog's
    // "Force Sync" button — all three just want "run a sync against this config and tell me what
    // happened" without duplicating the WowUtilsClient/SyncEngine wiring three times. Persists
    // config (LastSyncUtc) and adopts it as the live _config on success, same as the old
    // SyncNowAsync body did — the Settings dialog forcing a sync against its not-yet-saved field
    // values should still stick if it works.
    private async Task<SyncResult> RunSyncWithConfigAsync(CompanionConfig config)
    {
        try
        {
            var wowUtils = new WowUtilsClient(_httpClient, config.GroupKey!);
            var discovery = await wowUtils.GetDiscoveryAsync();

            var engine = new SyncEngine(wowUtils, _simFetchers, () => config, cfg => { _config = cfg; ConfigStore.Save(cfg); });
            return await engine.RunOnceAsync(discovery.Group.GroupId);
        }
        catch (Exception ex)
        {
            return new SyncResult(false, 0, 0, ex.Message);
        }
    }

    private void ShowError(string message)
    {
        _errorShown = true;
        _trayIcon.Icon = _errorIcon;
        _trayIcon.Text = "KART Companion — sync failed";
        _trayIcon.BalloonTipTitle = "KART Companion — sync failed";
        _trayIcon.BalloonTipText = message;
        _trayIcon.ShowBalloonTip(6000);
    }

    // Called from Program.cs's Application.ThreadException / AppDomain.UnhandledException
    // handlers so an unexpected crash shows a balloon instead of silently killing the tray app
    // (or, pre-.NET-8-WinForms-hardening, showing the default WinForms crash dialog).
    public void ShowUnexpectedError(string message)
    {
        _trayIcon.Text = "KART Companion — unexpected error";
        _trayIcon.BalloonTipTitle = "KART Companion — unexpected error";
        _trayIcon.BalloonTipText = message;
        _trayIcon.ShowBalloonTip(6000);
    }

    private void UpdateTooltip()
    {
        // Only reset to idle if nothing is syncing and no error is currently shown.
        // _syncGate.IsRunning guards against OpenSettings()'s UpdateTooltip() call clobbering a
        // *different*, still-in-flight sync's icon. _errorShown guards against the same
        // call clearing a red error dot just because Settings was saved — the spec requires
        // the error icon to persist until the next successful sync, not just until any
        // config save.
        if (!_syncGate.IsRunning && !_errorShown)
        {
            _trayIcon.Icon = _appIcon;
        }
        var last = _config.LastSyncUtc is { } t ? t.ToLocalTime().ToString("g") : "never";
        // NotifyIcon.Text has a 63-character limit.
        var text = $"KART Companion — last sync: {last}";
        _trayIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    private void ExitApp()
    {
        _trayIcon.Visible = false;
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Dispose();
            _syncTimer.Dispose();
            _appIcon.Dispose();
            _syncingIcon.Dispose();
            _errorIcon.Dispose();
            _logo.Dispose();
        }
        base.Dispose(disposing);
    }
}
