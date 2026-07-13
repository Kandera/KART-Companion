# KART Companion

A small Windows tray app that syncs droptimizer sim data (Raidbots + QE Live, via the WoWUtils
API) into [Keine Ahnung Raid Tools (KART)](https://github.com/Kandera/KeineAhnungRaidTools)'s
Loot Council window, so council members can see each candidate's simulated %DPS/HPS gain from
the item being rolled.

This is a separate, standalone project from the KART addon itself — most KART users don't need
it. Only someone acting as loot master/officer, who wants gain% shown during loot decisions,
needs to install and run this.

## Why this exists

WoW addons can't make HTTP requests or read arbitrary files — the only I/O they have is their
own SavedVariables Lua file, written to disk at logout/`/reload` and read back only at
login/`/reload`. This app does the networking on the addon's behalf and writes the result into
`KART_WoWUtilsCache` inside `KeineAhnungRaidTools.lua`, touching nothing else in that file.

Only one person (typically the loot master/officer) needs to run this — the WoWUtils group key
already grants access to the whole roster's data.

## Running it

1. Grab `KART-Companion-win-x64-*.exe` from the [latest release](../../releases/latest) (built
   by CI on every tag push — no separate install needed, it's self-contained).
2. Run it. It appears in the system tray.
3. Right-click the tray icon → **Settings...** → paste your WoWUtils group key, point it at your
   WoW install folder (the one containing `_retail_`), pick a sync interval.
4. It syncs automatically on that interval, or right-click → **Sync now**.
5. In-game: `/reload` to pick up new data (SavedVariables only load at login/reload — this is a
   WoW limitation, not a bug). Enable "Show droptimizer gain % in Loot Council" in KART's
   Droptimizer settings tab.

## Building locally

Requires the **.NET 8 SDK** (not just the runtime — check with `dotnet --list-sdks`). Install via
`winget install Microsoft.DotNet.SDK.8` if you don't have it.

```
dotnet build KARTCompanion.sln
dotnet test KARTCompanion.Tests/KARTCompanion.Tests.csproj
```

CI (`.github/workflows/release.yml`) is the authoritative build path for releases — it runs on
`windows-latest`, which has the SDK preinstalled.

## Project layout

- `KARTCompanion/` — the app itself (tray UI, WoWUtils client, Raidbots/QE Live sim fetchers,
  SavedVariables writer).
- `KARTCompanion.Tests/` — unit tests, including fixtures captured from real Raidbots/QE Live
  reports so the parsing logic is tested against real data, not guessed shapes.
