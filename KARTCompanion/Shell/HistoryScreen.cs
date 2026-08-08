using System.Globalization;
using System.Runtime.InteropServices;
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
/// cannot reach into the addon's SavedVariables to set the addon's "exported" field — writing back into
/// the game's files was examined and deliberately dropped (see the design spec), not deferred. So a
/// selection containing an award only the Companion has marked will, if exported
/// again from the addon, go to WoWUtils a second time — it does not dedup. That is stated on screen
/// rather than hidden or blocked (see UpdateFooter).
///
/// This screen deliberately holds no ArchiveDocument. What it keeps is a display snapshot — the list
/// of awards it last drew — and the archive itself is re-read for every decision that has consequences
/// (see OnCopy/OnSaveCopy and HistoryExportPlanner.ExportAndStamp). The shell is modeless: the tray's
/// "Read loot history now" and the background sync merge into their own documents and save while this
/// window sits open, so a document held here would be stale from the moment it was handed over, and
/// deciding from it is what once put a since-withdrawn award on the clipboard.
/// </summary>
public sealed class HistoryScreen : IScreen
{
    private const int RailWidth = 64;
    private const int ContentLeft = RailWidth + 16;
    private const int ContentWidth = 950;

    private readonly Func<ArchiveDocument> _loadArchive;
    private readonly Action<ArchiveDocument> _saveArchive;
    private readonly Panel _view;
    private readonly ListView _listView;
    private readonly ComboBox _playerCombo;
    private readonly TextBox _fromBox;
    private readonly TextBox _toBox;
    private readonly ComboBox _statusCombo;
    private readonly TextBox _searchBox;
    private readonly Label _emptyLabel;
    private readonly Label _dateWarningLabel;
    private readonly Label _countLabel;
    private readonly Label _noticeLabel;
    private readonly Label _resultLabel;
    private readonly Button _copyButton;
    private readonly Button _saveButton;
    private readonly Button _editButton;

    private IReadOnlyList<ArchivedAward> _awards;
    private IReadOnlyList<ArchivedAward> _filtered = Array.Empty<ArchivedAward>();
    private bool _suspendFilter;

    public string Title => "Loot History";
    public Theme.IconGlyph Glyph => Theme.IconGlyph.List;
    public Control View => _view;

    // Enter and Escape belong to whichever screen is showing. They used to be wired to the Settings
    // screen's OK and Cancel for the lifetime of the window, so Enter here saved settings and closed
    // the window from a screen that has no OK button on it (see CompanionShell.SwitchTo). Nothing on
    // this screen is safe to trigger by pressing Enter — Copy stamps an export — so it offers none.
    public IButtonControl? AcceptButton => null;
    public IButtonControl? CancelButton => null;

    /// Navigating to this screen re-reads the archive. What is on screen is a snapshot of a file the
    /// tray's "Read loot history now" and the background sync write while this window is open, and a
    /// snapshot taken when the window opened stayed on screen for the entire session.
    public void OnShown() => ReloadFromArchive();

    // This screen has no "health" concept the way Settings' sync status does — the rail dot simply
    // stays hidden for it (see IScreen's remarks: null means no dot). Custom add/remove (rather than a
    // plain field-like event) so the compiler does not flag it as CS0067 "event is never used": it
    // genuinely never fires, on purpose.
    public Color? StatusColor => null;
    public event EventHandler? StatusChanged { add { } remove { } }

    /// <param name="awards">What to draw before the archive is first re-read — the caller has just
    /// loaded it, so re-reading it here would be a second parse of the same file for nothing.</param>
    /// <param name="loadArchive">Re-reads the archive from disk. Must be the real load, not a
    /// fallback that answers an empty document on failure: this is also what an export saves back
    /// over, and saving an empty document over the one file the game has already forgotten is the
    /// worst outcome this program has.</param>
    /// <param name="saveArchive">Persists the archive after an export has been stamped.</param>
    public HistoryScreen(
        IReadOnlyList<ArchivedAward> awards, Func<ArchiveDocument> loadArchive, Action<ArchiveDocument> saveArchive)
    {
        _awards = awards;
        _loadArchive = loadArchive;
        _saveArchive = saveArchive;
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

        // Sits between the filter row and the list, where the field it is talking about is. An
        // unparsable date is treated as no filter at all — without this the only sign of a typo was
        // that the list did not narrow, which looks exactly like a date that matched everything.
        _dateWarningLabel = new Label
        {
            Left = ContentLeft,
            Top = 136,
            Width = ContentWidth,
            AutoSize = false,
            Height = 14,
            Font = new Font(_view.Font.FontFamily, 8f),
            ForeColor = Theme.Error,
        };

        // --- the list itself ---
        _listView = new SmoothListView
        {
            Left = ContentLeft,
            Top = 154,
            Width = ContentWidth,
            Height = 414,
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
        _listView.Columns.Add("Raid", 230);
        _listView.Columns.Add("Status", 130);
        _listView.RetrieveVirtualItem += (_, e) => e.Item = BuildRow(_filtered[e.ItemIndex]);
        _listView.DrawColumnHeader += DrawHeader;
        _listView.DrawItem += (_, e) => e.DrawDefault = false;
        _listView.DrawSubItem += DrawRow;
        _listView.SelectedIndexChanged += (_, _) => UpdateFooter();
        // Selection first, then the row: in a virtual ListView a ListViewItem handed out by
        // RetrieveVirtualItem is not in the Items collection, so its Index is not dependable. A
        // double-click has already selected the row it landed on, and SelectedIndices is.
        _listView.MouseDoubleClick += (_, _) => EditSelected();

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
        _copyButton = Theme.CreateButton("Copy for WoWUtils", primary: true);
        _copyButton.Left = ContentLeft;
        _copyButton.Top = 580;
        _copyButton.Width = 170;
        _copyButton.Click += (_, _) => OnCopy();

        _saveButton = Theme.CreateButton("Save a copy…");
        _saveButton.Left = _copyButton.Right + 10;
        _saveButton.Top = 580;
        _saveButton.Width = 140;
        _saveButton.Click += (_, _) => OnSaveCopy();

        _editButton = Theme.CreateButton("Edit award…");
        _editButton.Left = _saveButton.Right + 10;
        _editButton.Top = 580;
        _editButton.Width = 130;
        _editButton.Click += (_, _) => EditSelected();

        _countLabel = FooterLabel(622);
        _noticeLabel = FooterLabel(644);
        _resultLabel = FooterLabel(666);

        _view.Controls.AddRange(new Control[]
        {
            playerLabel, _playerCombo, fromLabel, _fromBox, toLabel, _toBox,
            statusLabel, _statusCombo, searchLabel, _searchBox,
            _dateWarningLabel, _listView, _emptyLabel,
            _copyButton, _saveButton, _editButton, _countLabel, _noticeLabel, _resultLabel,
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

    // A small ListView subclass for the two things the control does not give us: double buffering (a
    // protected Control property ListView does not expose itself) — without it, an owner-drawn list
    // of a few thousand rows flickers noticeably while scrolling — and Ctrl+A.
    private sealed class SmoothListView : ListView
    {
        // Windows' list-view control has no select-all of its own: Ctrl+A in Explorer is Explorer's
        // doing, not the control's. Measured on a throwaway harness with a real window: Ctrl+A
        // selected 0 of 5 rows in a plain ListView, a virtual one and an owner-drawn virtual one
        // alike, with the same harness proving the keystroke reached the app. Nothing selected means
        // nothing exported now, so without this there is no way at all to export a whole archive.
        private const int LVM_SETITEMSTATE = 0x1000 + 43;
        private const uint LVIF_STATE = 0x0008;
        private const uint LVIS_SELECTED = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct LVITEM
        {
            public uint mask; public int iItem; public int iSubItem; public uint state; public uint stateMask;
            public IntPtr pszText; public int cchTextMax; public int iImage; public IntPtr lParam;
            public int iIndent; public int iGroupId; public uint cColumns; public IntPtr puColumns;
            public IntPtr piColFmt; public int iGroup;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref LVITEM lParam);

        public SmoothListView() => DoubleBuffered = true;

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!e.Control || e.KeyCode != Keys.A) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            SelectAllRows();
        }

        // One message with an item index of -1, not a loop over SelectedIndices: measured on 20,000
        // virtual rows, the loop took 4.1 seconds and raised SelectedIndexChanged 20,000 times (so
        // every footer recount ran 20,000 times over a growing selection); this takes under a
        // millisecond and raises it once.
        private void SelectAllRows()
        {
            if (VirtualListSize == 0) return;
            var item = new LVITEM { mask = LVIF_STATE, state = LVIS_SELECTED, stateMask = LVIS_SELECTED };
            SendMessage(Handle, LVM_SETITEMSTATE, (IntPtr)(-1), ref item);
        }
    }

    private void PopulateFilterOptions()
    {
        RefreshPlayerOptions();
        _playerCombo.SelectedIndexChanged += (_, _) => RunFilter();

        _statusCombo.Items.Add("All statuses");
        foreach (var s in HistoryExportPlanner.StatusOptions) _statusCombo.Items.Add(StatusDisplay(s));
        _statusCombo.SelectedIndex = 0;
        _statusCombo.SelectedIndexChanged += (_, _) => RunFilter();

        _searchBox.TextChanged += (_, _) => RunFilter();
        _fromBox.TextChanged += (_, _) => RunFilter();
        _toBox.TextChanged += (_, _) => RunFilter();
    }

    /// Rebuilt from the current snapshot every time one is adopted, not once in the constructor: a
    /// player whose first award arrives while the window is open (the post-export reload, or
    /// navigating back to this screen) was otherwise absent from the dropdown, and so unfilterable,
    /// until the whole window was closed and reopened. The current choice survives if that player is
    /// still present.
    private void RefreshPlayerOptions()
    {
        var previous = _playerCombo.SelectedIndex > 0 ? _playerCombo.SelectedItem as string : null;

        // Clearing the items drives SelectedIndex to -1 and then back, and each step raises
        // SelectedIndexChanged — without this the filter would run twice over the whole archive on
        // a combo that is mid-rebuild.
        _suspendFilter = true;
        _playerCombo.Items.Clear();
        _playerCombo.Items.Add("All players");
        var players = _awards
            .Select(a => new LootHistoryEntry(a.EffectiveFields).Winner)
            .Where(w => !string.IsNullOrEmpty(w))
            .Distinct()
            .OrderBy(w => w, StringComparer.OrdinalIgnoreCase);
        foreach (var p in players) _playerCombo.Items.Add(p!);

        var restored = previous is null ? -1 : _playerCombo.Items.IndexOf(previous);
        _playerCombo.SelectedIndex = restored >= 0 ? restored : 0;
        _suspendFilter = false;
    }

    /// Re-reads the archive and redraws from it. Called when this screen becomes the current one
    /// (see IScreen.OnShown) and after an export, because everything this window shows is a snapshot
    /// of a file two other code paths write while it is open. The selection cannot survive it: a
    /// virtual ListView's selection is row indices, and the rows are about to be different ones.
    private void ReloadFromArchive()
    {
        try
        {
            AdoptSnapshot(_loadArchive().Awards);
        }
        catch (Exception ex)
        {
            // The list on screen is kept — a stale list is more use than an empty one, and the
            // export path re-reads the archive itself and will fail the same way, loudly, rather
            // than acting on this snapshot.
            SetResult($"The archive could not be re-read, so this list may be out of date: {ex.Message}", ResultKind.Error);
        }
    }

    private void AdoptSnapshot(IReadOnlyList<ArchivedAward> awards)
    {
        _awards = awards;
        RefreshPlayerOptions();
        RunFilter();
    }

    // Re-runs ArchiveQuery.Filter and resets VirtualListSize — called on every filter control change
    // and after a successful export (to move the status column). Selection is cleared rather than
    // carried over: the row indices a virtual ListView tracks would otherwise point at whatever award
    // happens to land on that index in the new, differently-filtered result.
    private void RunFilter()
    {
        if (_suspendFilter) return;

        _listView.SelectedIndices.Clear();

        var player = _playerCombo.SelectedIndex > 0 ? _playerCombo.SelectedItem as string : null;
        var status = HistoryExportPlanner.StatusForComboIndex(_statusCombo.SelectedIndex);
        var from = HistoryExportPlanner.ParseFromDate(_fromBox.Text);
        var to = HistoryExportPlanner.ParseToDate(_toBox.Text);
        var search = string.IsNullOrWhiteSpace(_searchBox.Text) ? null : _searchBox.Text.Trim();

        // A date that does not parse filters nothing, so say so twice over: name the field, and
        // color the field itself, rather than leaving a typo looking like a filter that matched.
        _dateWarningLabel.Text = HistoryExportPlanner.DateFilterWarning(_fromBox.Text, _toBox.Text);
        _fromBox.ForeColor = HistoryExportPlanner.IsUnparsableDate(_fromBox.Text) ? Theme.Error : Theme.Text;
        _toBox.ForeColor = HistoryExportPlanner.IsUnparsableDate(_toBox.Text) ? Theme.Error : Theme.Text;

        _filtered = ArchiveQuery.Filter(_awards, player, from, to, status, search);
        _listView.VirtualListSize = _filtered.Count;
        _emptyLabel.Visible = _filtered.Count == 0;
        _listView.Invalidate();
        UpdateFooter();
    }

    private ListViewItem BuildRow(ArchivedAward award)
    {
        var entry = new LootHistoryEntry(award.EffectiveFields);
        var status = ArchiveQuery.StatusOf(award);
        return new ListViewItem(new[]
        {
            FormatTime(entry.Time),
            entry.Winner ?? "",
            ArchiveQuery.ItemDisplayName(entry.Item),
            entry.Reason ?? "",
            HistoryExportPlanner.RaidDisplay(entry),
            StatusDisplay(status),
        });
    }

    private static string FormatTime(long unixSeconds) =>
        unixSeconds == 0
            ? ""
            : DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string StatusDisplay(AwardStatus status) => status switch
    {
        AwardStatus.Open => "open",
        AwardStatus.ExportedByAddon => "exported (addon)",
        AwardStatus.ExportedByCompanion => "exported (companion)",
        AwardStatus.ExportedByBoth => "exported (both)",
        AwardStatus.Withdrawn => "withdrawn",
        AwardStatus.Excluded => "excluded",
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

    // Withdrawn and excluded awards are shown, not hidden — a withdrawn one is the only remaining
    // record that an award was taken back, an excluded one is the maintainer's own call to hold it
    // out — both set apart with Theme.TextDim, same as their status text. A corrected cell is a third
    // colour, Theme.Accent, and keeps it even inside a dimmed row (see HistoryExportPlanner.EmphasisFor).
    private void DrawRow(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (e.ItemIndex < 0 || e.ItemIndex >= _filtered.Count) { e.DrawDefault = false; return; }

        var award = _filtered[e.ItemIndex];
        var selected = e.Item?.Selected ?? false;
        var back = selected ? Theme.AccentDim : Theme.Panel;
        using (var backBrush = new SolidBrush(back))
            e.Graphics.FillRectangle(backBrush, e.Bounds);

        var fore = HistoryExportPlanner.EmphasisFor(award, e.ColumnIndex) switch
        {
            // Withdrawn or excluded: the whole row is set back, same treatment withdrawn rows already had.
            HistoryExportPlanner.RowEmphasis.Held => Theme.TextDim,
            // A corrected value is the maintainer speaking, not the game. It must not look like the
            // rest of the row.
            HistoryExportPlanner.RowEmphasis.Edited => Theme.Accent,
            _ => Theme.Text,
        };
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

    // The keys of what is selected. Keys rather than the award objects themselves, because both
    // export paths re-read the archive and act on ITS awards — the objects in this list belong to a
    // snapshot that may already be out of date (see this class's own remarks).
    private List<string> SelectedKeys() => SelectedAwards().Select(a => a.Key).ToList();

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

    // Editing goes through AwardEditor.ApplyAndSave for the same reason exporting goes through
    // ExportAndStamp: this screen holds a display snapshot, and the tray's "Read loot history now" and
    // the background sync write the archive while the window sits open. The edit is applied to a
    // freshly-read document, never to the object the list is drawn from.
    private void EditSelected()
    {
        if (_listView.SelectedIndices.Count != 1) return;
        var index = _listView.SelectedIndices[0];
        if (index < 0 || index >= _filtered.Count) return;

        var award = _filtered[index];
        using var dialog = new AwardEditDialog(award);
        if (dialog.ShowDialog(_view.FindForm()) != DialogResult.OK) return;

        AwardEditor.EditOutcome outcome;
        try
        {
            outcome = AwardEditor.ApplyAndSave(
                award.Key,
                new Dictionary<string, string>
                {
                    ["winner"] = dialog.Winner,
                    ["reason"] = dialog.Reason,
                },
                dialog.ExcludedFromExport,
                _loadArchive,
                _saveArchive);
        }
        catch (Exception ex)
        {
            SetResult($"The correction could not be saved: {ex.Message}", ResultKind.Error);
            return;
        }

        AdoptSnapshot(outcome.Document.Awards);
        SetResult(HistoryExportPlanner.EditSummary(outcome.Award), ResultKind.Success);
    }

    // Copy for WoWUtils. Everything that matters happens inside
    // HistoryExportPlanner.ExportAndStamp: the archive is re-read, what goes is decided in THAT
    // document, the clipboard gets it, and only then is the mark stamped and the file saved.
    //
    // Deciding there rather than here is the fix for the one guarantee this window had been breaking.
    // The shell is modeless: while it sits open, the tray's "Read loot history now" and the
    // background sync merge and save their own documents, and a merge can mark an award Withdrawn
    // because the addon deleted its own row. This window's list still showed it as open, and a Copy
    // sent it — "a withdrawn award is never exported, whatever is selected" failed for exactly as
    // long as the list was stale, which was the whole session.
    //
    // The snapshot this screen draws from is adopted only once the whole sequence has succeeded; on
    // failure it is untouched, so there is nothing to roll back.
    private void OnCopy()
    {
        // Captured from the display snapshot before AdoptSnapshot below replaces it — what the
        // footer promised, so the result line can say when reality disagreed with it either way
        // (see ExportDiscrepancyNote).
        var displaySelected = SelectedAwards();
        var selectedCount = displaySelected.Count;
        var shownExportableCount = HistoryExportPlanner.AwardsToExport(displaySelected).Count;

        // Which step failed decides what the user is being told and what they have to do about it,
        // and only the call site can tell them apart — the three exceptions are otherwise identical.
        var loaded = false;
        var delivered = false;
        HistoryExportPlanner.ExportOutcome outcome;
        try
        {
            outcome = HistoryExportPlanner.ExportAndStamp(
                displaySelected.Select(a => a.Key).ToList(), DateTimeOffset.UtcNow,
                () => { var fresh = _loadArchive(); loaded = true; return fresh; },
                awards =>
                {
                    // Clipboard.SetText throws on an empty string, but ExportAndStamp does not call
                    // this at all when there is nothing to send.
                    Clipboard.SetText(RcLootCouncilJsonWriter.Write(awards.Select(a => new LootHistoryEntry(a.EffectiveFields))));
                    delivered = true;
                },
                _saveArchive);
        }
        catch (Exception ex)
        {
            // A clipboard manager, an RDP session or another app can transiently hold the clipboard
            // open (CLIPBRD_E_CANT_OPEN) and WinForms already retries internally; a failure after
            // the text is on the clipboard instead means the awards went out unrecorded, which is
            // the one of the three the user has to act on.
            var message = !loaded
                ? $"The archive could not be read, so nothing was copied: {ex.Message}"
                : !delivered
                    ? $"Could not copy to the clipboard: {ex.Message}"
                    : $"Copied to the clipboard, but the export could not be recorded: {ex.Message}. Exporting again from the addon may send them a second time.";
            SetResult(message, ResultKind.Error);
            return;
        }

        AdoptSnapshot(outcome.Document.Awards);

        if (outcome.Exported.Count == 0)
        {
            SetResult("Nothing was exported — every selected award is withdrawn or excluded.", ResultKind.Neutral);
            return;
        }

        // The list drawn on screen can disagree with what actually went, in either direction — an
        // award withdrawn since the list was drawn is left out silently otherwise, and "Copied 11"
        // against 12 selected rows is the kind of quiet discrepancy that gets explained away; the
        // reverse (a withdrawal reversed since the list was drawn) sends more than the list promised.
        var note = HistoryExportPlanner.ExportDiscrepancyNote(selectedCount, shownExportableCount, outcome.Exported.Count);
        SetResult($"Copied {outcome.Exported.Count} award(s) to the clipboard and marked them exported.{note}", ResultKind.Success);
    }

    // A copy for keeping, not the export of record — writes the same text to a file but never
    // touches ExportedByCompanionAt. It re-reads the archive all the same, because the withdrawn
    // rule is not about stamping: an award the addon has taken back must not be in a file the
    // maintainer later pastes somewhere either.
    private void OnSaveCopy()
    {
        ArchiveDocument fresh;
        IReadOnlyList<ArchivedAward> toExport;
        try
        {
            fresh = _loadArchive();
            toExport = HistoryExportPlanner.ExportableFrom(fresh, SelectedKeys());
        }
        catch (Exception ex)
        {
            SetResult($"The archive could not be read: {ex.Message}", ResultKind.Error);
            return;
        }

        if (toExport.Count == 0)
        {
            SetResult("Nothing was saved — every selected award is withdrawn or excluded.", ResultKind.Neutral);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"loot-history-{DateTime.Now:yyyyMMdd-HHmmss}.json",
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;

        var json = RcLootCouncilJsonWriter.Write(toExport.Select(a => new LootHistoryEntry(a.EffectiveFields)));
        try
        {
            File.WriteAllText(dialog.FileName, json);
            var leftOut = _listView.SelectedIndices.Count - toExport.Count;
            var note = leftOut > 0 ? $" {leftOut} withdrawn or excluded award(s) were left out." : "";
            SetResult($"Saved {toExport.Count} award(s) to {dialog.FileName}.{note}", ResultKind.Success);
        }
        catch (Exception ex)
        {
            SetResult($"Could not save the file: {ex.Message}", ResultKind.Error);
        }
    }

    // The count line the brief asks for, the state of the three buttons, and the one thing no code can
    // fix: exporting again from the addon will re-send anything only the Companion has marked,
    // because WoWUtils does not dedup and the Companion cannot set the addon's own mark — writing back
    // into the game's files was examined and deliberately dropped, not deferred.
    //
    // All three are asked about the real selection. The notice in particular: it used to be asked
    // about "the selection, or everything shown if nothing is selected", and since Companion-exported
    // awards never leave the archive, that meant it was lit by default from the second time the
    // window was ever opened. It is the only mitigation the design has for the two-exporters problem,
    // and a warning that is always on is furniture.
    private void UpdateFooter()
    {
        var selected = SelectedAwards();
        var exportable = HistoryExportPlanner.AwardsToExport(selected);

        _countLabel.Text = HistoryExportPlanner.SelectionSummary(_filtered.Count, selected);

        // Asked about the real selection, not the withdrawn-filtered exportable set: a display
        // snapshot can show an award withdrawn when ArchiveMerger has since un-withdrawn it (a
        // withdrawal is reversible), and pre-filtering it out here before the check would risk this
        // — the one duplicate-export mitigation the design has — staying silent about an award about
        // to genuinely export again. Asking about the selection over-warns for an award that turns
        // out to stay withdrawn, which is the safe direction.
        _noticeLabel.Text = HistoryExportPlanner.ContainsCompanionOnlyExport(selected)
            ? "Some of these were already exported by the Companion but not the addon — exporting again from the addon will send them a second time; WoWUtils does not dedup."
            : "";

        // Nothing selected means nothing exported, so neither button offers to do anything. There is
        // no unmark and no undo for a stamped export, which makes "export everything by pressing the
        // primary button with nothing selected" a destructive default — and one that fires again if
        // the user presses the button a second time to check the first press worked.
        _copyButton.Enabled = exportable.Count > 0;
        _saveButton.Enabled = exportable.Count > 0;

        // Exactly one row: a correction is a statement about one award, and there is no sensible
        // meaning for "apply this player name to twelve of them".
        _editButton.Enabled = _listView.SelectedIndices.Count == 1;
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

    /// <summary>The awards Copy/Save actually send: what is selected, minus every withdrawn or
    /// excluded award. A withdrawn award is never exported no matter what was selected — the addon
    /// already deleted its own copy of it — and neither is one the maintainer marked excluded.
    ///
    /// There is deliberately no "nothing selected means everything shown" fallback here. It existed,
    /// and on a 20,000-award archive one press of the primary button stamped 19,793 awards exported
    /// with no unmark and no undo, which is a destructive default reachable by pressing the same
    /// button twice. Nothing selected now means nothing exported (HistoryScreen disables both buttons
    /// and says so); Ctrl+A is how everything shown is taken. This is also what the design asked for:
    /// "What gets exported is what is selected."</summary>
    public static IReadOnlyList<ArchivedAward> AwardsToExport(IReadOnlyList<ArchivedAward> selected) =>
        selected.Where(a => !a.Withdrawn && !a.ExcludedFromExport).ToList();

    /// <summary>True when the selection holds an award the Companion has marked exported but the
    /// addon has not — the one case exporting again from the addon would resend, since WoWUtils does
    /// not dedup and the Companion cannot set the addon's own mark.
    ///
    /// Asked of the two marks directly, NOT of ArchiveQuery.StatusOf. StatusOf answers "what is this
    /// award now", where Withdrawn and Excluded both outrank the export marks — so asking it here meant
    /// a withdrawn or excluded award could never light this notice, even though the addon still has no
    /// idea the Companion already sent it. Over-warning about an award that turns out to stay withdrawn
    /// is the safe direction; going quiet about one is not.
    ///
    /// Asked about the user's actual selection, never about "everything shown": Companion-exported
    /// awards stay in the archive forever, so a fallback selection meant this was lit from the second
    /// time the window was ever opened, permanently. A warning that is always on is furniture, and
    /// this is the only mitigation the design has for the two-exporters problem.</summary>
    public static bool ContainsCompanionOnlyExport(IReadOnlyList<ArchivedAward> selected) =>
        selected.Any(a => a.ExportedByCompanionAt != null
            && !(a.Fields.TryGetValue("exported", out var v) && v is true));

    /// <summary>The footer's count line. Counts rows shown as rows shown — a withdrawn award among
    /// them is still a row on screen, and reporting four rows as "3 shown" (the exportable count
    /// wearing the shown count's label) described neither number correctly.</summary>
    public static string SelectionSummary(int shownCount, IReadOnlyList<ArchivedAward> selected)
    {
        if (selected.Count == 0)
        {
            return shownCount == 0
                ? "No awards shown — nothing to select."
                : $"Nothing selected — select the awards to export. {shownCount} shown; Ctrl+A selects all of them.";
        }

        var exportable = AwardsToExport(selected).Count;
        var withdrawn = selected.Count(a => a.Withdrawn);
        var excluded = selected.Count(a => !a.Withdrawn && a.ExcludedFromExport);

        var held = new List<string>();
        if (withdrawn > 0) held.Add($"{withdrawn} withdrawn");
        if (excluded > 0) held.Add($"{excluded} excluded");

        return held.Count == 0
            ? $"{selected.Count} of {shownCount} shown award(s) selected."
            : $"{selected.Count} of {shownCount} shown award(s) selected — {exportable} will be exported, {string.Join(", ", held)}.";
    }

    /// <summary>How a cell should be set apart from an ordinary one. An enum rather than a Color so
    /// the decision can be tested without constructing a Form — HistoryScreen maps these to theme
    /// colors and nothing else.</summary>
    public enum RowEmphasis
    {
        Normal,

        /// <summary>The maintainer corrected this exact field. Marked at the cell, not the row: once
        /// the Companion is what feeds WoWUtils, a corrected value is an ASSERTION by the maintainer
        /// rather than a record from the game, and the two must not look alike — six months on, nobody
        /// remembers which is which. Outranks Held: a row that will never be exported can still carry
        /// a correction, and the two facts are both true at once, so the corrected cell keeps showing
        /// as corrected even inside a held row (see EmphasisFor).</summary>
        Edited,

        /// <summary>This award will not be exported at all — withdrawn by the game, or excluded by the
        /// maintainer. The whole row, every column — except a column Edited already claimed.</summary>
        Held,
    }

    /// <summary>The Raid column's text: the raid name and its difficulty, joined by an em dash when
    /// both are present. <c>Instance</c> is absent on every award logged before the addon started
    /// recording it, and will stay absent on those forever — that is not a defect (see
    /// LootHistoryEntry's own remarks on absence), so those awards fall back to showing just the
    /// difficulty, exactly what the old Difficulty column showed.</summary>
    public static string RaidDisplay(LootHistoryEntry e)
    {
        var instance = e.Instance;
        var difficulty = DifficultyNames.Canonical(e);

        if (!string.IsNullOrEmpty(instance) && !string.IsNullOrEmpty(difficulty))
            return $"{instance} — {difficulty}";
        return !string.IsNullOrEmpty(instance) ? instance : difficulty;
    }

    /// <summary>The field a list column shows, or null for a column that shows something the archive
    /// does not store as an editable field. Indices match the columns HistoryScreen adds, in order:
    /// Time, Player, Item, Reason, Raid, Status.</summary>
    public static string? FieldForColumn(int columnIndex) => columnIndex switch
    {
        1 => "winner",
        3 => "reason",
        _ => null,
    };

    /// <summary>Edited is checked before Held, and only for the one column the correction applies to:
    /// a row can be both corrected and held back from export at once — the natural case is a defect
    /// that got an award wrong, which is exactly what "excluded and corrected" looks like — and both
    /// facts belong on screen together. The Held check therefore only ever sees the columns Edited
    /// declined, so a row that is held but not edited still comes back Held for every column, same as
    /// before.</summary>
    public static RowEmphasis EmphasisFor(ArchivedAward award, int columnIndex)
    {
        var field = FieldForColumn(columnIndex);
        if (field != null && AwardEditor.IsFieldEdited(award, field)) return RowEmphasis.Edited;

        return award.Withdrawn || award.ExcludedFromExport ? RowEmphasis.Held : RowEmphasis.Normal;
    }

    /// <summary>The line shown after an edit is saved. Says what now stands, not what changed: the
    /// dialog sets every editable field at once, so "corrected the player" is only true of the
    /// resulting state.</summary>
    public static string EditSummary(ArchivedAward award)
    {
        var corrected = new List<string>();
        if (AwardEditor.IsFieldEdited(award, "winner")) corrected.Add("player");
        if (AwardEditor.IsFieldEdited(award, "reason")) corrected.Add("reason");

        var head = corrected.Count == 0
            ? "Saved. No corrections are in place for this award"
            : $"Saved. Corrected {string.Join(" and ", corrected)}";

        return award.ExcludedFromExport
            ? head + ", and it is excluded from every export."
            : head + ".";
    }

    /// <summary>The warning for a date field that does not parse. Unparsable text is treated as no
    /// filter at all, which on its own is silent: a mistyped date narrows nothing and looks exactly
    /// like a date that simply matched everything.</summary>
    public static string DateFilterWarning(string fromText, string toText)
    {
        var badFrom = IsUnparsableDate(fromText);
        var badTo = IsUnparsableDate(toText);
        if (badFrom && badTo) return "From and To are not dates — both filters are being ignored. Use a date like 2026-08-08.";
        if (badFrom) return "From is not a date — that filter is being ignored. Use a date like 2026-08-08.";
        if (badTo) return "To is not a date — that filter is being ignored. Use a date like 2026-08-08.";
        return "";
    }

    /// <summary>Blank means "no filter", which is not a mistake; anything else that fails to parse
    /// is.</summary>
    public static bool IsUnparsableDate(string text) =>
        !string.IsNullOrWhiteSpace(text) && ParseDate(text, timeZone: null) == null;

    /// <summary>Maps the status dropdown's SelectedIndex back to the AwardStatus it represents, or
    /// null for index 0 ("All statuses"). Index 0 is the sentinel, so the lookup into StatusOptions is
    /// offset by one — exactly the arithmetic that would silently show the wrong status class if it
    /// were ever off by one, with nothing visibly wrong to notice.</summary>
    public static AwardStatus? StatusForComboIndex(int selectedIndex) =>
        selectedIndex > 0 ? StatusOptions[selectedIndex - 1] : null;

    /// <summary>Parses a "From" field as the start of the typed day, or null if the text doesn't
    /// parse. Free-text rather than a native DateTimePicker — see HistoryScreen's own remarks on why.
    ///
    /// The day starts in a caller-supplied time zone, defaulting to the machine's local one: the user
    /// types the date they saw in the list's Time column, and that column is rendered local (see
    /// FormatTime). The override exists for the same reason RcLootCouncilJsonWriter has one — a test
    /// that takes its expected offset from the value under test cannot fail, which is exactly how a
    /// timezone defect shipped green earlier in this work.</summary>
    public static DateTimeOffset? ParseFromDate(string text, TimeZoneInfo? timeZone = null) =>
        ParseDate(text, timeZone);

    /// <summary>Parses a "To" field as the END of the typed day (23:59:59.9999999), not the instant
    /// at midnight — "to 2026-08-08" means include everything that happened ON the 8th, which is what
    /// a human typing a date means, not "up to the very start of it".</summary>
    public static DateTimeOffset? ParseToDate(string text, TimeZoneInfo? timeZone = null) =>
        ParseDate(text, timeZone)?.AddDays(1).AddTicks(-1);

    private static DateTimeOffset? ParseDate(string text, TimeZoneInfo? timeZone)
    {
        if (!DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d)) return null;
        var zone = timeZone ?? TimeZoneInfo.Local;
        var midnight = d.Date;
        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
    }

    /// <summary>The awards a Copy or a Save would actually send, decided in the document handed in —
    /// the selection's keys, minus anything withdrawn or excluded in THAT document.
    ///
    /// Keys rather than award objects on purpose: the caller's objects belong to the snapshot its
    /// list was drawn from, and the decision has to be made against the archive as it is now.</summary>
    public static IReadOnlyList<ArchivedAward> ExportableFrom(
        ArchiveDocument document, IReadOnlyCollection<string> selectedKeys)
    {
        var keys = new HashSet<string>(selectedKeys);
        return document.Awards.Where(a => keys.Contains(a.Key) && !a.Withdrawn && !a.ExcludedFromExport).ToList();
    }

    /// <summary>The trailing note for the post-Copy result line, when what actually went disagrees
    /// with what the list on screen promised. Two directions, never both at once:
    ///
    /// Fewer than selected: an award was withdrawn between the list being drawn and the button being
    /// pressed, so it was left out.
    ///
    /// More than the list showed as exportable: the reverse — ArchiveMerger.cs sets Withdrawn back to
    /// false when an award reappears in a later snapshot, so an award the list still shows withdrawn
    /// can genuinely export by the time Copy re-reads the archive. leftOut alone can never go
    /// negative to report this side; without it, "Copied 2" against a list that said "1 will be
    /// exported" reads as a miscount instead of explaining itself.</summary>
    public static string ExportDiscrepancyNote(int selectedCount, int shownExportableCount, int actuallyExportedCount)
    {
        var leftOut = selectedCount - actuallyExportedCount;
        if (leftOut > 0) return $" {leftOut} withdrawn or excluded award(s) were left out.";

        var extra = actuallyExportedCount - shownExportableCount;
        if (extra > 0) return $" {extra} more award(s) were exported than the list showed — a withdrawal was reversed since this list was drawn.";

        return "";
    }

    /// <summary>What an export did: the document it was decided in, and the awards that actually
    /// went.</summary>
    public sealed record ExportOutcome(ArchiveDocument Document, IReadOnlyList<ArchivedAward> Exported);

    /// <summary>
    /// The whole Copy path in one tested place: load the archive fresh, decide what goes from THAT
    /// document, hand it to <paramref name="deliver"/> (the clipboard), stamp only what went, save.
    ///
    /// The reload is not just about not overwriting a newer file. It is what makes "a withdrawn or
    /// excluded award is never exported" true: the shell is modeless, and the tray's "Read loot
    /// history now" or the background sync can mark an award Withdrawn — the addon deleted its own
    /// row — while this
    /// window sits open showing a snapshot that still calls it open. Deciding from the caller's
    /// snapshot and stamping in a fresh one meant the stamp was correct and the export was not.
    /// Deciding and stamping in the same document is the only shape that cannot drift.
    ///
    /// Order matters and is pinned by test: nothing is stamped and nothing is saved unless delivery
    /// returned. If <paramref name="deliver"/> or <paramref name="save"/> throws, the exception
    /// propagates and the freshly-loaded document is discarded with it — there is nothing to roll
    /// back, because nothing durable and nothing the caller already held was touched.
    /// </summary>
    public static ExportOutcome ExportAndStamp(
        IReadOnlyCollection<string> selectedKeys, DateTimeOffset stampedAt,
        Func<ArchiveDocument> load, Action<IReadOnlyList<ArchivedAward>> deliver, Action<ArchiveDocument> save)
    {
        var fresh = load();
        var toExport = ExportableFrom(fresh, selectedKeys);
        if (toExport.Count == 0) return new ExportOutcome(fresh, toExport);

        deliver(toExport);

        foreach (var award in toExport) award.ExportedByCompanionAt = stampedAt;
        save(fresh);
        return new ExportOutcome(fresh, toExport);
    }

}
