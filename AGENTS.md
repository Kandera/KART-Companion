# Project Conventions

## What this is

A Windows tray app (WinForms, `net8.0-windows`) that writes droptimizer data into the KART
addon's `KART_WoWUtilsCache` inside `KeineAhnungRaidTools.lua`. Separate repo from the addon.
README.md covers what it does and how it is run.

## Language: English

Commit messages, code comments, README, CHANGELOG and release notes are English. The app's
own UI strings are German, because its users are.

## The SavedVariables file is not ours

It belongs to WoW and to the addon. Write `KART_WoWUtilsCache` and touch nothing else in it.

The game reads that file only at login and `/reload`, and writes it only at logout and
`/reload`. A write while the client is running is lost or overwrites what the client is about
to save. Any feature that assumes a live channel to the running game is a feature this app
cannot have.

## The test suite constructs real windows

`KARTCompanion.Tests/WinFormsHarness.cs` builds actual Forms. They are never shown -- only
their handles are forced. The older note that "nothing in this suite constructs a Form, a
known and accepted gap" is obsolete; it was never a limit, only an option nobody had taken,
and it let several defects reach the maintainer's screen.

**Two mutations must never be run.** Both put a modal dialog on the screen and cost the run:

- deleting `Application.ThreadException += Collect` -- raises `ThreadExceptionDialog`, parks
  the STA thread for the full 60 s and leaves the dialog standing
- `UnhandledExceptionMode.CatchException` -> `ThrowException` -- the test host dies

Both are marked as such in the code. Reason about them instead of running them; running them
has already gone wrong twice.

Two `MinimumSize` clamp tests need a second monitor and do nothing on CI.

## Report what is unpinned, not only what was checked

A complete-looking mutation table says nothing about a mechanism with no coverage at all.
That is how `chrome.BringToFront()` stayed deletable -- which opens the window with no title
bar, no caption and no close button. Every report lists the unpinned, not just the verified.
