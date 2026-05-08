# Security review

**Date:** 2026-05-08
**Scope:** All shipping source under `src/WindowsRightClickLock/`.
**Threat model:** malicious code running as the same user, malicious processes in other sessions on a shared machine (RDP, fast user switching), local low-privilege users on a multi-user box. Admin-level attackers are out of scope.

This document records adversarial findings against the codebase as of commit `de65b1b` and the fixes applied. The numbering is preserved across the document so issue IDs (H1, M3, etc.) can be cited in commit messages and PRs.

## Summary

| ID | Severity | Title | Status |
|---|---|---|---|
| H1 | High | `Global\` namespace on single-instance objects allows cross-session DoS and IPC | Fixed |
| H2 | High | Named event lacks DACL | Fixed |
| M1 | Medium | Hook proc swallows exceptions with no out-of-band log | Fixed |
| M2 | Medium | `SendInput` return value ignored | Fixed |
| M3 | Medium | AutoStart path quoting fragile if path contains `"` | Fixed |
| M4 | Medium | `--preview-icons [outDir]` accepts unrestricted output path | Fixed |
| M5 | Medium | Settings file lacks integrity check | Accepted (low residual risk) |
| M6 | Medium | TOCTOU on tap-to-release if `SendInput` fails | Fixed |
| L1 | Low | No code signing | Tracked separately |
| L2 | Low | Full exception text shown in MessageBox | Accepted |
| L3 | Low | Hook event subscription not unsubscribed in `Dispose` | Accepted |
| L4 | Low | Debug window leaks fine-grained mouse activity | Accepted (opt-in) |

No findings rose to critical (no remote code execution, no elevation of privilege, no authentication bypass).

## High

### H1. `Global\` namespace on the single-instance mutex and the show-window event

**File:** `src/WindowsRightClickLock/Program.cs:8-9`

The single-instance mutex and the show-window event were declared in the kernel-global namespace via the `Global\` prefix. Two attacks were enabled:

1. Cross-session denial of service. Any low-privilege process in any session on the machine could create a mutex with the same name first. The legitimate user's launcher would then see `createdNew == false` and exit silently, with no UI feedback to the user that anything was wrong. The attacker would only need to keep the mutex alive to permanently prevent the app from starting.
2. Unauthenticated cross-process IPC. Any process on the system could call `EventWaitHandle.TryOpenExisting` on the well-known name and `Set()` it. The listener thread responded by calling `ShowMainForm`, which calls `BringToFront` and `Activate`. This was a clean focus-stealing primitive triggerable by any process on the box, suitable as a step in a UI-redress chain.

**Fix:** changed both names to the `Local\` prefix, which scopes them to the current logon session. A single Windows logon session corresponds to a single user, so cross-session attacks are no longer possible. Within a session, attackers running as the same user already have full control, so adding a SID to the name is unnecessary defense.

### H2. Named event has no DACL

**File:** `src/WindowsRightClickLock/Program.cs:64`

The `EventWaitHandle` was constructed with the default DACL, which on Windows grants synchronize access broadly. Combined with H1, this meant any process on the system could signal the event. After H1 alone, the namespace is per-session, but a per-session attacker (same user, same session) could still signal the event because no ACL restricted it.

**Fix:** the event is now constructed via `EventWaitHandleAcl.Create` with an explicit `EventWaitHandleSecurity` granting `FullControl` only to the current user's SID. The mutex received the same treatment via `MutexAcl.Create`. Any process running as a different user, even in the same session, will now receive `UnauthorizedAccessException` on `TryOpenExisting`.

## Medium

### M1. Hook proc swallows exceptions silently

**File:** `src/WindowsRightClickLock/Hooks/LowLevelMouseHook.cs:56-60`

The `try/catch` around the hook proc body swallowed every exception with no out-of-band signal. The comment claimed "the controller's debug stream is the right place to learn about it," but the swallowed exception originated inside the event handler and never reached the controller's logging path. A bug that throws on every event would have been an undiagnosable silent malfunction.

**Fix:** added a static rate-limited error logger. Up to 5 hook-proc exceptions per process lifetime are appended to `%LocalAppData%\WindowsRightClickLock\hook-errors.log` via a fire-and-forget `Task` so the hook thread itself never blocks on file I/O. After 5 errors the log is suppressed to avoid runaway disk writes.

### M2. `SendInput` return value ignored

**File:** `src/WindowsRightClickLock/Native/InputInjector.cs:53` and `19-23`

`SendInput` returned the count of events successfully inserted; the code discarded it via `_ = SendInput(...)`. `RightDown` then unconditionally called `MarkHeld(true)` even if `SendInput` returned 0. The `_heldFlag` could go out of sync with the OS state, causing `EmergencyRelease` to send a `RIGHTUP` for a button that was never pressed, or fail to release one that was.

**Fix:** `SendMouseFlag` now returns `bool` indicating whether `SendInput` reported success. `RightDown`, `RightUp`, and `EmergencyRelease` only update the held flag on success.

### M3. AutoStart path quoting breaks if `Environment.ProcessPath` contains `"`

**File:** `src/WindowsRightClickLock/Core/AutoStart.cs:22-23`

The Run-key value was constructed as `$"\"{exe}\""`. NTFS allows `"` in path components. If the executable lived at a path containing a quote, the resulting Run-key value would be malformed and the shell parser would resolve a different binary on next logon.

**Fix:** the autostart writer now rejects paths containing characters that cannot be safely round-tripped through the shell parser (`"`, control characters), throwing `InvalidOperationException` with a message the UI surfaces to the user. In practice this rejection will never fire on a normal Windows install; the check is defense in depth.

### M4. `--preview-icons [outDir]` accepts unrestricted output path

**File:** `src/WindowsRightClickLock/Program.cs:23-26` and `src/WindowsRightClickLock/UI/PreviewIcons.cs`

The output directory was passed straight through to `Directory.CreateDirectory` and `Bitmap.Save`. Any process on the box that could spawn `WindowsRightClickLock.exe --preview-icons C:\some\path` could cause the binary to drop four PNG files at `C:\some\path` as the user. The files were our content, not attacker-controlled, so impact was limited to file creation and possible overwrite of legitimately named files at the chosen path. Still, the surface was unintentional.

**Fix:** the `--preview-icons` argument is now validated to be a relative path with no directory traversal (`..`). Absolute paths and traversal segments are rejected with a clear error before any file is created. The dev workflow documented in `CLAUDE.md` (running with no argument, or with a simple subfolder name like `preview`) continues to work.

### M5. Settings file integrity (accepted)

**File:** `src/WindowsRightClickLock/Core/AppSettings.cs:43-44`

A user with write access to `%AppData%\WindowsRightClickLock\settings.json` can modify the contents. The schema is `bool`/`int` only, and `JsonSerializer.Deserialize<AppSettings>` is invoked with no polymorphism, so deserialization-gadget abuse is not feasible. The autostart toggle is checked against the registry, not the JSON, so the JSON cannot cause a binary to autostart that wouldn't already.

**Decision:** accepted. A future maintainer should re-evaluate this finding if `string` or `object` fields are added to `AppSettings`. The simplest robust hardening would be to keep the schema strictly primitive and add a unit test that fails the build if a non-primitive property is added.

### M6. TOCTOU on tap-to-release if `SendInput` fails

**File:** `src/WindowsRightClickLock/Core/RightClickLockController.cs:114-122`

When the user tapped RMB to release the lock, the controller did three things in order: suppress the physical DOWN, set `_swallowNextRealRmbUp = true`, and call `ReleaseLockIfHeld` which calls `InputInjector.RightUp` (a `SendInput` call). If `SendInput` failed, the OS still believed RMB was held, the controller had already cleared `_locked`, and the next physical UP was about to be swallowed by the flag we just set. Result: stuck synthetic RMB-down with no recovery short of process exit.

**Fix:** `RightUp` now returns `bool`. The tap-to-release path only sets `_swallowNextRealRmbUp = true` and `_locked = false` if the synthetic UP succeeded. On failure, the controller logs the failure to the debug stream and lets the natural physical UP pass through, which corrects the OS's belief about the button state.

## Low (accepted)

### L1. No code signing
The shipping binary is unsigned. SmartScreen will warn on first run; some EDR products may quarantine. Operational concern, not a code-level vulnerability. A signed build with an EV cert is tracked separately as a release-engineering task.

### L2. Full `Exception.ToString()` in MessageBox
`Application.ThreadException` shows the full exception including stack and assembly paths. The binary is open-source so the disclosure is bounded; no secrets are reachable. Accepted.

### L3. `_mouseHook.MouseEvent` not unsubscribed in `Dispose`
The hook and its sole subscriber share a lifetime, so the leaked subscription is harmless in practice. Cosmetic.

### L4. Debug window timestamps every mouse event
A user who shares the debug window in a screen capture leaks their own mouse activity. Opt-in by the user, accepted.

## Informational (correctly handled, recorded for future maintainers)

- The `InjectionTag = 0x57494E4D` is not a security boundary. It is a self-recursion marker. A same-user attacker can spoof it. The codebase relies on it only for filtering self-injected events, never for trust decisions.
- `WH_MOUSE_LL` is a low-level hook. Unlike non-LL global hooks, it does not inject the controller's DLL into other processes. The hook proc runs only in our own process. No cross-process code execution surface is introduced.
- Single-instance mutex semantics for an abandoned mutex are correct: a crashed first instance leaves the kernel object marked abandoned, and the next launch sees `createdNew == true` because the kernel released ownership when the prior owning thread died.
- `SystemEvents.SessionSwitch` releases the lock on `SessionLock` and `SessionLogoff`, preventing the user from returning from the lock screen with a synthetically-held button.

## Fix priority and rollout

The applied fixes were sequenced as:

1. H1 and H2 first. These are the only findings a security reviewer at Microsoft is likely to flag during a five-minute skim of the reference implementation; addressing them removes a distraction during the pitch.
2. M2 and M6 second. These are correctness hygiene around `SendInput` that also closes a real "stuck button" failure mode.
3. M1 third. Diagnosability of the hook layer.
4. M3 and M4 fourth. Defense in depth on minor surfaces.

All fixes preserve the existing public behavior. No setting names changed, no settings-file format changed, no IPC protocol changed for legitimate clients. The only externally observable difference is that other processes can no longer signal the show-window event, which was never a documented or supported integration.
