# CLAUDE.md

Project notes for Claude Code sessions in this repo.

## What this is

Windows tray utility (.NET 9 WinForms) that "locks" the right mouse button held — like Windows ClickLock, but for RMB. Built for games where camera control means holding right-click (MMOs, ARPGs, RTS).

## Tech stack

- .NET 9 SDK, target framework `net9.0-windows`
- WinForms, single-instance, system-tray resident
- No NuGet dependencies — BCL only

## Architecture (bottom-up)

- **`src/WindowsMouseMods/Native/`** — P/Invoke surface.
  - `NativeMethods.cs` declares everything (SetWindowsHookEx, SendInput, structs, constants).
  - `InputInjector.cs` wraps `SendInput` for synthetic RMB down/up.
- **`src/WindowsMouseMods/Hooks/`** — wrappers for `WH_MOUSE_LL` and `WH_KEYBOARD_LL`. They raise events with a mutable `Suppress` flag — handlers set it to `true` to drop the message before downstream apps see it.
- **`src/WindowsMouseMods/Core/`** — pure logic.
  - `RightClickLockController` is the state machine for both modes (HotkeyToggle, ClickLock).
  - `AppSettings` is JSON at `%APPDATA%\WindowsMouseMods\settings.json`.
  - `AutoStart` toggles the `HKCU\...\Run` key.
- **`src/WindowsMouseMods/UI/`** — `TrayApplicationContext` (NotifyIcon + context menu, runs as the `ApplicationContext`) and `MainForm` (settings window).

## Important invariants

- **Self-injection tag**: every `SendInput` call sets `dwExtraInfo = NativeMethods.InjectionTag` (`'WINM'` / `0x57494E4D`). The mouse hook checks this flag and ignores anything we injected so we don't recurse on our own events.
- **Hook callback lifetime**: each hook wrapper stores the `HookProc` delegate in an instance field. Don't inline it as a lambda passed directly to `SetWindowsHookEx` — the GC will collect it and the hook will crash.
- **Single instance**: enforced via a named mutex in `Program.cs`. Second launches exit silently. (TODO: signal the running instance to show its window.)
- **DPI**: PerMonitorV2, set via `<ApplicationHighDpiMode>` in the csproj. Don't put DPI settings in `app.manifest` — modern WinForms warns about that.
- **Window-close behavior**: `MainForm`'s title-bar close hides to tray. Only "Exit" in the tray menu actually quits.

## Build / run

```powershell
dotnet build src/WindowsMouseMods/WindowsMouseMods.csproj -c Release
# Output: src/WindowsMouseMods/bin/Release/net9.0-windows/WindowsMouseMods.exe
```

Single-file publish:

```powershell
dotnet publish src/WindowsMouseMods/WindowsMouseMods.csproj `
  -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

## Conventions

- File-scoped namespaces.
- No external NuGet packages without a strong reason — keep the binary small and self-contained.
- The hook callback path is hot — avoid allocations and I/O there. Settings are saved only on explicit user action.

## Dev shortcuts

- `WindowsMouseMods.lnk` at the repo root launches the most recent Release build. Regenerate after a clean checkout with `pwsh scripts/create-shortcut.ps1`.
- `.lnk` files contain absolute paths and are gitignored.
