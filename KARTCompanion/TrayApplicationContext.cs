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
    private readonly NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.Timer _syncTimer = new();
    private readonly Bitmap _logo;
    private readonly Icon _appIcon;

    private CompanionConfig _config;
    private static readonly string CapturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "wowutils-loot-import.capture.json");
    private CompanionShell? _shell;

    public TrayApplicationContext(HttpClient httpClient, IReadOnlyList<ISimReportFetcher> simFetchers)
    {
        _httpClient = httpClient;
        _ = simFetchers;
        _config = ConfigStore.Load();

        _logo = AppIcon.LoadLogoBitmap();
        _appIcon = AppIcon.CreateTrayIcon(_logo);

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

        _syncTimer.Tick += (_, _) => ReadLootHistory(announceNothingNew: false);
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

        // The shell still pumps timer ticks while open. Stop the loot-history read until the
        // window closes, then FormClosed starts it again. The shell is modeless, so this method
        // returns immediately.
        _syncTimer.Stop();

        var settingsScreen = new SettingsScreen(_config, () => ReadLootHistory(announceNothingNew: true));
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
        bool uploadEnabled;
        try
        {
            uploadEnabled = LootHistoryImportCapture.Load(CapturePath).IsEnabled;
        }
        catch (Exception ex)
        {
            // A missing or unreadable capture fixture must not escape after _syncTimer.Stop()
            // above — that would leave automatic syncing off until restart. Upload stays disabled;
            // the window still opens so settings and the archive list remain usable.
            ShowUnexpectedError($"The import capture fixture could not be read: {ex.Message}");
            uploadEnabled = false;
        }

        // ArchiveStore.Load/Save, not a wrapper that answers an empty document on failure: the
        // screen saves through this after an export, and an empty document saved over the archive
        // would destroy the only copy of everything the game has already forgotten.
        var historyScreen = new HistoryScreen(
            archiveDoc.Awards,
            ArchiveStore.Load,
            ArchiveStore.Save,
            uploadEnabled: uploadEnabled,
            createImporter: () =>
            {
                if (string.IsNullOrWhiteSpace(_config.ImportCredential)) return null;
                return new LootHistoryImporter(_httpClient, LootHistoryImportCapture.Load(CapturePath), _config.ImportCredential);
            },
            refreshFromSavedVariables: () => ReadLootHistory(announceNothingNew: false),
            excludedUploadReasons: _config.UploadExcludedReasons,
            saveExcludedUploadReasons: reasons =>
            {
                _config.UploadExcludedReasons = reasons.ToList();
                ConfigStore.Save(_config);
            });

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

    public void ShowUnexpectedError(string message)
    {
        _trayIcon.Text = "KART Companion — unexpected error";
        _trayIcon.BalloonTipTitle = "KART Companion — unexpected error";
        _trayIcon.BalloonTipText = message;
        _trayIcon.ShowBalloonTip(6000);
    }

    private void UpdateTooltip()
    {
        _trayIcon.Icon = _appIcon;
        _trayIcon.Text = "KART Companion";
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
            _logo.Dispose();
        }
        base.Dispose(disposing);
    }
}
