# Teams-Always-Active

Tray app that keeps Microsoft Teams from flipping to **Away** while you (or Claude Code) are working.

Teams goes Away after ~5 min with no keyboard/mouse input. When active, this app sends one
F15 key press (a key no keyboard has) whenever Windows has seen 4+ minutes of no input.
It never sends anything while you're using the PC.

## Modes (right-click the tray dot)

- **Off** — does nothing.
- **On** — keeps you active.
- **Auto** — keeps you active only while a Claude Code session is working
  (desktop Code tab, VS Code extension, CLI; not plain desktop chat).

Green dot = keeping active, grey = not. The mode is remembered across restarts.

## Install

Requires the .NET 10 SDK. Exit the tray app first if it's running (the exe is locked while it runs).

```powershell
dotnet publish src/TeamsAlwaysActive -c Release -o "$env:LOCALAPPDATA\TeamsAlwaysActive\app"
Start-Process "$env:LOCALAPPDATA\TeamsAlwaysActive\app\TeamsAlwaysActive.exe" -ArgumentList '--install' -Wait
Start-Process "$env:LOCALAPPDATA\TeamsAlwaysActive\app\TeamsAlwaysActive.exe"
```

`--install` adds four hooks to `~/.claude/settings.json` (previous version kept as `settings.json.bak`)
and starts the app at logon. A dialog confirms the result. Claude Code reads hooks when a session
starts, so open a new session afterwards.

`--uninstall` removes the hooks and the logon entry. It does not stop a running tray app
or delete `%LOCALAPPDATA%\TeamsAlwaysActive`.

## How Auto works

| Claude Code hook | Action |
|---|---|
| `UserPromptSubmit` | create `%LOCALAPPDATA%\TeamsAlwaysActive\busy\<session id>` |
| `Stop`, `SessionEnd` | delete it |
| `Notification` (`idle_prompt`) | delete it (covers Esc-interrupts, which skip `Stop`) |

Markers older than 12 h are treated as leftovers from a crash and ignored.

## Caveats

- While active, the screen will not auto-lock either. Check your employer's policy.
- Locking the PC or sleep still shows Away (by design).
- Synthetic input is blocked while an elevated (admin) window is focused, so Teams can still go Away then.
