# Tray & Settings Visual Polish — Design

Date: 2026-07-14
Status: proposed

## Context

KART Companion already has a coherent visual identity (`Theme.cs`, colors lifted from the
addon's `KAimg.jpg` icon: dark navy background, cyan accent) applied to the `SettingsForm`
dialog. Three visible gaps remain, identified during a design review of the app's two surfaces
(tray icon + context menu, and the Settings dialog):

1. The tray right-click `ContextMenuStrip` renders with the default Windows system theme —
   visually inconsistent with the branded dialog and icon.
2. The Settings dialog is laid out with hand-placed `Left`/`Top` pixel coordinates with little
   breathing room between the three field groups (group key / WoW path / interval).
3. Sync status (idle / syncing / error) is only visible as tooltip text on the `NotifyIcon`,
   which requires hovering to see — there's no glanceable indicator.

This is a visual/UX-only pass — no behavioral or data changes. It follows a separate bugfix pass
(credential leak, config write race, exception-safety, unhandled-exception handling, URL
escaping) that was completed first and is out of scope here.

## Goals

- Tray context menu visually matches the app's navy/cyan branding.
- Settings dialog reads as less cramped, with clearer grouping between its three settings.
- Sync state (idle / syncing / error) is visible at a glance from the tray icon itself, not just
  from a hover tooltip.

## Non-goals

- No `TableLayoutPanel`/auto-layout rewrite of `SettingsForm` — the app runs on a single
  developer's machine at a fixed DPI; the manual-layout approach stays, just tidied up.
- No taskbar overlay icons (Teams/Slack-style) — not available for tray-only `NotifyIcon` apps
  without a taskbar button; out of technical reach for this app's shape.
- No new settings, menu items, or behavioral changes of any kind.

## Approach

### 1. Tray context menu theming

Add a `TrayMenuColorTable : ProfessionalColorTable` (new file, `KARTCompanion/TrayMenuColorTable.cs`)
that overrides the color table entries WinForms' `ToolStripProfessionalRenderer` reads —
`ToolStripDropDownBackground`, `MenuItemSelected`, `MenuItemSelectedGradientBegin/End`,
`MenuItemBorder`, `MenuBorder`, `SeparatorDark`/`SeparatorLight`, `MenuItemPressedGradientBegin/End`
— mapped onto the existing `Theme` constants (`Theme.Panel` for the menu background,
`Theme.Accent`/`Theme.AccentDim` for hover/selection, `Theme.Text` for item text via a
`ToolStripProfessionalRenderer` subclass overriding `OnRenderItemText`).

`TrayApplicationContext`'s `ContextMenuStrip` gets `Renderer = new ToolStripProfessionalRenderer(new TrayMenuColorTable())`
and `ForeColor = Theme.Text` set once at menu construction. No change to menu items, click
handlers, or keyboard/mouse behavior — purely a `Renderer` swap.

### 2. Settings dialog spacing & grouping

Within `SettingsForm`'s existing manual-layout constructor:

- Increase inter-group vertical spacing from the current ~35px (label→next-group-label) to
  ~48px, giving each of the three groups (group key / WoW path / interval) visual room without
  the dialog feeling sparse.
- Add a second thin `Theme.AccentDim` divider panel (matching the existing header divider)
  between the WoW-path group and the interval group, so the three groups read as distinct
  sections rather than one dense block.
- Round the corners of `TextBox`/`NumericUpDown`/`Button` controls to a 6px radius. WinForms
  controls don't support corner radius directly; use `Control.Region = new Region(GraphicsPath)`
  with a rounded-rect path, applied in `Theme.StyleTextBox`/`StyleNumericUpDown`/`StyleButton`
  (extending those existing methods rather than adding new ones) so every themed control picks
  it up automatically, including in any future dialog. Must re-apply the region on `Resize`
  (`_statusLabel.SizeChanged` already triggers a re-layout pass — hook the same event) since a
  `Region` is sized to the control's bounds at the point it's created.
- Form `ClientSize` grows slightly (still computed dynamically via the existing
  `LayoutBelowStatusLabel` closure) to accommodate the extra spacing.

### 3. At-a-glance sync status on the tray icon

Extend `AppIcon.cs` (which already loads/composites the base icon from `KAimg.jpg`) with a
method that draws a small status dot (bottom-right, ~1/3 icon size) onto a copy of the base
bitmap before converting to an `Icon`: cyan (`Theme.Accent`) while syncing, red (`Theme.Error`)
after a failed sync, no dot when idle/last sync succeeded. `TrayApplicationContext` builds all
three icon variants once at startup (idle/syncing/error) and swaps `_trayIcon.Icon` at the same
points it already updates `_trayIcon.Text` (`SyncNowAsync`, `ShowError`, `UpdateTooltip`) — no
new state machine, just three pre-built `Icon` instances selected at existing transition points.
The error variant persists until the next successful sync, matching how `ShowError`/`UpdateTooltip`
already behave for the tooltip text today.

## Testing

All three changes are pure rendering/visual behavior with no new pure-logic branches worth a
unit test (consistent with this codebase's existing convention: `SettingsForm`,
`TrayApplicationContext`, and `Theme` have zero unit test coverage today — tests cover the
pure-logic classes like `SavedVariablesLocator`, `LuaTableWriter`, the sim parsers, `ConfigStore`,
`SyncGate`, `CrashLog`). Verification is by running the app and visually inspecting the tray menu,
Settings dialog, and the three icon states (trigger via Sync now / a forced network failure),
per the `run` skill's launch-and-observe approach rather than automated tests.

## Rollout

Single pass across all three areas in one PR, since they're small, independent, low-risk changes
to the same two files (`TrayApplicationContext.cs`, `SettingsForm.cs`) plus two new small files
(`TrayMenuColorTable.cs`, and the icon-compositing addition to `AppIcon.cs`).
