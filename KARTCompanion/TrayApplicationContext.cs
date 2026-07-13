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

    private CompanionConfig _config;
    private bool _syncing;

    public TrayApplicationContext(HttpClient httpClient, IReadOnlyList<ISimReportFetcher> simFetchers)
    {
        _httpClient = httpClient;
        _simFetchers = simFetchers;
        _config = ConfigStore.Load();

        if (string.IsNullOrWhiteSpace(_config.SavedVariablesFilePath))
        {
            var found = SavedVariablesLocator.ScanCommonInstallPaths();
            if (found.Count == 1)
            {
                _config.SavedVariablesFilePath = found[0];
                ConfigStore.Save(_config);
            }
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add("Sync now", null, async (_, _) => await SyncNowAsync());
        menu.Items.Add("Open WoW folder", null, (_, _) => OpenWowFolder());
        menu.Items.Add("Settings...", null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _trayIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
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
        _syncTimer.Interval = Math.Max(1, _config.SyncIntervalMinutes) * 60 * 1000;
        _syncTimer.Start();
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_config);
        if (form.ShowDialog() == DialogResult.OK)
        {
            _config = form.Result;
            ConfigStore.Save(_config);
            ApplyIntervalToTimer();
            UpdateTooltip();
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
        if (_syncing) return;
        if (!_config.IsComplete)
        {
            OpenSettings();
            return;
        }

        _syncing = true;
        _trayIcon.Icon = System.Drawing.SystemIcons.Information;
        _trayIcon.Text = "KART Companion — syncing...";

        try
        {
            var wowUtils = new WowUtilsClient(_httpClient, _config.GroupKey!);
            var discovery = await wowUtils.GetDiscoveryAsync();

            var engine = new SyncEngine(wowUtils, _simFetchers, () => _config, cfg => { _config = cfg; ConfigStore.Save(cfg); });
            var result = await engine.RunOnceAsync(discovery.Group.GroupId);

            if (result.Success)
            {
                _trayIcon.Icon = System.Drawing.SystemIcons.Application;
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
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void ShowError(string message)
    {
        _trayIcon.Icon = System.Drawing.SystemIcons.Warning;
        _trayIcon.Text = "KART Companion — sync failed";
        _trayIcon.BalloonTipTitle = "KART Companion — sync failed";
        _trayIcon.BalloonTipText = message;
        _trayIcon.ShowBalloonTip(6000);
    }

    private void UpdateTooltip()
    {
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
        }
        base.Dispose(disposing);
    }
}
