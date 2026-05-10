# CLAUDE.md

Project notes for Claude Code sessions in this repo.

## What this is

Windows tray utility (.NET 9 WinForms) that "locks" the right mouse button held, like Windows ClickLock, but for RMB. Built for games where camera control means holding right-click (MMOs, ARPGs, RTS).

## Tech stack

- .NET 9 SDK, target framework `net9.0-windows`
- WinForms, single-instance, system-tray resident
- No NuGet dependencies (BCL only)

## Architecture (bottom-up)

- **`src/WindowsRightClickLock/Native/`**: P/Invoke surface.
  - `NativeMethods.cs` declares everything (SetWindowsHookEx, SendInput, structs, constants).
  - `InputInjector.cs` wraps `SendInput` for synthetic RMB down/up.
- **`src/WindowsRightClickLock/Hooks/`**: `LowLevelMouseHook` wraps `WH_MOUSE_LL`. Events expose a mutable `Suppress` flag; handlers set it to `true` to drop the message before downstream apps see it.
- **`src/WindowsRightClickLock/Core/`**: pure logic.
  - `RightClickLockController` is the ClickLock state machine. Also exposes `DebugMessage` events so the debug window can render a live stream.
  - `AppSettings` is JSON at `%APPDATA%\WindowsRightClickLock\settings.json`.
  - `AutoStart` toggles the `HKCU\...\Run` key.
- **`src/WindowsRightClickLock/UI/`**
  - `TrayApplicationContext`: runs as the `ApplicationContext`; owns the `NotifyIcon`, the controller, the settings form, and the debug form.
  - `MainForm`: settings window. Title-bar close raises a `TaskDialog` asking Minimize / Exit / Cancel.
  - `DebugForm`: live event stream wired to `controller.DebugMessage`. Bounded at 1000 lines, with Pause / Clear / Auto-scroll.
  - `TrayIcons`: GDI+ renders the idle and locked tray icons at runtime (mouse silhouette with right-button color tint). HICONs cloned and originals destroyed to avoid GDI-handle leaks.
  - `PreviewIcons`: dev-only. Run the binary with `--preview-icons [outDir]` to dump 32x32 native renders plus 4x nearest-neighbor upscales for visual review.

## Important invariants

- **Self-injection tag**: every `SendInput` call sets `dwExtraInfo = NativeMethods.InjectionTag` (`'WINM'` / `0x57494E4D`). The mouse hook checks this flag and ignores anything we injected so we don't recurse on our own events.
- **Hook callback lifetime**: each hook wrapper stores the `HookProc` delegate in an instance field. Don't inline it as a lambda passed directly to `SetWindowsHookEx`. The GC will collect it and the hook will crash.
- **Single instance**: enforced via a named mutex in `Program.cs`. Second launches exit silently. (TODO: signal the running instance to show its window.)
- **DPI**: PerMonitorV2, set via `<ApplicationHighDpiMode>` in the csproj. Don't put DPI settings in `app.manifest`; modern WinForms warns about that.
- **Window-close behavior**: `MainForm.OnFormClosing` runs a `TaskDialog` (Minimize / Exit / Cancel) for `CloseReason.UserClosing`. The `_closeResolved` flag short-circuits re-entry when the dialog programmatically closes the form. Other close reasons (system shutdown, etc.) pass through untouched.
- **Debug window persistence**: `AppSettings.ShowDebugOnStartup` is set when the user opens the debug window and cleared when they close it. On launch, if the flag is on, the window auto-reopens.
- **Hot path discipline**: the hook callback should not allocate or block. `RightClickLockController.Log()` only fires an event handler; strings are formatted via interpolation, but stay short. The debug form does its own marshaling via `BeginInvoke`.
- **Move-cancel window**: only between RMB DOWN and the timer firing. Once `_clickLockArmed` is true, mouse motion no longer prevents the lock. `_moveCancelled` short-circuits subsequent move checks within the same hold so we don't recompute distances after the decision is made.

## Build / run

```powershell
dotnet build src/WindowsRightClickLock/WindowsRightClickLock.csproj -c Release
# Output: src/WindowsRightClickLock/bin/Release/net9.0-windows/WindowsRightClickLock.exe
```

Single-file publish:

```powershell
dotnet publish src/WindowsRightClickLock/WindowsRightClickLock.csproj `
  -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

Signed release build (single self-contained `.exe` for distribution): run `scripts\release.ps1` from a non-elevated PowerShell with the OK Studio Inc. eToken plugged in. `-Tag` adds a `vX.Y.Z` git tag and a GitHub Release with the signed binary attached. The release-notes payload is passed via `--notes-file` (PowerShell argument parsing eats `*foo*` patterns when notes are inline).

## Conventions

- File-scoped namespaces.
- No external NuGet packages without a strong reason; keep the binary small and self-contained.
- The hook callback path is hot; avoid allocations and I/O there. Settings are saved only on explicit user action.

## Dev shortcuts

- `WindowsRightClickLock.lnk` at the repo root launches the most recent Release build. Regenerate after a clean checkout with `pwsh scripts/create-shortcut.ps1`.
- `.lnk` files contain absolute paths and are gitignored.
