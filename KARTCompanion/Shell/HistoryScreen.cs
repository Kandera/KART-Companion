using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using KARTCompanion.Archive;
using KARTCompanion.Export;
using KARTCompanion.SavedVariables;

namespace KARTCompanion.Shell;

/// <summary>
/// Browses the loot-history archive and exports a selection to WoWUtils. The archive is the one
/// place a withdrawn award (the addon deleted its own row) still exists, so it is always shown, never
/// hidden — just marked and excluded from anything exported.
///
/// The list is a virtual ListView: RetrieveVirtualItem answers one row at a time from whatever
/// ArchiveQuery.Filter last returned, so this screen's cost stays proportional to what is on screen,
/// not to how many thousand awards the archive has accumulated (see ArchivedAward's own remarks on
/// why nothing is ever deleted from it).
///
/// Exporting has one consequence no code here can remove: the Companion can stamp its own mark, but it
/// cannot reach into the addon's SavedVariables to set the addon's "exported" field (that needs part
/// 3's write-back). So a selection containing an award only the Companion has marked will, if exported
/// again from the addon, go to WoWUtils a second time — it does not dedup. That is stated on screen
/// rather than hidden or blocked (see UpdateFooterLabels).
/// </summary>
public sealed class HistoryScreen : IScreen
{
    private const int RailWidth = 64;
    private const int ContentLeft = RailWidth + 16;
    private const int ContentWidth = 820;

    // The display name inside a hyperlink's brackets: |c...|Hitem:...|h[Name]|h|r. Duplicated from
    // RcLootCouncilJsonWriter's own (private) pattern rather than exposing it there — this is a
    // display concern for the list, not part of what gets exported.
    private static readonly Regex ItemNamePattern = new(@"\[(.*?)\]", RegexOptions.Compiled);

    private readonly ArchiveDocument _doc;
    private readonly Panel _view;
    private readonly ListView _listView;
    private readonly ComboBox _playerCombo;
    private readonly TextBox _fromBox;
    private readonly TextBox _toBox;
    private readonly ComboBox _statusCombo;
    private readonly TextBox _searchBox;
    private readonly Label _emptyLabel;
    private readonly Label _countLabel;
    private readonly Label _noticeLabel;
    private readonly Label _resultLabel;

    private IReadOnlyList<ArchivedAward> _filtered = Array.Empty<ArchivedAward>();

    public string Title => "Loot History";
    public Theme.IconGlyph Glyph => Theme.IconGlyph.List;
    public Control View => _view;

    // This screen has no "health" concept the way Settings' sync status does — the rail dot simply
    // stays hidden for it (see IScreen's remarks: null means no dot). Custom add/remove (rather than a
    // plain field-like event) so the compiler does not flag it as CS0067 "event is never used": it
    // genuinely never fires, on purpose.
    public Color? StatusColor => null;
    public event EventHandler? StatusChanged { add { } remove { } }

    public HistoryScreen(ArchiveDocument doc)
    {
        _doc = doc;
        _view = new Panel();

        // --- filter row ---
        var playerLabel = FilterLabel("Player", ContentLeft);
        _playerCombo = FilterCombo(ContentLeft, 150);

        var fromLabel = FilterLabel("From", 240);
        _fromBox = FilterTextBox(240, 110);

        var toLabel = FilterLabel("To", 360);
        _toBox = FilterTextBox(360, 110);

        var statusLabel = FilterLabel("Status", 480);
        _statusCombo = FilterCombo(480, 170);

        var searchLabel = FilterLabel("Search", 660);
        _searchBox = FilterTextBox(660, 210);

        // --- the list itself ---
        _listView = new SmoothListView
        {
            Left = ContentLeft,
            Top = 148,
            Width = ContentWidth,
            Height = 420,
            View = System.Windows.Forms.View.Details,
            VirtualMode = true,
            OwnerDraw = true,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            GridLines = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _listView.Columns.Add("Time", 130);
        _listView.Columns.Add("Player", 110);
        _listView.Columns.Add("Item", 230);
        _listView.Columns.Add("Reason", 120);
        _listView.Columns.Add("Difficulty", 100);
        _listView.Columns.Add("Status", 130);
        _listView.RetrieveVirtualItem += (_, e) => e.Item = BuildRow(_filtered[e.ItemIndex]);
        _listView.DrawColumnHeader += DrawHeader;
        _listView.DrawItem += (_, e) => e.DrawDefault = false;
        _listView.DrawSubItem += DrawRow;
        _listView.SelectedIndexChanged += (_, _) => UpdateFooterLabels();

        _emptyLabel = new Label
        {
            Left = _listView.Left,
            Top = _listView.Top,
            Width = _listView.Width,
            Height = _listView.Height,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "No awards recorded yet — the archive fills in as loot is synced.",
        };
        Theme.StyleLabel(_emptyLabel, dim: true);

        // --- footer: actions + the lines the brief asks for ---
        var copyButton = Theme.CreateButton("Copy for WoWUtils", primary: true);
        copyButton.Left = ContentLeft;
        copyButton.Top = 580;
        copyButton.Width = 170;
        copyButton.Click += (_, _) => OnCopy();

        var saveButton = Theme.CreateButton("Save a copy…");
        saveButton.Left = copyButton.Right + 10;
        saveButton.Top = 580;
        saveButton.Width = 140;
        saveButton.Click += (_, _) => OnSaveCopy();

        _countLabel = FooterLabel(622);
        _noticeLabel = FooterLabel(644);
        _resultLabel = FooterLabel(666);

        _view.Controls.AddRange(new Control[]
        {
            playerLabel, _playerCombo, fromLabel, _fromBox, toLabel, _toBox,
            statusLabel, _statusCombo, searchLabel, _searchBox,
            _listView, _emptyLabel,
            copyButton, saveButton, _countLabel, _noticeLabel, _resultLabel,
        });

        // Controls.AddRange leaves _listView in front of _emptyLabel (added right after it, but
        // WinForms z-order puts index 0 in front, not the other way round) — without this, the
        // opaque ListView paints over the label and an empty archive shows a blank rectangle instead
        // of the "No awards recorded yet" text. Confirmed with a real window and a pixel capture of
        // the list's body area (see task-4-report.md): before this line, bodyDistinctColors=1,
        // bodyTextPixels=0; after it, both are non-trivial.
        _emptyLabel.BringToFront();

        PopulateFilterOptions();
        RunFilter();

        _view.Size = new Size(ContentLeft + ContentWidth + 12, 700);
    }

    // ---- control factories (kept tiny and local — there is no Theme helper for a plain filter
    // field, and adding one to Theme.cs for five call sites here is not this task's job) ----

    private static Label FilterLabel(string text, int left)
    {
        var label = new Label { Text = text, Left = left, Top = 92, AutoSize = true };
        Theme.StyleLabel(label, dim: true);
        return label;
    }

    private static ComboBox FilterCombo(int left, int width) => new()
    {
        Left = left,
        Top = 110,
        Width = width,
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Panel,
        ForeColor = Theme.Text,
    };

    private static TextBox FilterTextBox(int left, int width) => new()
    {
        Left = left,
        Top = 110,
        Width = width,
        BackColor = Theme.Panel,
        ForeColor = Theme.Text,
        BorderStyle = BorderStyle.FixedSingle,
    };

    private Label FooterLabel(int top)
    {
        var label = new Label
        {
            Left = ContentLeft,
            Top = top,
            Width = ContentWidth,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
        };
        Theme.StyleLabel(label, dim: true);
        return label;
    }

    // A small ListView subclass purely to flip on double buffering (a protected Control property
    // ListView does not expose itself) — without it, an owner-drawn list of a few thousand rows
    // flickers noticeably while scrolling.
    private sealed class SmoothListView : ListView
    {
        public SmoothListView() => DoubleBuffered = true;
    }

    private void PopulateFilterOptions()
    {
        _playerCombo.Items.Add("All players");
        var players = _doc.Awards
            .Select(a => new LootHistoryEntry(a.Fields).Winner)
            .Where(w => !string.IsNullOrEmpty(w))
            .Distinct()
            .OrderBy(w => w, StringComparer.OrdinalIgnoreCase);
        foreach (var p in players) _playerCombo.Items.Add(p!);
        _playerCombo.SelectedIndex = 0;
        _playerCombo.SelectedIndexChanged += (_, _) => RunFilter();

        _statusCombo.Items.Add("All statuses");
        foreach (var s in HistoryExportPlanner.StatusOptions) _statusCombo.Items.Add(StatusDisplay(s));
        _statusCombo.SelectedIndex = 0;
        _statusCombo.SelectedIndexChanged += (_, _) => RunFilter();

        _searchBox.TextChanged += (_, _) => RunFilter();
        _fromBox.TextChanged += (_, _) => RunFilter();
        _toBox.TextChanged += (_, _) => RunFilter();
    }

    // Re-runs ArchiveQuery.Filter and resets VirtualListSize — called on every filter control change
    // and after a successful export (to move the status column). Selection is cleared rather than
    // carried over: the row indices a virtual ListView tracks would otherwise point at whatever award
    // happens to land on that index in the new, differently-filtered result.
    private void RunFilter()
    {
        _listView.SelectedIndices.Clear();

        var player = _playerCombo.SelectedIndex > 0 ? _playerCombo.SelectedItem as string : null;
        var status = HistoryExportPlanner.StatusForComboIndex(_statusCombo.SelectedIndex);
        var from = HistoryExportPlanner.ParseFromDate(_fromBox.Text);
        var to = HistoryExportPlanner.ParseToDate(_toBox.Text);
        var search = string.IsNullOrWhiteSpace(_searchBox.Text) ? null : _searchBox.Text.Trim();

        _filtered = ArchiveQuery.Filter(_doc.Awards, player, from, to, status, search);
        _listView.VirtualListSize = _filtered.Count;
        _emptyLabel.Visible = _filtered.Count == 0;
        _listView.Invalidate();
        UpdateFooterLabels();
    }

    private ListViewItem BuildRow(ArchivedAward award)
    {
        var entry = new LootHistoryEntry(award.Fields);
        var status = ArchiveQuery.StatusOf(award);
        return new ListViewItem(new[]
        {
            FormatTime(entry.Time),
            entry.Winner ?? "",
            ItemDisplayName(entry.Item),
            entry.Reason ?? "",
            DifficultyDisplay(entry),
            StatusDisplay(status),
        });
    }

    private static string FormatTime(long unixSeconds) =>
        unixSeconds == 0
            ? ""
            : DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string DifficultyDisplay(LootHistoryEntry e) =>
        e.Difficulty ?? e.DifficultyId?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static string ItemDisplayName(string? link)
    {
        if (string.IsNullOrEmpty(link)) return "";
        var m = ItemNamePattern.Match(link);
        return m.Success ? m.Groups[1].Value : link;
    }

    private static string StatusDisplay(AwardStatus status) => status switch
    {
        AwardStatus.Open => "open",
        AwardStatus.ExportedByAddon => "exported (addon)",
        AwardStatus.ExportedByCompanion => "exported (companion)",
        AwardStatus.ExportedByBoth => "exported (both)",
        AwardStatus.Withdrawn => "withdrawn",
        _ => status.ToString(),
    };

    // Windows' own ListView ignores BackColor for the column header, so this repaints it by hand to
    // match the dark theme instead of showing a bright native header above a dark body.
    private void DrawHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using (var backBrush = new SolidBrush(Theme.RailBackground))
            e.Graphics.FillRectangle(backBrush, e.Bounds);
        var bounds = e.Bounds;
        bounds.X += 6;
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", _listView.Font, bounds, Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        using var pen = new Pen(Theme.BorderStrong);
        e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        e.DrawDefault = false;
    }

    // Withdrawn awards are shown, not hidden — they are the only remaining record that an award was
    // taken back — but set apart with Theme.TextDim, same as their "withdrawn" status text.
    private void DrawRow(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (e.ItemIndex < 0 || e.ItemIndex >= _filtered.Count) { e.DrawDefault = false; return; }

        var award = _filtered[e.ItemIndex];
        var selected = e.Item?.Selected ?? false;
        var back = selected ? Theme.AccentDim : Theme.Panel;
        using (var backBrush = new SolidBrush(back))
            e.Graphics.FillRectangle(backBrush, e.Bounds);

        var fore = award.Withdrawn ? Theme.TextDim : Theme.Text;
        var bounds = e.Bounds;
        bounds.X += 6;
        bounds.Width -= 6;
        TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", _listView.Font, bounds, fore,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        e.DrawDefault = false;
    }

    private List<ArchivedAward> SelectedAwards() =>
        _listView.SelectedIndices.Cast<int>()
            .Where(i => i >= 0 && i < _filtered.Count)
            .Select(i => _filtered[i])
            .ToList();

    // The set Copy/Save would actually act on right now: the selection, or everything shown when
    // nothing is selected, always minus withdrawn awards. Shared by both buttons and by the footer
    // labels so what the labels describe and what a press does can never drift apart.
    private IReadOnlyList<ArchivedAward> PrepareExport()
    {
        var effective = HistoryExportPlanner.EffectiveSelection(_filtered, SelectedAwards());
        return HistoryExportPlanner.AwardsToExport(effective);
    }

    private enum ResultKind { Neutral, Success, Error }

    private void SetResult(string text, ResultKind kind)
    {
        _resultLabel.Text = text;
        _resultLabel.ForeColor = kind switch
        {
            ResultKind.Success => Theme.Success,
            ResultKind.Error => Theme.Error,
            _ => Theme.TextDim,
        };
    }

    // Copy for WoWUtils: clipboard first (that is the actual export the user asked for), then the
    // Companion's own record of it. The record is built by HistoryExportPlanner.StampAndSave, which
    // reloads the archive from disk before stamping rather than writing back the snapshot this window
    // opened with — the shell is modeless and the tray's "Read loot history now" / background sync
    // hold their own separate ArchiveDocument and can Save a newer file while this window sits open.
    // Writing this window's stale copy back over that would silently erase whatever merged in since
    // (new awards, new Withdrawn marks) — the one file that exists precisely because the game has
    // already forgotten that data. _doc is only updated — and only its Awards, not replaced wholesale
    // — once the reload-stamp-save has actually succeeded; on failure _doc is untouched, so there is
    // nothing to roll back.
    private void OnCopy()
    {
        var toExport = PrepareExport();
        if (toExport.Count == 0)
        {
            // Clipboard.SetText throws on an empty string — rather than let that surface as an
            // unhandled exception, an empty export is just not sent to the clipboard at all.
            SetResult("Nothing to export.", ResultKind.Neutral);
            return;
        }

        var json = RcLootCouncilJsonWriter.Write(toExport.Select(a => new LootHistoryEntry(a.Fields)));
        try
        {
            Clipboard.SetText(json);
        }
        catch (ExternalException ex)
        {
            // A clipboard manager, an RDP session, or another app can transiently hold the clipboard
            // open (CLIPBRD_E_CANT_OPEN); WinForms already retries internally, but if it still fails
            // this reports it the same way OnSaveCopy reports a failed file write, instead of letting
            // it escape as a generic crash balloon.
            SetResult($"Could not copy to the clipboard: {ex.Message}", ResultKind.Error);
            return;
        }

        var keys = toExport.Select(a => a.Key).ToList();
        ArchiveDocument updated;
        try
        {
            updated = HistoryExportPlanner.StampAndSave(keys, DateTimeOffset.UtcNow, ArchiveStore.Load, ArchiveStore.Save);
        }
        catch (Exception ex)
        {
            SetResult($"Copied {toExport.Count} award(s), but the export could not be recorded: {ex.Message}. Exporting again from the addon may send them a second time.", ResultKind.Error);
            return;
        }

        _doc.Awards = updated.Awards;
        SetResult($"Copied {toExport.Count} award(s) to the clipboard and marked them exported.", ResultKind.Success);
        RunFilter();
    }

    // A copy for keeping, not the export of record — writes the same text to a file but never
    // touches ExportedByCompanionAt.
    private void OnSaveCopy()
    {
        var toExport = PrepareExport();
        if (toExport.Count == 0)
        {
            SetResult("Nothing to export.", ResultKind.Neutral);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"loot-history-{DateTime.Now:yyyyMMdd-HHmmss}.json",
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;

        var json = RcLootCouncilJsonWriter.Write(toExport.Select(a => new LootHistoryEntry(a.Fields)));
        try
        {
            File.WriteAllText(dialog.FileName, json);
            SetResult($"Saved {toExport.Count} award(s) to {dialog.FileName}.", ResultKind.Success);
        }
        catch (Exception ex)
        {
            SetResult($"Could not save the file: {ex.Message}", ResultKind.Error);
        }
    }

    // The count line the brief asks for, plus the one thing no code can fix: exporting again from the
    // addon will re-send anything only the Companion has marked, because WoWUtils does not dedup and
    // the Companion cannot set the addon's own mark (that needs part 3's write-back).
    private void UpdateFooterLabels()
    {
        var selected = SelectedAwards();
        var effective = HistoryExportPlanner.EffectiveSelection(_filtered, selected);
        var exportable = HistoryExportPlanner.AwardsToExport(effective);

        _countLabel.Text = selected.Count > 0
            ? $"{selected.Count} award(s) selected ({exportable.Count} exportable)."
            : $"No selection — Copy exports all {exportable.Count} shown award(s).";

        _noticeLabel.Text = HistoryExportPlanner.ContainsCompanionOnlyExport(effective)
            ? "Some of these were already exported by the Companion but not the addon — exporting again from the addon will send them a second time; WoWUtils does not dedup."
            : "";
    }
}

/// <summary>
/// The decision logic behind HistoryScreen's two export buttons, pulled out so it can be tested
/// without constructing a Form — see HistoryScreen's own remarks on why nothing else there has
/// automated coverage.
/// </summary>
public static class HistoryExportPlanner
{
    /// <summary>The AwardStatus values in the order the status filter dropdown lists them after its
    /// "All statuses" sentinel at index 0 — shared by HistoryScreen (to populate the dropdown) and by
    /// StatusForComboIndex (to read it back), so the two can never drift apart into an off-by-one.</summary>
    public static readonly AwardStatus[] StatusOptions = Enum.GetValues<AwardStatus>();

    /// <summary>What a press of either export button would act on right now: the current selection,
    /// or everything the filter shows when nothing is selected.</summary>
    public static IReadOnlyList<ArchivedAward> EffectiveSelection(
        IReadOnlyList<ArchivedAward> filtered, IReadOnlyList<ArchivedAward> selected) =>
        selected.Count > 0 ? selected : filtered;

    /// <summary>The awards Copy/Save actually send: the effective selection minus every withdrawn
    /// award. A withdrawn award is never exported no matter what was selected — the addon already
    /// deleted its own copy of it.</summary>
    public static IReadOnlyList<ArchivedAward> AwardsToExport(IReadOnlyList<ArchivedAward> effectiveSelection) =>
        effectiveSelection.Where(a => !a.Withdrawn).ToList();

    /// <summary>True when the effective selection holds an award the Companion has marked exported
    /// but the addon has not — the one case exporting again from the addon would resend, since
    /// WoWUtils does not dedup and the Companion cannot set the addon's own mark.</summary>
    public static bool ContainsCompanionOnlyExport(IReadOnlyList<ArchivedAward> effectiveSelection) =>
        effectiveSelection.Any(a => ArchiveQuery.StatusOf(a) == AwardStatus.ExportedByCompanion);

    /// <summary>Maps the status dropdown's SelectedIndex back to the AwardStatus it represents, or
    /// null for index 0 ("All statuses"). Index 0 is the sentinel, so the lookup into StatusOptions is
    /// offset by one — exactly the arithmetic that would silently show the wrong status class if it
    /// were ever off by one, with nothing visibly wrong to notice.</summary>
    public static AwardStatus? StatusForComboIndex(int selectedIndex) =>
        selectedIndex > 0 ? StatusOptions[selectedIndex - 1] : null;

    /// <summary>Parses a "From" field as the start of the typed day, local time, or null if the text
    /// doesn't parse. Free-text rather than a native DateTimePicker — see HistoryScreen's own remarks
    /// on why.</summary>
    public static DateTimeOffset? ParseFromDate(string text) => ParseDate(text);

    /// <summary>Parses a "To" field as the END of the typed day (23:59:59.9999999 local), not the
    /// instant at midnight — "to 2026-08-08" means include everything that happened ON the 8th, which
    /// is what a human typing a date means, not "up to the very start of it".</summary>
    public static DateTimeOffset? ParseToDate(string text) => ParseDate(text)?.AddDays(1).AddTicks(-1);

    private static DateTimeOffset? ParseDate(string text) =>
        DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d)
            ? new DateTimeOffset(DateTime.SpecifyKind(d.Date, DateTimeKind.Local))
            : null;

    /// <summary>
    /// Stamps ExportedByCompanionAt on the awards named by <paramref name="keysToStamp"/>, in a
    /// document fetched fresh via <paramref name="load"/> — not in whatever document the caller
    /// already had — then saves that document via <paramref name="save"/> and returns it.
    ///
    /// This is what keeps a Copy from a modeless, long-open history window from overwriting work it
    /// never saw: the shell stays open while the tray's background sync or "Read loot history now"
    /// runs against their own separate ArchiveDocument and can Save a newer file in the meantime.
    /// Reloading immediately before stamping means whatever they added — new awards, new Withdrawn
    /// marks — is what gets saved back, with only the requested keys touched on top of it.
    ///
    /// If <paramref name="save"/> throws, the exception propagates and the freshly-loaded, stamped
    /// document this method built is simply discarded with it — there is nothing to roll back because
    /// nothing durable, and nothing the caller already held, was ever touched.
    /// </summary>
    public static ArchiveDocument StampAndSave(
        IReadOnlyCollection<string> keysToStamp, DateTimeOffset stampedAt,
        Func<ArchiveDocument> load, Action<ArchiveDocument> save)
    {
        var fresh = load();
        var keys = new HashSet<string>(keysToStamp);
        // The archive never deletes a row (see ArchivedAward's own remarks) — every key passed in
        // was read from an award that came out of this same file, so it cannot be missing here.
        foreach (var award in fresh.Awards)
        {
            if (keys.Contains(award.Key)) award.ExportedByCompanionAt = stampedAt;
        }

        save(fresh);
        return fresh;
    }
}
