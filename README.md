# windows-mouse-mods

A small Windows tray utility that lets you "lock" the right mouse button down — like Windows ClickLock, but for RMB.

Built to make camera control comfortable in games where you have to keep right-click held the entire time you're moving the camera (MMOs, ARPGs, RTS, sandbox builders). Press and briefly hold RMB; let go; the right button stays held until you tap it again.

## Features

- **ClickLock for RMB** — press and briefly hold RMB; once you cross the configured threshold (default 500 ms), the button stays locked when you release. The next physical RMB tap releases the lock (and is itself swallowed cleanly).
- **Tray-resident** with a quick-access menu (Enable / Disable, Settings, Show debug window, Exit).
- **Live debug window** showing the mouse-event stream, lock state, and timing — useful for tuning the hold threshold or diagnosing why a game isn't seeing the held button.
- **Close-confirmation dialog** lets you choose between "Minimize to Tray" and "Exit" each time you close the settings window.
- Settings persisted to `%APPDATA%\WindowsMouseMods\settings.json`.
- Optional "Start with Windows" via the user `Run` registry key (no admin needed).
- Single-instance enforced via a named mutex.
- Per-monitor V2 DPI aware.

## Documentation

- **[Usage guide](docs/usage.md)** — installation, daily use, settings reference, debug window, troubleshooting.
- **[Technical white paper](docs/whitepaper.md)** — design rationale, state machine, crash-safety model, performance, compatibility.

## Install

Grab the latest [release](../../releases) or build from source.

## Build from source

Requires the .NET 9 SDK (Windows). Install via winget:

```powershell
winget install Microsoft.DotNet.SDK.9
```

Then:

```powershell
dotnet build src/WindowsMouseMods/WindowsMouseMods.csproj -c Release
```

For a single-file publish:

```powershell
dotnet publish src/WindowsMouseMods/WindowsMouseMods.csproj `
  -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

The published `.exe` lands in `src/WindowsMouseMods/bin/Release/net9.0-windows/win-x64/publish/`.

## Run

Launch `WindowsMouseMods.exe`. The settings window opens (unless you've opted into "Start minimized").

- **Right-click** the tray icon for quick toggles (enable/disable, settings, debug window, exit).
- **Double-click** the tray icon to re-open settings.
- The settings window's close button asks **Minimize to Tray** vs **Exit** each time. Use **Exit** in the tray menu to quit without the prompt.

## How ClickLock works

1. Press and hold RMB. While you're holding, a timer runs (`Hold to lock` ms — default 500).
2. If you release **after** the threshold, the natural RMB **up** is suppressed — the OS still thinks the button is held.
3. The next physical RMB click releases the lock (the click is swallowed so the OS only sees the synthetic up).

If you let go **before** the threshold, the click passes through normally — it's just a regular right-click.

**Move-cancel:** if the cursor moves more than the configured threshold (default 5 px) during the hold, arming is cancelled — so a press-and-drag turns into a normal drag instead of a sticky lock. Toggle off in settings to disable.

## Debug window

Open from the tray menu (**Show debug window**). It shows:

- Live event stream with timestamps: physical RMB down/up, timer arming, lock engage/release, settings reapplied.
- Current state line at the top: enabled/disabled, ready/locked, current hold threshold.
- **Pause** to freeze the stream while you read; **Clear** to wipe; **Auto-scroll** to follow the latest event.

The window remembers itself across launches — if it was open when you exit, it re-opens on the next launch.

## Configuration

| Setting | What it does |
| --- | --- |
| Enabled | Master on/off without quitting the app |
| Hold to lock (ms) | ClickLock threshold. Default 500, range 100–3000 |
| Cancel arming if mouse moves during hold | Mirrors Windows ClickLock — moving the cursor past the threshold during the hold cancels arming. Default on |
| Movement threshold (px) | Pixel distance before move-cancel triggers. Default 5, range 1–50 |
| Start with Windows | Adds/removes a `HKCU\...\Run` entry |
| Start minimized to tray | If unchecked, settings window opens on launch |

The JSON file at `%APPDATA%\WindowsMouseMods\settings.json` is human-editable if you want to script it.

## Project structure

```
windows-mouse-mods/
├── WindowsMouseMods.sln
├── CLAUDE.md                    # notes for future Claude Code sessions
├── scripts/create-shortcut.ps1  # generates a root-level .lnk launcher
└── src/WindowsMouseMods/
    ├── WindowsMouseMods.csproj
    ├── app.manifest             # asInvoker, Win10/11 supportedOS
    ├── Program.cs               # entry point, single-instance, runs the tray context
    ├── Native/
    │   ├── NativeMethods.cs     # all P/Invoke (SetWindowsHookEx, SendInput, ...)
    │   └── InputInjector.cs     # SendInput wrapper for synthetic RMB down/up
    ├── Hooks/
    │   └── LowLevelMouseHook.cs # WH_MOUSE_LL wrapper, raises events with Suppress flag
    ├── Core/
    │   ├── AppSettings.cs       # JSON-backed settings in %APPDATA%
    │   ├── AutoStart.cs         # HKCU Run-key toggle
    │   └── RightClickLockController.cs  # ClickLock state machine
    └── UI/
        ├── TrayApplicationContext.cs  # NotifyIcon + context menu
        ├── MainForm.cs                # settings window
        ├── DebugForm.cs               # live debug window
        ├── TrayIcons.cs               # GDI-rendered idle/locked tray icons
        └── PreviewIcons.cs            # dev-only: dump icons to PNG via --preview-icons
```

## Implementation notes

- `SetWindowsHookEx(WH_MOUSE_LL)` for global, low-level mouse observation. Returning a non-zero value from the hook procedure drops the message before it reaches the rest of the system — that's how we suppress the natural RMB **up** when the lock engages, and how we swallow the release tap.
- Synthetic events go out via `SendInput` with `dwExtraInfo = 'WINM'` (`0x57494E4D`). The hook checks that tag and ignores anything we injected, so we don't recurse on our own events.
- The hook callback delegate is held in an instance field so the GC doesn't collect it while the hook is installed.
- Close confirmation uses `TaskDialog` (System.Windows.Forms) for a native-looking three-button choice.

## Caveats

- Some games and anti-cheat systems flag synthesized input. This tool is intended for single-player and cooperative games. Don't use it where it might violate a game's terms of service.
- Hooks run on the application's UI message loop. Keep the controller logic fast (it is — no I/O, no allocations on the hot path).
- Currently x64 only. Build for `win-x86` if you need 32-bit.

## Roadmap

- Custom tray icon (SVG → ICO) with a visible "locked" state
- Per-process enable/disable rules (only auto-engage in specific games)
- Optional "release lock on mouse move > N px" for ClickLock parity with the Windows behavior
- Installer (MSI or `winget` package)

## Contributing

Issues and PRs welcome. Open an issue first if you're planning a non-trivial change so we can sanity-check the approach.
