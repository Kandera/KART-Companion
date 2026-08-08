# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.5.0] - 2026-08-08

### Added
- **Loot history archive.** The Companion reads the addon's `KART_LootHistory` out of the WoW
  SavedVariables file and keeps its own copy in `%AppData%\KARTCompanion\loot-history.json`, so
  history survives the addon's 500-entry cap and its raid-wide wipe. Read on each sync tick, plus a
  **"Read loot history now"** entry in the tray menu. Reading only — nothing is written back into
  the game's files, and no contact is made with the game process: staleness is decided purely by
  the SavedVariables file's last-write time.
- Awards that vanish from a snapshot are classified as cap-evicted, wiped, or **withdrawn** (a
  revoke or a re-decision). Nothing is deleted from the archive; a withdrawal is recorded so a
  later export can leave it out instead of crediting somebody with an item that was taken back.
- All Battle.net account folders are archived, not just the configured one, and the balloon says
  when there is more than one.
- `ArchiveStore.Save` keeps one generation of the previous archive as `loot-history.json.bak`. An
  archive that cannot be read — or that this build does not understand — is moved aside and
  reported, never replaced.
- **History screen** showing every award the archive holds, with filters for player, timeframe and
  status, and search over item names and award reasons.
- **Copy for WoWUtils** puts selected awards on the clipboard in RCLootCouncil format and records
  that they were exported.
- **Save a copy** of an export to a file without recording the export.

### Fixed
- The Settings dialog rebuilt the config from a fixed list of fields on OK, dropping any field it
  did not name.

## [1.4.0] - 2026-07-18

### Added
- **"Mit Windows starten" toggle in Settings.** Registers the app in the per-user registry Run
  key (no admin needed); the toggle reads its state straight from the registry, so it stays in
  sync even if the entry is removed via Task Manager.

## [1.3.0] - 2026-07-17

### Changed
- Settings dialog redesign by OpenDesign: borderless rounded floating card, dark icon rail with
  sync-health status dot, icon-prefixed input fields, and a custom toggle switch.
- Sync interval input replaced with a digit-only text field (no native spinner buttons) to match
  the new field styling.

### Added
- "Automatically sync" toggle in Settings — when off, only the manual Force Sync button syncs;
  the background timer stays paused.

### Fixed
- The WoW install folder field in Settings was cleared every time the dialog was reopened or the
  app restarted, even after successfully picking a folder. The folder path was never persisted to
  `config.json` (only the resolved SavedVariables file path was) — it's now saved and restored
  correctly.

## [1.2.0] - previous release

See git history prior to this file for earlier changes.
