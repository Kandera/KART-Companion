using KARTCompanion.Archive;
using KARTCompanion.Config;
using KARTCompanion.SavedVariables;
using KARTCompanion.Shell;
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
    private CompanionShell? _shell;

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
        menu.Items.Add("Read loot history now", null, (_, _) => ReadLootHistory(announceNothingNew: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _trayIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "KART Companion",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => BringShellForward();

        UpdateTooltip();

        _syncTimer.Tick += async (_, _) =>
        {
            await SyncNowAsync();
            ReadLootHistory(announceNothingNew: false);
        };
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

    // The tray icon's double-click, distinct from the "Settings..." menu entry: it must only bring
    // whichever screen is already showing forward, not force the shell back to Settings — that used
    // to route through OpenSettings' Show(0) and silently discard, say, a History selection the user
    // was in the middle of making. Falls back to OpenSettings only when there is no shell yet, since
    // that is also what creates one.
    private void BringShellForward()
    {
        if (_shell is not null)
        {
            _shell.Show();
            _shell.Activate();
            return;
        }
        OpenSettings();
    }

    private void OpenSettings()
    {
        // Opening it twice must focus the existing window, not make a second — the shell is
        // shown modeless (see below) so a re-entrant call here (e.g. the tray menu clicked again
        // while it's already open) just brings the existing one forward. Show(0) rather than a bare
        // Activate(): this is the "Settings..." menu item, and a window left on the History screen
        // used to come forward still showing History, which answers a request for settings with a
        // list of loot.
        if (_shell is not null)
        {
            _shell.Show(0);
            return;
        }

        // The shell still pumps timer ticks while open, so without this the background timer and
        // the screen's own Force Sync button could sync concurrently. Previously undone in a
        // finally around the (modal) ShowDialog() call; now undone from FormClosed instead, since
        // the shell is modeless and this method returns immediately.
        _syncTimer.Stop();

        var settingsScreen = new SettingsScreen(_config, RunSyncWithConfigAsync);
        settingsScreen.Saved += config =>
        {
            _config = config;
            try
            {
                ConfigStore.Save(_config);
            }
            catch (Exception ex)
            {
                ShowUnexpectedError($"Failed to save settings: {ex.Message}");
            }
            UpdateTooltip();
        };

        ArchiveDocument archiveDoc;
        try
        {
            archiveDoc = ArchiveStore.Load();
        }
        catch (ArchiveUnreadableException ex)
        {
            Notify($"The loot history archive could not be read and was kept at {ex.QuarantinePath}. A new one was started.");
            archiveDoc = new ArchiveDocument();
        }
        catch (Exception ex)
        {
            // Anything else — the file is there and readable but the directory is denied, the disk
            // errors, a bug in Load — used to escape this method. It escaped after _syncTimer.Stop()
            // above and before the FormClosed handler that restarts it was attached, so the window
            // never opened AND automatic syncing was silently off until the app was restarted. The
            // window opens on an empty list instead, saying why; nothing is written from it (the
            // export path re-reads the archive itself and will fail the same way, loudly).
            ShowUnexpectedError($"The loot history archive could not be opened: {ex.Message}");
            archiveDoc = new ArchiveDocument();
        }
        // ArchiveStore.Load/Save, not a wrapper that answers an empty document on failure: the
        // screen saves through this after an export, and an empty document saved over the archive
        // would destroy the only copy of everything the game has already forgotten.
        var historyScreen = new HistoryScreen(archiveDoc.Awards, ArchiveStore.Load, ArchiveStore.Save);

        _shell = new CompanionShell(new IScreen[] { settingsScreen, historyScreen }, _logo, _appIcon);
        _shell.FormClosed += (_, _) =>
        {
            _shell = null;
            ApplyIntervalToTimer();
        };
        _shell.Show(0);
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

    /// <summary>
    /// Reads every saved-variables file whose last-write time has moved since we last read it, and
    /// folds each into the archive.
    ///
    /// Timestamp comparison, not a file watcher and not process watching. What we need to know is
    /// "has the game written a new state", and a timestamp answers it without naming, enumerating or
    /// opening a handle on the game process — the last of which is a common anti-cheat heuristic,
    /// because it is how every memory cheat begins.
    ///
    /// Merging is idempotent because every award carries a stable key, so reading three times during
    /// an evening or once after it produces the same archive.
    /// </summary>
    private void ReadLootHistory(bool announceNothingNew)
    {
        // WowInstallPath is what the user browsed to in Settings — the folder containing "_retail_",
        // which is exactly what FindSavedVariablesFiles takes. No path arithmetic off the file path.
        var files = string.IsNullOrWhiteSpace(_config.WowInstallPath)
            ? SavedVariablesLocator.ScanCommonInstallPaths()
            : SavedVariablesLocator.FindSavedVariablesFiles(_config.WowInstallPath);

        // Reading is not writing: a second Battle.net account is still the same person's loot, so
        // all of them are read. But say so rather than merging silently — on a shared machine this
        // would put somebody else's awards into the maintainer's WoWUtils import. Carried as a note
        // on whatever balloon this pass ends up showing rather than fired as its own: on its own it
        // was shown on every single tick, so a two-account user got it every sync interval forever.
        var accountsNote = files.Count > 1
            ? $" Read from {files.Count} account folders."
            : "";

        ArchiveDocument doc;
        try
        {
            doc = ArchiveStore.Load();
        }
        catch (ArchiveUnreadableException ex)
        {
            Notify($"The loot history archive could not be read and was kept at {ex.QuarantinePath}. A new one was started.");
            doc = new ArchiveDocument();
        }

        var total = new MergeResult(0, 0, 0, 0);
        var unreadable = 0;
        // The read stamps are held back until the archive is safely on disk. Recording them inside
        // the loop meant that a failing ArchiveStore.Save left the stamps standing, so the file was
        // skipped on every later tick until WoW rewrote it — and every award the cap evicted in
        // that window was gone for good.
        var pendingStamps = new Dictionary<string, DateTimeOffset>();
        // Stamp of every file that failed to read this pass, so the notification decision below can
        // tell "still unreadable, already told the user" from "unreadable again with new content".
        var unreadableStamps = new Dictionary<string, DateTimeOffset>();

        foreach (var file in files)
        {
            var stamp = new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);
            if (_config.LootHistoryReadAt.TryGetValue(file, out var last) && last == stamp) continue;

            IReadOnlyList<LootHistoryEntry> entries;
            try
            {
                entries = LootHistoryReader.Read(File.ReadAllText(file));
            }
            catch (Exception)
            {
                // A half-written or unfamiliar file contributes nothing. Do NOT record the stamp:
                // the next tick should try again once the game has finished writing.
                //
                // But count it and say so. Swallowed silently, a shape the reader rejects — a
                // future WoW build, a future addon version — makes every pass fail, every failure
                // invisible, and the user is told "nothing new" by the one feature whose entire
                // purpose is not losing data.
                unreadable++;
                unreadableStamps[file] = stamp;
                continue;
            }

            var result = ArchiveMerger.Merge(doc, entries, file, DateTimeOffset.UtcNow);
            total = new MergeResult(total.Added + result.Added, total.Updated + result.Updated,
                                    total.Withdrawn + result.Withdrawn, total.Skipped + result.Skipped);
            pendingStamps[file] = stamp;
        }

        // A file the reader keeps rejecting fails on every tick forever, since its read stamp is
        // never advanced (see above). Reporting that unconditionally on the automatic tick meant a
        // balloon on every single sync interval, indefinitely — the same nag shape as M5. Report a
        // given file's failure once, and again only once its content actually changes (new
        // last-write stamp) and still fails; that is new information, not a repeat. Manual "Read
        // loot history now" bypasses this and always reports, per Important 4.
        var newlyUnreadable = false;
        foreach (var (file, stamp) in unreadableStamps)
        {
            if (!_config.LootHistoryUnreadableNotifiedAt.TryGetValue(file, out var notifiedAt) || notifiedAt != stamp)
            {
                newlyUnreadable = true;
                break;
            }
        }
        var reportUnreadable = unreadable > 0 && (announceNothingNew || newlyUnreadable);

        if (pendingStamps.Count == 0)
        {
            if (reportUnreadable)
            {
                foreach (var (file, stamp) in unreadableStamps) _config.LootHistoryUnreadableNotifiedAt[file] = stamp;
                ConfigStore.Save(_config);
                Notify($"Could not read {unreadable} saved-variables file(s); nothing was archived. It will be retried.{accountsNote}");
            }
            else if (announceNothingNew)
                Notify("No new loot history to read." + accountsNote);
            return;
        }

        ArchiveStore.Save(doc);
        foreach (var (file, stamp) in pendingStamps) _config.LootHistoryReadAt[file] = stamp;
        foreach (var (file, stamp) in unreadableStamps) _config.LootHistoryUnreadableNotifiedAt[file] = stamp;
        ConfigStore.Save(_config);
        // Skipped and unreadable are surfaced alongside Added rather than left to a log: a silent
        // skip is exactly the shape of defect this project keeps finding. A user who updates the
        // addon and later sees awards being skipped should be able to tell why from this balloon.
        var notes = new List<string>();
        if (total.Skipped > 0) notes.Add($"{total.Skipped} skipped — no id");
        if (unreadable > 0) notes.Add($"{unreadable} unreadable — will retry");
        var note = notes.Count > 0 ? $" ({string.Join("; ", notes)})" : "";
        Notify($"Archived {total.Added} new awards{note} ({doc.Awards.Count} in total).{accountsNote}");
    }

    private void Notify(string message)
    {
        _trayIcon.BalloonTipTitle = "KART Companion";
        _trayIcon.BalloonTipText = message;
        _trayIcon.ShowBalloonTip(4000);
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
