# Windows Mouse Mods — Usage Guide

A practical, end-user walkthrough. For the design rationale and implementation details, see [whitepaper.md](whitepaper.md).

## Quick start

1. Run `WindowsMouseMods.exe`. The settings window opens; defaults are sensible.
2. In a game (or any window): **press and hold the right mouse button for half a second, then release**. The tray icon turns red and the right button stays "held" — move your mouse to look around.
3. **Click the right mouse button again** to release the lock and return to normal.

That's the whole feature. Everything else is configuration and operational hygiene.

## Installation

### Option A — Build from source

You need the .NET 9 SDK on Windows. Install via winget:

```powershell
winget install Microsoft.DotNet.SDK.9
```

Clone the repo and build:

```powershell
git clone https://github.com/owenpkent/windows-mouse-mods.git
cd windows-mouse-mods
dotnet build src\WindowsMouseMods\WindowsMouseMods.csproj -c Release
```

The output is at `src\WindowsMouseMods\bin\Release\net9.0-windows\WindowsMouseMods.exe`.

### Option B — Single-file publish

Produces a self-contained `.exe` you can copy anywhere:

```powershell
dotnet publish src\WindowsMouseMods\WindowsMouseMods.csproj `
  -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

Output lands in `src\WindowsMouseMods\bin\Release\net9.0-windows\win-x64\publish\`.

### Option C — Root-level shortcut (dev)

After building, run the helper script to drop a `.lnk` at the repo root that targets the latest Release build:

```powershell
pwsh scripts\create-shortcut.ps1
```

The `.lnk` is gitignored (absolute paths are machine-specific).

## First launch

You'll see two things:

- A **tray icon** — a stylized mouse silhouette. The right button is steel-blue when idle, vivid red when locked.
- A **settings window**. If you'd rather skip it on launch, check "Start minimized to tray" and save.

The default configuration:

- Enabled
- Hold to lock: **500 ms**
- Cancel arming if the cursor moves: **on**, threshold **5 px**
- No autostart with Windows
- Settings window opens on launch

## Daily use

### Engaging the lock

1. Press and hold the right mouse button.
2. Hold steady for the configured threshold (default 500 ms). If the cursor moves more than 5 px during the hold, arming is cancelled — that becomes a normal drag.
3. Release the button while keeping the cursor mostly still. The tray icon turns red. The OS now perceives RMB as continuously held.

### Releasing the lock

Three ways:

- **Click the right mouse button** once. The click is consumed; the lock releases. (Most natural in a game.)
- **Right-click the tray icon → Exit.** Quits the app and synthesizes a clean release.
- **Open settings → uncheck Enabled.** Disables the tool and synthesizes a clean release.

### What still passes through normally

- A short tap (< hold threshold) → regular right-click, unaffected.
- A press-and-drag (cursor moves past the move threshold during the hold) → regular drag, unaffected.
- Any RMB activity while "Enabled" is off.

## Settings reference

| Setting | Default | What it controls |
| --- | --- | --- |
| Enabled | on | Master on/off without quitting the app |
| Hold to lock (ms) | 500 | How long to hold RMB before arming triggers |
| Cancel arming if mouse moves during hold | on | If on, motion past the threshold during the hold cancels arming |
| Movement threshold (px) | 5 | Distance that triggers move-cancel. Range 1–50 |
| Start with Windows | off | Adds/removes a `HKCU\...\Run` registry entry. No admin needed |
| Start minimized to tray | off | If on, settings window does not auto-open on launch |

Settings are saved to `%APPDATA%\WindowsMouseMods\settings.json`. The file is human-readable JSON; you can edit it externally if you want to script configuration. Out-of-range values are clamped on load.

## Tray menu reference

Right-click the tray icon:

- **Enabled** — toggle without opening settings.
- **Settings...** — open the settings window.
- **Show debug window** — open the live event stream (also available from the settings window).
- **Exit** — quit the app immediately, no prompt. Settings window's title-bar close shows a Minimize/Exit/Cancel dialog instead, since users often hit X without meaning to quit a tray app.

Double-click the tray icon to open settings.

## Debug window

A separate, minimal terminal-style window that streams the controller's view of every relevant mouse event with millisecond timestamps. Open it from:

- Tray menu → **Show debug window**, or
- Settings window → **Debug window...** button.

What it logs:

- Hook installation / removal
- Physical RMB DOWN, with the hold-timer start
- ClickLock arming events (the threshold fired)
- Lock engagement (RMB UP suppressed)
- Lock release events, with the reason ("tap-to-release", "disabled in settings", etc.)
- Move-cancel events, with the actual measured distance
- Settings reapplied

Controls:

- **Pause** — freeze the stream while you read; subsequent events queue up only as a counter (the line is dropped, not buffered, to keep memory bounded).
- **Clear** — wipe the buffer.
- **Auto-scroll** — toggle; defaults on. Turn off if you want to read older lines without the view jumping.

The window remembers itself across launches: if it was open when the app exits, it auto-opens on the next launch.

### Tuning with the debug window

The debug window is the right tool if the lock isn't engaging when you expect it to:

- Press and hold RMB. If you see "ClickLock armed" but no "LOCKED" — the cursor moved past the threshold during release. Either steady your hand or raise the move threshold.
- If you see "Physical RMB DOWN" but no "ClickLock armed" — your hold was shorter than the threshold. Lower the hold-to-lock value.
- If "Move-cancel" fires constantly — your mouse has high jitter; raise the threshold to 10–15 px.

## Troubleshooting

### The lock doesn't engage at all

- **Is "Enabled" checked?** Tray menu and settings both show this.
- **Are the hooks installed?** Open the debug window — there should be a "Mouse hook installed." line at the top.
- **Is the tray icon red after release?** If yes, the lock engaged but the focused application doesn't see it. See next.

### A specific game doesn't respond to the lock

Some games use Raw Input with `RIDEV_NOLEGACY`, opting out of standard window mouse messages. In that mode, the synthesized "held" state is invisible to the game. Workarounds:

- Check the game's input settings for a Raw Input toggle.
- Some titles (older ones especially) accept legacy input as a fallback.
- If the game offers a "toggle camera" rebind, prefer that to this tool.

### RMB feels stuck after a crash

This should be rare — the app installs five emergency-release paths. If it does happen:

- **Click the right mouse button once.** A real physical click clears the OS's belief that RMB is held.
- The next time you launch the app, it will not be in a stuck state — held state is not persisted.

### The app launches and immediately disappears

That's the single-instance focus signal: a second launch hands the focus signal to the existing instance and exits. Look for the existing instance's settings window or the tray icon.

### I want to undo "Start with Windows"

Open settings → uncheck **Start with Windows** → Save. The registry entry is removed. (You can also delete `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\WindowsMouseMods` manually.)

## Uninstall

The app is portable — there is no installer to remove. To clean up:

1. From the tray menu, choose **Exit**.
2. Delete the `WindowsMouseMods.exe` (and any DLLs alongside it from a non-published build).
3. (Optional) Delete `%APPDATA%\WindowsMouseMods\` to remove the saved settings.
4. (Optional) Open Registry Editor and delete `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\WindowsMouseMods` if you had enabled "Start with Windows".

## Reporting issues

Open an issue at <https://github.com/owenpkent/windows-mouse-mods/issues> with:

- Windows version (Settings → System → About → "OS build").
- A short description of what you expected and what happened.
- If reproducible: a few lines from the debug window covering the moment things went wrong.
