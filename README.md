# windows-mouse-mods

A small Windows tray utility that lets you "lock" the right mouse button down — like Windows ClickLock, but for RMB.

Originally built to make camera control comfortable in games where you have to keep right-click held the entire time you're moving the camera (MMOs, ARPGs, RTS, sandbox builders). Tap a key, the right button stays held, your hand stays relaxed.

## Features

- Two activation modes, switchable in the GUI:
  - **Hotkey toggle** — a configured key (default `F8`) flips RMB held / released.
  - **ClickLock** — press and briefly hold RMB; after the configured delay it stays locked when you release.
- Either way, a normal physical RMB click while locked **releases** the lock, so you never get stuck with the button down.
- System-tray icon with quick-access menu (Enable / Disable, Mode, Settings, Exit).
- Settings persisted to `%APPDATA%\WindowsMouseMods\settings.json`.
- Optional "Start with Windows" via the user `Run` registry key (no admin needed).
- Single-instance enforced via a named mutex.
- Per-monitor V2 DPI aware.

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

Launch `WindowsMouseMods.exe`. A tray icon appears.

- **Double-click** the tray icon to open the settings window.
- **Right-click** the tray icon for quick toggles (enable/disable, mode, exit).
- The settings window's close button hides to tray; use **Exit** from the tray menu to actually quit.

## How the modes work

### Hotkey toggle

1. Press the configured hotkey → the app injects a synthetic RMB **down**. The OS (and any focused game) sees the right button as held.
2. Press it again, **or** physically click RMB, → the app injects a synthetic RMB **up** and you're back to normal.

The hotkey itself is suppressed at the OS level, so it doesn't bleed through to the focused application as a stray keypress.

### ClickLock

1. Press and hold RMB. While you're holding, a timer runs (`Hold to lock` ms — default 500).
2. If you release **after** the threshold, the natural RMB **up** is suppressed — the OS still thinks the button is held.
3. The next physical RMB click releases the lock (the click itself is swallowed so the OS only sees the synthetic up).

If you let go **before** the threshold, the click passes through normally — it's just a regular right-click.

## Configuration

The settings window covers everything:

| Setting | What it does |
| --- | --- |
| Enabled | Master on/off without quitting the app |
| Mode | Hotkey toggle vs ClickLock |
| Hotkey | Capture any key (Esc cancels). Default `F8` |
| Hold to lock (ms) | ClickLock threshold. Default 500, range 100–3000 |
| Start with Windows | Adds/removes a `HKCU\...\Run` entry |
| Start minimized to tray | If unchecked, settings window opens on launch |

The JSON file at `%APPDATA%\WindowsMouseMods\settings.json` is human-editable if you want to script it.

## Project structure

```
windows-mouse-mods/
├── WindowsMouseMods.sln
└── src/WindowsMouseMods/
    ├── WindowsMouseMods.csproj
    ├── app.manifest               # asInvoker, Win10/11 supportedOS
    ├── Program.cs                 # entry point, single-instance, runs the tray context
    ├── Native/
    │   ├── NativeMethods.cs       # all P/Invoke (SetWindowsHookEx, SendInput, ...)
    │   └── InputInjector.cs       # SendInput wrapper for synthetic RMB down/up
    ├── Hooks/
    │   ├── LowLevelMouseHook.cs   # WH_MOUSE_LL wrapper, raises events with Suppress flag
    │   └── LowLevelKeyboardHook.cs # WH_KEYBOARD_LL wrapper
    ├── Core/
    │   ├── AppSettings.cs         # JSON-backed settings in %APPDATA%
    │   ├── AutoStart.cs           # HKCU Run-key toggle
    │   └── RightClickLockController.cs  # the state machine for both modes
    └── UI/
        ├── TrayApplicationContext.cs  # NotifyIcon + context menu
        └── MainForm.cs                # settings window
```

## Implementation notes

- Uses `SetWindowsHookEx(WH_MOUSE_LL)` and `SetWindowsHookEx(WH_KEYBOARD_LL)` for global, low-level input observation. Returning a non-zero value from the hook procedure drops the message before it reaches the rest of the system, which is how we suppress the natural RMB **up** in ClickLock mode and how we keep the hotkey from leaking to the focused app.
- Synthetic events are emitted via `SendInput` with `dwExtraInfo = 'WINM'` (`0x57494E4D`). The hook checks that tag and ignores anything we injected, so we don't recurse on our own events.
- The hook callback delegate is held in an instance field so the GC doesn't collect it while a hook is installed.
- The settings window uses `KeyPreview = true` and `e.SuppressKeyPress` to capture a hotkey without echoing it into the focused control.

## Caveats

- Some games and anti-cheat systems flag synthesized input. This tool is intended for single-player and cooperative games. Don't use it where it might violate a game's terms of service.
- Hooks run on the application's UI thread message loop. If the process hangs, the hook is unresponsive and Windows will eventually time it out and unhook it. Keep the controller logic fast (it is — no I/O, no blocking).
- Currently x64 only. Build for `win-x86` if you need 32-bit.

## Roadmap

- Custom tray icon (SVG → ICO) with a visible "locked" state
- Modifier-key combos for the hotkey (e.g. `Ctrl+F8`)
- Per-process enable/disable rules (only auto-engage in specific games)
- Optional "release lock on mouse move > N px" for ClickLock parity with the Windows behavior
- Installer (MSI or `winget` package)

## Contributing

Issues and PRs welcome. Open an issue first if you're planning a non-trivial change so we can sanity-check the approach.
