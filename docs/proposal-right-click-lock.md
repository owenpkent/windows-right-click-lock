# Right-Click Lock: Proposal for Native Windows Integration

**Version:** 0.1
**Author:** Owen Kent (redacted@example.invalid)
**Reference implementation:** [github.com/owenpkent/windows-right-click-lock](https://github.com/owenpkent/windows-right-click-lock)
**Audience:** Windows Input, Devices, Settings, and Accessibility teams at Microsoft

---

## Executive summary

Windows has shipped **ClickLock**, a tap-and-hold-to-lock primary mouse button feature, since Windows 2000. There is no equivalent for the secondary (right) button. The asymmetry has a real user cost: sustained right-click is the de-facto camera gesture across modern PC games (MMOs, ARPGs, RTS, builder/sandbox titles, flight sims), and accessibility users with limited grip strength need symmetric button-locking for the same reasons that motivated ClickLock in the first place.

This proposal asks Microsoft to add **Right-Click Lock** as a native option in Mouse Properties (classic Control Panel) and Settings → Bluetooth & devices → Mouse, mirroring the existing ClickLock UX. A complete user-mode reference implementation already exists (~600 LOC, .NET 9, BCL-only) that demonstrates feasibility, validates the UX, and surfaces the operational concerns (move-cancel, crash-safe release, hook ordering) Microsoft would need to address. The proposal includes a UX mockup, suggested implementation paths inside the OS input stack, risk analysis, and the third-party fallback we will ship if the feature does not land natively.

---

## 1. Problem statement

### 1.1 The asymmetry

ClickLock, exposed on the Buttons tab of Mouse Properties, allows a user to:

1. Tap and briefly hold the **primary** mouse button.
2. After a configurable threshold, release the button.
3. The OS continues to perceive the button as held until the next tap.

This is precisely the behavior many users want for the **secondary** button. The Windows ClickLock implementation is hard-coded to the primary button; there is no public API, Group Policy, registry knob, or configuration surface that extends it to the secondary button.

### 1.2 Affected user populations

- **PC gamers.** Sustained right-click is the dominant camera gesture in titles spanning MMOs (World of Warcraft, Final Fantasy XIV, Lost Ark, New World), ARPGs (Path of Exile, Diablo IV, Last Epoch), RTS (Age of Empires IV, Stormgate), city builders (Cities: Skylines II, Manor Lords), space sims (EVE Online, Star Citizen), and flight sims (MSFS). Hours of held RMB load the index/middle finger statically; for many users this presents as discomfort, for some as injury.
- **Accessibility users.** Users with arthritis, hand injury, repetitive-strain symptoms, or motor impairment benefit from button-locking on *both* buttons. ClickLock is documented under Microsoft's accessibility guidance for left-button use; the same arguments extend symmetrically to the right.
- **Productivity users.** Right-button drag (file copy/move with context menu, certain CAD selection idioms) and long context-menu interactions benefit from the same affordance.

### 1.3 Current workarounds

- **AutoHotkey scripts.** Functional but require user-installed runtimes, are flagged by certain anti-cheat systems, and require admin privileges to interact with elevated windows.
- **Per-game rebinding.** Only a subset of games expose "toggle camera" as a discrete bindable action.
- **Third-party utilities** (including this project's reference implementation). Operate via `WH_MOUSE_LL` + `SendInput` injection, are unsigned by default, may be flagged by Defender SmartScreen, and depend on hook ordering that can be disrupted by other utilities or system updates.

None of these are appropriate for the broad user base who would benefit. The feature belongs in the OS.

---

## 2. Proposal

### 2.1 What ships

Add a **Right-Click Lock** section to the Buttons tab in Mouse Properties, structurally identical to the existing ClickLock section:

- A "Turn on Right-Click Lock" checkbox.
- A "Settings…" button opening a modal with a hold-duration slider (Short ↔ Long), matching the existing ClickLock settings dialog.
- A two-line description explaining the feature.

Mirror the same option in **Settings → Bluetooth & devices → Mouse**, alongside the existing primary-button options.

### 2.2 Behavior specification

Identical pattern to ClickLock, applied to the secondary button:

1. User holds RMB physically for ≥ *threshold* milliseconds (default ~500 ms, user-configurable).
2. On physical RMB release, OS continues to assert RMB-down to the foreground window.
3. Next physical RMB click releases the synthetic hold and returns to idle.

### 2.3 Move-cancel safety overlay

The reference implementation surfaces a non-obvious safety requirement: if the user moves the mouse beyond a small dead-zone *before* the lock arms, treat the gesture as a real drag and do **not** lock. This prevents the lock from triggering on legitimate right-button drags or click-releases that happen to span the threshold window. Microsoft should incorporate this overlay in any native implementation; it is documented in the reference whitepaper §4.

### 2.4 Crash / session safety

Because a locked secondary button can leave the OS believing RMB is held even after the locking process exits, any implementation needs:

- A guaranteed release on process termination (`atexit` semantics, or kernel-side cleanup if implemented in the input stack).
- A guaranteed release on session lock / fast user switch / sleep.
- A "panic release": pressing any keyboard key, or moving focus to a system overlay, releases the lock.

A native implementation in the input stack avoids these concerns entirely (the OS simply stops asserting the synthetic state). The reference implementation handles them at user-mode and the engineering is not trivial.

---

## 3. Why this belongs in Windows (vs leaving to ISVs)

| Concern | Third-party | Native |
|---|---|---|
| Anti-cheat compatibility | Routinely false-flagged for synthetic input | Built-in, exempt |
| Code signing / SmartScreen | Per-vendor cert, reputation cold-start | Already trusted |
| Hook ordering / interaction with other utilities | Fragile, breaks when other tools install hooks | Implemented at the input source |
| Crash-safe release | Requires careful per-app engineering | Implicit |
| Per-app exclusion model | Each ISV reinvents | Inherits Focus Assist / app exclusion |
| Discoverability | Search engines, word of mouth | Mouse Properties + Settings, where users already look |
| Accessibility audit | Per-vendor, inconsistent | Inherits Microsoft accessibility process |

The feature has the characteristics that historically motivate Microsoft to absorb third-party tooling into Windows: it is small, broadly useful, addresses an existing accessibility asymmetry, has a clean UX precedent (ClickLock), and is currently served by a fragmented landscape of unsigned utilities and AutoHotkey scripts that present support and security friction users should not need to navigate.

---

## 4. UX

The proposed Buttons tab layout, with a Right-Click Lock section inserted below the existing ClickLock section:

![Mouse Properties with Right-Click Lock](mouse-properties-mockup.png)

The mockup is generated from the current Windows 11 Mouse Properties dialog (`main.cpl` Buttons tab) by inserting a new section that mirrors the ClickLock layout: same card chrome, same checkbox + Settings… button row, same two-line description. The visual consistency is intentional: the proposal is for a *symmetric* feature, and the UX should communicate that symmetry.

---

## 5. Implementation outline

This section is offered as background; Microsoft's input team will know the codebase better than the author.

### 5.1 Suggested layer

The reference implementation operates at the `WH_MOUSE_LL` user-mode hook layer, which is the highest layer at which a third party can intercept and inject mouse events. A native implementation has cleaner options:

- **Mouse Properties path.** `MouseControlPanel.dll` already owns the ClickLock UI and persistence; extending it to a second button is principally a UI and `SystemParametersInfo` change.
- **Input stack path.** The actual lock semantics are most cleanly implemented in the same layer as the existing ClickLock, believed to be in the Win32 user-mode side of input dispatch (`win32k.sys` / `user32.dll` boundary). Any place ClickLock branches on `MK_LBUTTON` is a natural site for a parallel `MK_RBUTTON` branch behind the new setting.
- **Settings app path.** The modern Settings page (Bluetooth & devices → Mouse) reads the same `SystemParametersInfo` settings as Mouse Properties; a second checkbox plus a slider in `Settings.MouseAndTouchpad` should suffice.

### 5.2 Persistence

A new `SPI_*` pair (e.g., `SPI_GETRIGHTCLICKLOCK` / `SPI_SETRIGHTCLICKLOCK` and a corresponding time setting) plus the standard `HKCU\Control Panel\Mouse` registry mirror is the obvious extension of the existing model.

### 5.3 Per-app exclusion

The reference implementation does not currently expose per-app exclusion. A native implementation should hook into the Focus Assist / app exclusion model so that competitive games or specific applications can opt out by user choice or by certified-app metadata.

---

## 6. Risks and mitigations

| Risk | Likelihood | Mitigation |
|---|---|---|
| Game with right-button-fire mechanic locks unintentionally | Medium | Default off; per-app exclusion; move-cancel overlay |
| Power user with existing third-party tool experiences double-handling | Low | Detect and defer when external low-level mouse hook installed by user app, similar to existing input-stack courtesies |
| Accessibility regression if added without screen-reader/Narrator audit | Low | Inherit standard accessibility review; mirror ClickLock's existing affordances |
| Telemetry / feature-flag concerns delaying GA | Medium | Ship behind Settings checkbox default-off; surface to Insiders first |

The most likely actual risk is the first one: a game that uses right-click for transient fire/aim actions could see the lock trigger unintentionally on a long hold. The move-cancel overlay handles the common case; per-app exclusion handles the rest.

---

## 7. Success metrics

If the feature ships, the proposed signals to track post-launch:

- **Adoption.** Percentage of users with Right-Click Lock enabled, segmented by game-detection telemetry.
- **Third-party tool decline.** Reduction in installs of AutoHotkey distributions and named ClickLock-equivalent utilities, measurable via Windows Defender Antimalware Engine telemetry on common tool signatures.
- **Accessibility user feedback.** Disability:IN, AbleGamers, and Microsoft Accessibility team channels.
- **Hand-strain support tickets to OEMs.** Anecdotal, but tractable through Insider Hub feedback categorization.
- **Steam hardware survey corollaries.** Steam tracks accessibility settings adoption on Windows; trendline post-launch is a public signal.

---

## 8. Reference implementation

A complete working implementation of Right-Click Lock for Windows 10/11 is available at:

- **Source:** [github.com/owenpkent/windows-right-click-lock](https://github.com/owenpkent/windows-right-click-lock)
- **Stack:** .NET 9, WinForms, BCL-only (no NuGet dependencies).
- **Size:** ~600 lines of C#.
- **Architecture:** Layered (Native → Hooks → Core → UI). Whitepaper at [docs/whitepaper.md](whitepaper.md).
- **Operational features:** Crash-safe release, single-instance signaling, atomic JSON settings, move-cancel safety overlay, live debug stream, autostart via `HKCU\…\Run`, system-tray resident.

The reference implementation is offered as:

1. A proof of feasibility. The feature works, the UX is right, and the safety overlays are necessary and sufficient.
2. A test corpus. Microsoft's input team can use the same gesture set to validate a native implementation.
3. An evaluation tool. The live debug window in the reference implementation streams every hook event, which is useful for comparing native and user-mode timing.

The author is happy to grant Microsoft a perpetual, royalty-free license to use the reference implementation, the whitepaper, and the UX mockups in any form, with or without attribution, if the feature ships natively.

---

## 9. Ask

1. **Decision** on whether Right-Click Lock is in scope for a future Windows release.
2. If yes: a directed conversation with the Mouse Properties / Settings owners to align on UX surface and the SPI registration.
3. If no: explicit guidance on whether a third-party Control Panel applet (the planned v1.2 of this project) will be supported by the Mouse Properties / Settings teams, or whether they would prefer the feature stay in user-space tray utilities.

The author can be reached at redacted@example.invalid and is available for a call at Microsoft's convenience.

---

## Appendix A. Third-party fallback plan

If the feature does not ship natively, the project will pursue the following integration path to maximize discoverability without depending on Microsoft cooperation:

1. **Visual mimicry.** Restyle the settings form to match the Mouse Properties dialog (Segoe UI Variable, card sections, exact button placement). Adds a Start-menu shortcut so Win+S "mouse" surfaces the tool.
2. **Custom Control Panel applet (`.cpl`).** A C++ shim DLL implementing `CPlApplet`, registered under `HKCU\Software\Microsoft\Windows\CurrentVersion\Control Panel\Cpls`, surfacing "Right-Click Lock" alongside "Mouse" in classic Control Panel and in Settings search.
3. **Code signing** (EV cert) to clear SmartScreen and reduce AV friction.
4. **Single-instance named-pipe IPC** so the CPL can surface the running tray instance's settings form rather than launching a duplicate.

This plan is documented in detail in the project's internal integration plan and is offered here as context: the project will ship something either way; native is strictly better for users.

## Appendix B. State machine

The reference implementation's right-click-lock state machine, lifted from `whitepaper.md` §4:

```
        [Idle]
          | RMB DOWN (physical)
          v
       [Holding] -- MOUSEMOVE > deadzone --> [Cancelled] --> [Idle]
          | timer >= threshold
          v
        [Armed]
          | RMB UP (physical)
          v
        [Locked] -- next RMB DOWN --> [Idle]
```

The implementation is approximately 80 lines of C# in `RightClickLockController.cs`. A native implementation would presumably collapse this into the existing ClickLock state machine, parameterized by which button is locked.

## Appendix C. Contact

- **Author:** Owen Kent
- **Email:** redacted@example.invalid
- **GitHub:** [github.com/owenpkent](https://github.com/owenpkent)
- **Project:** [github.com/owenpkent/windows-right-click-lock](https://github.com/owenpkent/windows-right-click-lock)
