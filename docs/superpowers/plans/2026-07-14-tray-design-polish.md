# Tray & Settings Visual Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make KART Companion's tray context menu, Settings dialog, and tray icon visually match the app's existing navy/cyan branding (`Theme.cs`), with sync status visible at a glance from the tray icon.

**Architecture:** Three independent visual changes to the existing WinForms tray app, layered onto the existing `Theme.cs`/`AppIcon.cs`/`TrayApplicationContext.cs`/`SettingsForm.cs` structure — no new architectural components, no behavior changes. Each task is a self-contained visual change verified by building and manually running the app (this codebase has zero unit test coverage on its WinForms UI classes — `SettingsForm`, `TrayApplicationContext`, `Theme` — by existing convention; this plan follows that convention rather than introducing UI-automation tests for a five-file app).

**Tech Stack:** C# / .NET 8 / WinForms (`System.Windows.Forms`, `System.Drawing`).

## Global Constraints

- Design spec: `docs/superpowers/specs/2026-07-14-tray-design-polish-design.md` (committed as `42c00db`).
- Rounded control corner radius: **6px**.
- Settings dialog inter-group vertical spacing: **48px** (up from the current ~35px).
- Tray icon status dot diameter: **12px**, bottom-right corner of the 32x32 icon.
- Color mapping source of truth is `Theme.cs` — no new hardcoded colors anywhere in this plan; every color used is one of `Theme.Panel`, `Theme.Accent`, `Theme.AccentDim`, `Theme.Text`, `Theme.Error`.
- No `TableLayoutPanel` rewrite, no taskbar overlay icons, no new settings/menu items/behavior — visual-only, per the spec's Non-goals.
- There is a separate, already-complete but **uncommitted** bugfix diff sitting in the working tree (`WowUtilsClient.cs`, `ConfigStore.cs`, `SettingsForm.cs`, `Program.cs`, `TrayApplicationContext.cs`, `RaidbotsReportClient.cs`, plus new `SyncGate.cs`/`CrashLog.cs` and their tests). Do not revert, "clean up", or fold that diff into this plan's commits — it is out of scope and the user will commit it separately. Each task's git commit in this plan must `git add` only the exact files that task lists, never `git add -A` or `git add .`.

---

### Task 1: Rounded corners for themed controls

**Files:**
- Modify: `KARTCompanion/Theme.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `Theme.StyleTextBox`, `Theme.StyleNumericUpDown`, `Theme.StyleButton` now also round the control's corners as a side effect — no signature change, so every existing call site (`SettingsForm.cs`) picks this up automatically with no changes there.

- [ ] **Step 1: Add the rounding helper and a `using` for `GraphicsPath`**

Open `KARTCompanion/Theme.cs`. Add this line as the very first line of the file (before `namespace KARTCompanion;`):

```csharp
using System.Drawing.Drawing2D;

```

Add this private helper method inside the `Theme` class, right after the `Error`/`Success` field declarations and before `StyleForm`:

```csharp
    private const int CornerRadius = 6;

    // WinForms controls don't support a corner-radius property — clipping the control to a
    // rounded-rect Region is the standard workaround. The Region is sized to the control's
    // bounds at the point it's created, so it must be re-applied on Resize or it'll be stale
    // (e.g. wrong size) after any layout pass that changes the control's Width/Height.
    private static void ApplyRoundedRegion(Control control)
    {
        void Apply()
        {
            if (control.Width <= 0 || control.Height <= 0) return;

            var d = CornerRadius * 2;
            var rect = new Rectangle(0, 0, control.Width, control.Height);
            using var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            control.Region?.Dispose();
            control.Region = new Region(path);
        }

        Apply();
        control.Resize += (_, _) => Apply();
    }
```

- [ ] **Step 2: Call it from the three `Style*` methods**

In `StyleTextBox`, `StyleNumericUpDown`, and `StyleButton`, add `ApplyRoundedRegion(...)` as the last line of each method body (parameter name is `box` in the first two, `button` in the third — use whichever the method already has). Example for `StyleTextBox`:

```csharp
    public static void StyleTextBox(TextBox box)
    {
        box.BackColor = Panel;
        box.ForeColor = Text;
        box.BorderStyle = BorderStyle.FixedSingle;
        ApplyRoundedRegion(box);
    }
```

Do the same (one line, `ApplyRoundedRegion(box);` / `ApplyRoundedRegion(button);`) at the end of `StyleNumericUpDown` and `StyleButton`.

- [ ] **Step 3: Build**

Run: `dotnet build KARTCompanion.sln`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Manually verify**

Run: `dotnet run --project KARTCompanion/KARTCompanion.csproj`
Right-click the new tray icon → **Settings...**. Confirm the group-key textbox, WoW-path textbox, interval numeric field, and all three buttons (Browse, Force Sync, OK/Cancel) now have visibly rounded corners instead of sharp rectangular ones. Close the dialog, exit the app via the tray menu (**Exit**).

- [ ] **Step 5: Commit**

```bash
git add KARTCompanion/Theme.cs
git commit -m "Round corners of themed WinForms controls"
```

---

### Task 2: Brand the tray context menu

**Files:**
- Create: `KARTCompanion/TrayMenuColorTable.cs`
- Modify: `KARTCompanion/TrayApplicationContext.cs:43` (the `menu` construction)

**Interfaces:**
- Consumes: `Theme.Panel`, `Theme.Accent`, `Theme.AccentDim`, `Theme.Text` (all already exist in `Theme.cs`).
- Produces: `TrayMenuColorTable` class, used only by `TrayApplicationContext`.

- [ ] **Step 1: Create the color table**

Create `KARTCompanion/TrayMenuColorTable.cs`:

```csharp
namespace KARTCompanion;

/// <summary>
/// Color table for the tray context menu's ToolStripProfessionalRenderer, mapped onto Theme.cs
/// so the right-click menu matches the app's navy/cyan branding instead of the default Windows
/// system menu style.
/// </summary>
public sealed class TrayMenuColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Theme.Panel;
    public override Color ImageMarginGradientBegin => Theme.Panel;
    public override Color ImageMarginGradientMiddle => Theme.Panel;
    public override Color ImageMarginGradientEnd => Theme.Panel;
    public override Color MenuBorder => Theme.AccentDim;
    public override Color MenuItemBorder => Theme.Accent;
    public override Color MenuItemSelected => Theme.AccentDim;
    public override Color MenuItemSelectedGradientBegin => Theme.AccentDim;
    public override Color MenuItemSelectedGradientEnd => Theme.AccentDim;
    public override Color MenuItemPressedGradientBegin => Theme.Accent;
    public override Color MenuItemPressedGradientEnd => Theme.Accent;
    public override Color SeparatorDark => Theme.AccentDim;
    public override Color SeparatorLight => Theme.AccentDim;
}
```

- [ ] **Step 2: Wire it into the tray menu**

In `KARTCompanion/TrayApplicationContext.cs`, find this line (currently line 43):

```csharp
        var menu = new ContextMenuStrip();
```

Replace it with:

```csharp
        var menu = new ContextMenuStrip
        {
            Renderer = new ToolStripProfessionalRenderer(new TrayMenuColorTable()),
            ForeColor = Theme.Text,
        };
```

(`ToolStripItem.ForeColor` inherits from its owning `ContextMenuStrip` when not explicitly set per-item, so setting it once here colors all five menu items — no per-item changes needed.)

- [ ] **Step 3: Build**

Run: `dotnet build KARTCompanion.sln`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Manually verify**

Run: `dotnet run --project KARTCompanion/KARTCompanion.csproj`
Right-click the tray icon. Confirm: the menu background is dark navy (matching the Settings dialog background), menu text is off-white, and hovering an item highlights it in the dim cyan (`AccentDim`) tone instead of the default Windows blue. Confirm all five items (Sync now / Open WoW folder / Settings... / separator / Exit) still work by clicking each. Exit via the tray menu.

- [ ] **Step 5: Commit**

```bash
git add KARTCompanion/TrayMenuColorTable.cs KARTCompanion/TrayApplicationContext.cs
git commit -m "Brand the tray context menu to match the app's navy/cyan theme"
```

---

### Task 3: Settings dialog spacing and grouping

**Files:**
- Modify: `KARTCompanion/SettingsForm.cs` (constructor body, roughly lines 57-107)

**Interfaces:**
- Consumes: `Theme.AccentDim` (existing).
- Produces: no new public members — purely repositions existing controls and adds one new `Panel` divider.

- [ ] **Step 1: Update the Y-coordinates and add the second divider**

In `KARTCompanion/SettingsForm.cs`, within the constructor, find the block starting at the WoW-path label (currently `Top = 123`) through the interval controls (currently `Top = 178` / `Top = 198`), plus the status label (currently `Top = 228`). Replace that whole block:

```csharp
        var wowPathLabel = new Label { Text = "WoW install folder (contains \"_retail_\"):", Left = 12, Top = 123, Width = 300 };
        Theme.StyleLabel(wowPathLabel);
        _wowPathBox = new TextBox { Left = 12, Top = 143, Width = 316 };
        Theme.StyleTextBox(_wowPathBox);
        var browseButton = new Button { Text = "Browse...", Left = 334, Top = 142, Width = 74 };
        Theme.StyleButton(browseButton);
        browseButton.Click += (_, _) => BrowseForWowFolder();

        var intervalLabel = new Label { Text = "Sync interval (minutes):", Left = 12, Top = 178, Width = 200 };
        Theme.StyleLabel(intervalLabel);
        _intervalBox = new NumericUpDown { Left = 12, Top = 198, Width = 80, Minimum = 1, Maximum = 240, Value = Math.Clamp(current.SyncIntervalMinutes, 1, 240) };
        Theme.StyleNumericUpDown(_intervalBox);

        // AutoSize + MaximumSize lets this grow downward to however many lines a long path
        // actually needs, instead of clipping it at a guessed fixed height.
        _statusLabel = new Label
        {
            Left = 12,
            Top = 228,
            Width = 396,
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(396, 0),
        };
```

with:

```csharp
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
```

- [ ] **Step 2: Add `divider2` to the controls collection**

Find (currently within the `Controls.AddRange` call):

```csharp
        Controls.AddRange(new Control[]
        {
            logoBox, titleLabel, divider,
            groupKeyLabel, _groupKeyBox, wowPathLabel, _wowPathBox, browseButton,
            intervalLabel, _intervalBox, _statusLabel, _forceSyncButton, okButton, cancelButton,
        });
```

Replace with:

```csharp
        Controls.AddRange(new Control[]
        {
            logoBox, titleLabel, divider,
            groupKeyLabel, _groupKeyBox, wowPathLabel, _wowPathBox, browseButton,
            divider2,
            intervalLabel, _intervalBox, _statusLabel, _forceSyncButton, okButton, cancelButton,
        });
```

(`ClientSize` is already computed dynamically from `_statusLabel.Top + Height + 12` inside `LayoutBelowStatusLabel()` further down — it does not need to change; it will pick up the new, larger `_statusLabel.Top` automatically.)

- [ ] **Step 3: Build**

Run: `dotnet build KARTCompanion.sln`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Manually verify**

Run: `dotnet run --project KARTCompanion/KARTCompanion.csproj`
Right-click the tray icon → **Settings...**. Confirm: there is visibly more room between the group-key field and the WoW-path field than before, a second thin divider line appears between the WoW-path/Browse row and the "Sync interval" row, and no controls overlap or get clipped by the dialog's bottom edge. Resize is not expected to change anything (dialog is `FixedDialog`, non-resizable) — just confirm the static layout looks right, then close and exit via the tray menu.

- [ ] **Step 5: Commit**

```bash
git add KARTCompanion/SettingsForm.cs
git commit -m "Add breathing room and a section divider to the Settings dialog"
```

---

### Task 4: Composite a status dot onto the tray icon

**Files:**
- Modify: `KARTCompanion/AppIcon.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: new overload `AppIcon.CreateTrayIcon(Bitmap logo, Color? statusDotColor)`. The existing `AppIcon.CreateTrayIcon(Bitmap logo)` call site in `TrayApplicationContext.cs` keeps working unchanged (it now delegates to the new overload with `null`).

- [ ] **Step 1: Add the new overload**

In `KARTCompanion/AppIcon.cs`, replace:

```csharp
    /// <summary>Downscales the (large, square) source logo to a crisp 32x32 icon. The returned
    /// Icon's native handle lives for the process lifetime — fine for a single tray icon on a
    /// long-running app, not worth the extra DestroyIcon plumbing for one handle.</summary>
    public static Icon CreateTrayIcon(Bitmap logo)
    {
        using var resized = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(logo, 0, 0, 32, 32);
        }
        return Icon.FromHandle(resized.GetHicon());
    }
```

with:

```csharp
    /// <summary>Downscales the (large, square) source logo to a crisp 32x32 icon. The returned
    /// Icon's native handle lives for the process lifetime — fine for a single tray icon on a
    /// long-running app, not worth the extra DestroyIcon plumbing for one handle.</summary>
    public static Icon CreateTrayIcon(Bitmap logo) => CreateTrayIcon(logo, statusDotColor: null);

    /// <summary>Same as <see cref="CreateTrayIcon(Bitmap)"/>, but composites a small solid-color
    /// dot onto the bottom-right corner when <paramref name="statusDotColor"/> is not null — used
    /// so sync state (syncing/error) is visible at a glance without hovering the tray tooltip.</summary>
    public static Icon CreateTrayIcon(Bitmap logo, Color? statusDotColor)
    {
        const int dotDiameter = 12;

        using var resized = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(logo, 0, 0, 32, 32);

            if (statusDotColor is { } color)
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var dotRect = new Rectangle(32 - dotDiameter, 32 - dotDiameter, dotDiameter, dotDiameter);
                using var brush = new SolidBrush(color);
                g.FillEllipse(brush, dotRect);
            }
        }
        return Icon.FromHandle(resized.GetHicon());
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build KARTCompanion.sln`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (the existing single-argument call site in `TrayApplicationContext.cs` must still compile unchanged against the new delegating overload).

- [ ] **Step 3: Commit**

```bash
git add KARTCompanion/AppIcon.cs
git commit -m "Add status-dot icon compositing to AppIcon.CreateTrayIcon"
```

(No standalone manual-verification step here — the new overload isn't called from anywhere yet; Task 5 wires it up and is where this becomes visible.)

---

### Task 5: Swap the tray icon to reflect sync status

**Files:**
- Modify: `KARTCompanion/TrayApplicationContext.cs`

**Interfaces:**
- Consumes: `AppIcon.CreateTrayIcon(Bitmap logo, Color? statusDotColor)` (Task 4), `Theme.Accent`, `Theme.Error`.
- Produces: no new public members.

- [ ] **Step 1: Add the two new icon fields and build them in the constructor**

Find (currently near the top of the class):

```csharp
    private readonly Bitmap _logo;
    private readonly Icon _appIcon;
```

Replace with:

```csharp
    private readonly Bitmap _logo;
    private readonly Icon _appIcon;
    private readonly Icon _syncingIcon;
    private readonly Icon _errorIcon;
```

Find (currently in the constructor):

```csharp
        _logo = AppIcon.LoadLogoBitmap();
        _appIcon = AppIcon.CreateTrayIcon(_logo);
```

Replace with:

```csharp
        _logo = AppIcon.LoadLogoBitmap();
        _appIcon = AppIcon.CreateTrayIcon(_logo);
        _syncingIcon = AppIcon.CreateTrayIcon(_logo, Theme.Accent);
        _errorIcon = AppIcon.CreateTrayIcon(_logo, Theme.Error);
```

- [ ] **Step 2: Set the syncing icon when a sync starts**

Find (in `SyncNowAsync`):

```csharp
        _trayIcon.Text = "KART Companion — syncing...";
```

Replace with:

```csharp
        _trayIcon.Text = "KART Companion — syncing...";
        _trayIcon.Icon = _syncingIcon;
```

- [ ] **Step 3: Set the error icon on failure, and reset to idle on success**

Find `ShowError`:

```csharp
    private void ShowError(string message)
    {
        _trayIcon.Text = "KART Companion — sync failed";
        _trayIcon.BalloonTipTitle = "KART Companion — sync failed";
        _trayIcon.BalloonTipText = message;
        _trayIcon.ShowBalloonTip(6000);
    }
```

Replace with:

```csharp
    private void ShowError(string message)
    {
        _trayIcon.Icon = _errorIcon;
        _trayIcon.Text = "KART Companion — sync failed";
        _trayIcon.BalloonTipTitle = "KART Companion — sync failed";
        _trayIcon.BalloonTipText = message;
        _trayIcon.ShowBalloonTip(6000);
    }
```

Find `UpdateTooltip`:

```csharp
    private void UpdateTooltip()
    {
        var last = _config.LastSyncUtc is { } t ? t.ToLocalTime().ToString("g") : "never";
        // NotifyIcon.Text has a 63-character limit.
        var text = $"KART Companion — last sync: {last}";
        _trayIcon.Text = text.Length > 63 ? text[..63] : text;
    }
```

Replace with:

```csharp
    private void UpdateTooltip()
    {
        // UpdateTooltip is only called after a successful sync (or at idle startup/after
        // Settings is saved) — never while a sync is in flight or right after a failure — so
        // resetting to the base icon here is always correct: it's the "we're idle/healthy" state.
        _trayIcon.Icon = _appIcon;
        var last = _config.LastSyncUtc is { } t ? t.ToLocalTime().ToString("g") : "never";
        // NotifyIcon.Text has a 63-character limit.
        var text = $"KART Companion — last sync: {last}";
        _trayIcon.Text = text.Length > 63 ? text[..63] : text;
    }
```

- [ ] **Step 4: Dispose the two new icons**

Find `Dispose`:

```csharp
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
```

Replace with:

```csharp
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
```

- [ ] **Step 5: Build**

Run: `dotnet build KARTCompanion.sln`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Manually verify all three icon states**

Run: `dotnet run --project KARTCompanion/KARTCompanion.csproj`. If Settings isn't already configured, fill in a (real or dummy) group key and WoW folder so `IsComplete` is true.
1. **Idle:** on launch, before any sync, the tray icon shows the plain logo with no dot.
2. **Syncing:** right-click → **Sync now**. While the request is in flight, the tray icon briefly shows a small cyan dot bottom-right (this is fast against a real API — if it's too quick to see, that's expected and fine; the code path is what matters, confirmed by reading the diff, not necessarily catching every frame by eye).
3. **Error:** with an invalid/empty group key (or by disconnecting network), trigger **Sync now** again and confirm the tray icon now shows a red dot, and it persists (survives mousing away and back) until the next successful sync.
4. **Back to idle:** fix the config and sync successfully; confirm the red dot disappears and the icon returns to plain.

Exit via the tray menu when done.

- [ ] **Step 7: Commit**

```bash
git add KARTCompanion/TrayApplicationContext.cs
git commit -m "Show sync status (syncing/error) as a tray icon badge"
```

---

## Self-Review Notes

- **Spec coverage:** Section 1 (tray menu) → Task 2. Section 2 (Settings spacing/grouping/rounded corners) → Tasks 1 and 3. Section 3 (icon status) → Tasks 4 and 5. All three spec sections have a task.
- **Type consistency:** `AppIcon.CreateTrayIcon(Bitmap, Color?)` (Task 4) is the exact signature consumed in Task 5. `TrayMenuColorTable` (Task 2, no constructor args — uses `ProfessionalColorTable`'s default parameterless constructor) matches its instantiation `new TrayMenuColorTable()`.
- **Ordering:** Task 4 must land before Task 5 (Task 5 calls the overload Task 4 adds). Tasks 1, 2, 3 are independent of each other and of 4/5 — they can run in any order, but are numbered in spec-section order for readability.
