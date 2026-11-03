# Teams Always Active — Design

Date: 2026-11-03 · Status: approved

## Problem

New Teams flips presence to **Away** after ~5 min with no keyboard/mouse input (also on lock/sleep).
While Claude works unattended, colleagues see Away. Want a switch that keeps presence Available.

## Research findings (why this shape)

- Teams presence follows Windows *input idle* (`GetLastInputInfo`), not "PC awake".
  `SetThreadExecutionState` / PowerToys Awake prevent sleep but do not reset input idle → not relied on.
- A real Windows Service can't do it: services run in session 0 and cannot inject input into the user desktop.
  → per-user tray app started at logon.
- Graph `setUserPreferredPresence` needs an Entra app registration (likely admin consent in the work tenant)
  and docs don't confirm "Available" survives idle → rejected.
- Side effect: synthetic input also resets the screen auto-lock timer, so the screen won't auto-lock while active.
  Accepted by the user; check employer policy.

## Solution

One .NET 10 WinForms exe, `TeamsAlwaysActive.exe`, two entry modes.

### Tray mode (no args)

- Single instance (named mutex). Tray icon only, no window.
- Menu: **Off / On / Auto** (radio) + **Exit**. Mode persisted in `%LOCALAPPDATA%\TeamsAlwaysActive\mode.txt`.
- Icon: green when currently keeping active, grey otherwise. Tooltip shows mode + state.
- Timer every 30 s:
  - `wantActive = mode == On || (mode == Auto && ClaudeBusy())`
  - if `wantActive && idle >= 4 min` → `SendInput` one F15 key down/up.
  - Never sends while the user is active (idle < 4 min).
- `ClaudeBusy()` = any file in `%LOCALAPPDATA%\TeamsAlwaysActive\busy\` modified within the last 12 h
  (older = stale from a crash, ignored and deleted). 12 h, not shorter, so multi-hour Claude runs aren't dropped.

### Hook mode (`--hook busy|idle`)

- Reads Claude Code hook JSON from stdin, takes `session_id`.
- `busy` → touch `busy\<session_id>`; `idle` → delete it. Exit 0 silently, no UI. Never fails the hook.
- Wired in `~/.claude/settings.json` (user level → covers Claude desktop Code tab, VS Code extension, CLI;
  not plain desktop chat):
  - `UserPromptSubmit` → `--hook busy`
  - `Stop`, `SessionEnd` → `--hook idle`

### Install

- `dotnet publish` to a fixed folder (e.g. `%LOCALAPPDATA%\TeamsAlwaysActive\app\`).
- Shortcut in `shell:startup` for logon start.
- Hook entries added to user `settings.json` (merged, not overwritten).

## Error handling

- Hook mode: any exception swallowed → exit 0 (must never block Claude).
- Unreadable/missing `mode.txt` → Off.
- Missing `busy\` dir → created on demand.

## Testing

- One small test project covering the decision logic: `wantActive(mode, busy)`, stale-marker cutoff,
  idle threshold. Input injection and tray verified manually (idle 4+ min with On → Teams stays Available).

## Out of scope

Graph presence API, settings UI, schedules, configurable intervals (constants until a need appears).
