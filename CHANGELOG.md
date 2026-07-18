# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

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
