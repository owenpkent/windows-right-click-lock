# Right-Click Lock: Proposal for Native Windows Integration

**Version:** 0.1
**Author:** Owen Kent ([linkedin.com/in/owenpkent](https://www.linkedin.com/in/owenpkent/))
**Reference implementation:** [github.com/owenpkent/windows-right-click-lock](https://github.com/owenpkent/windows-right-click-lock)
**Audience:** Windows Input, Devices, Settings, and Accessibility teams at Microsoft

---

## Executive summary

Windows has shipped **ClickLock**, a tap-and-hold-to-lock primary mouse button feature, since Windows 2000. There is no equivalent for the secondary (right) button. The asymmetry has a real user cost: sustained right-click is the de-facto camera gesture across modern PC games (MMOs, ARPGs, RTS, builder/sandbox titles, flight sims), and accessibility users with limited grip strength need symmetric button-locking for the same reasons that motivated ClickLock in the first place.

This proposal asks Microsoft to ship **Right-Click Lock** through one of two paths: (1) natively in Mouse Properties (classic Control Panel) and Settings → Bluetooth & devices → Mouse, mirroring the existing ClickLock UX; or (2) as a new module in **PowerToys**, alongside the existing Mouse utilities. Native is the stronger fit for the accessibility argument and for symmetry with ClickLock; PowerToys is the faster path with distribution and signing already solved. A complete user-mode reference implementation already exists (~600 LOC, .NET 9, BCL-only) that demonstrates feasibility, validates the UX, and surfaces the operational concerns (move-cancel, crash-safe release, hook ordering) either path would need to address. If neither path lands, the project will continue to ship the existing signed tray utility as a self-release; that fallback is described in Appendix A.

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
- **Third-party utilities** (including this project's reference implementation). Operate via `WH_MOUSE_LL` + `SendInput` injection, must each warm up SmartScreen reputation per binary (even when signed with an EV certificate), and depend on hook ordering that can be disrupted by other utilities or system updates.

None of these are appropriate for the broad user base who would benefit. The feature belongs in the OS.

---

## 2. Proposal: two paths to ship in Microsoft

The ask is for Microsoft to absorb Right-Click Lock through one of two surfaces. The behavior specification (§2.3 to §2.5) is identical for both. The author's preference is the native path for the symmetry-with-ClickLock and accessibility reasons; PowerToys is presented as a credible alternative if native scoping is harder than the contributor cadence PowerToys allows.

### 2.1 Path A: native in Mouse Properties and Settings

Add a **Right-Click Lock** section to the Buttons tab in Mouse Properties, structurally identical to the existing ClickLock section:

- A "Turn on Right-Click Lock" checkbox.
- A "Settings…" button opening a modal with a hold-duration slider (Short ↔ Long), matching the existing ClickLock settings dialog.
- A two-line description explaining the feature.

Mirror the same option in **Settings → Bluetooth & devices → Mouse**, alongside the existing primary-button options.

This is the strongest fit for the user populations identified in §1.2. The accessibility audience that motivated ClickLock looks here; gamers and productivity users searching "mouse" find ClickLock today and would find Right-Click Lock right next to it. Native is also the only path that delivers the anti-cheat exemption discussed in §3.

### 2.2 Path B: PowerToys module

Submit Right-Click Lock to [microsoft/PowerToys](https://github.com/microsoft/PowerToys) as a new module under the existing **Mouse utilities** category (Find My Mouse, Mouse Highlighter, Mouse Pointer Crosshairs, Mouse Jump). The module would expose a settings card in the PowerToys Settings UI with the same controls as the reference implementation: master toggle, hold-duration slider, move-cancel toggle and threshold, plus a per-app exclusion list using the existing PowerToys excluded-apps pattern. An optional toggle hotkey would slot into PowerToys' centralized hotkey manager.

Why this path is credible:

- **Distribution and signing solved.** PowerToys ships as a single signed MSIX bundle through GitHub Releases, the Microsoft Store, and `winget`. No per-vendor SmartScreen warm-up.
- **Established mouse-utility category.** Four existing modules establish that low-level pointer/button manipulation is in scope. Right-Click Lock is structurally smaller than any of them.
- **Enterprise story for free.** PowerToys ships with a Group Policy template (`PowerToysGpoTemplate.admx`) for enabling/disabling individual modules; the new module inherits that mechanism.
- **Documented contribution flow.** `CONTRIBUTING.md` and `doc/devdocs/` lay out the new-module submission process: feature-request issue, triage, design review, PR.

Trade-offs vs. Path A: PowerToys does not reach users who don't install it (most of the accessibility population, much of the gaming population), it doesn't deliver an anti-cheat exemption, and it does not address the symmetry-with-ClickLock argument that motivates the proposal in the first place. The native path remains preferred for those reasons; PowerToys is the right call if the native release cadence is incompatible with the timeline Microsoft is willing to commit to.

Implementation note: the reference implementation's Core layer (state machine, settings model) is pure C# with no UI dependency and would drop into a PowerToys module project unchanged. The Hooks and Native layers are functionally equivalent to what `FindMyMouse.cpp` already does. Estimated porting effort is one contributor week, plus PowerToys triage and review (typically 2 to 8 weeks for a new module of this size, based on observed cadence in the public PR history).

### 2.3 Behavior specification

Identical pattern to ClickLock, applied to the secondary button:

1. User holds RMB physically for ≥ *threshold* milliseconds (default ~500 ms, user-configurable).
2. On physical RMB release, OS continues to assert RMB-down to the foreground window.
3. Next physical RMB click releases the synthetic hold and returns to idle.

### 2.4 Move-cancel safety overlay

The reference implementation surfaces a non-obvious safety requirement: if the user moves the mouse beyond a small dead-zone *before* the lock arms, treat the gesture as a real drag and do **not** lock. This prevents the lock from triggering on legitimate right-button drags or click-releases that happen to span the threshold window. Microsoft should incorporate this overlay in any implementation; it is documented in the reference whitepaper §4.

### 2.5 Crash / session safety

Because a locked secondary button can leave the OS believing RMB is held even after the locking process exits, any implementation needs:

- A guaranteed release on process termination (`atexit` semantics, or kernel-side cleanup if implemented in the input stack).
- A guaranteed release on session lock / fast user switch / sleep.
- A "panic release": pressing any keyboard key, or moving focus to a system overlay, releases the lock.

A native implementation in the input stack avoids these concerns entirely (the OS simply stops asserting the synthetic state). A PowerToys implementation operates at user-mode and inherits the same engineering burden the reference implementation already addresses. Either way, the requirements are non-trivial and pre-validated by the reference code.

---

## 3. Why this belongs in Microsoft's surface area (vs leaving to ISVs)

| Concern | Third-party self-release | PowerToys module | Native (Mouse Properties / Settings) |
|---|---|---|---|
| Anti-cheat compatibility | Routinely false-flagged | Routinely false-flagged | Built-in, exempt |
| Code signing / SmartScreen | Per-vendor cert, reputation cold-start | Trusted (PowerToys MSIX) | Already trusted |
| Hook ordering / interaction with other utilities | Fragile | Same hook layer, slightly less fragile in practice | Implemented at the input source |
| Crash-safe release | Requires careful per-app engineering | Same engineering, scoped to the module | Implicit |
| Per-app exclusion model | Each ISV reinvents | Inherits PowerToys excluded-apps pattern | Inherits Focus Assist |
| Enterprise GPO disable | Custom (would need authoring) | Inherited from PowerToys | Native admin templates |
| Discoverability | Search engines, word of mouth | Users who install PowerToys | Mouse Properties + Settings, where users already look |
| Accessibility audit | Per-vendor, inconsistent | Inherits PowerToys accessibility review | Inherits Microsoft accessibility process |
| Reaches users who don't install PowerToys | Yes (anyone can download) | No | Yes (everyone with Windows) |

The feature has the characteristics that historically motivate Microsoft to absorb third-party tooling into Windows: it is small, broadly useful, addresses an existing accessibility asymmetry, has a clean UX precedent (ClickLock), and is currently served by a fragmented landscape of unsigned utilities and AutoHotkey scripts that present support and security friction users should not need to navigate. Either of the two Microsoft-side paths in §2 substantially improves on the third-party-self-release status quo; the native path improves on it the most.

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

1. **Decision** on whether Right-Click Lock is in scope for either Microsoft-side path described in §2.
2. **If native (Path A):** a directed conversation with the Mouse Properties / Settings owners to align on UX surface and the SPI registration.
3. **If PowerToys (Path B):** a green light from the PowerToys leadership to open a feature-request issue and submit a draft PR adapting the reference implementation. The author is willing to do the porting work.
4. **If neither:** the project will continue to ship as the existing signed tray utility (Appendix A). Even in that outcome, an explicit "no, please don't" or "yes, but not on our roadmap" is more useful than silence; it lets the project plan its own roadmap accordingly.

The author can be reached via [LinkedIn](https://www.linkedin.com/in/owenpkent/) and is available for a call at Microsoft's convenience.

---

## Appendix A. Self-release fallback

If neither Microsoft-side path in §2 lands, the project continues to ship the existing signed tray utility as a self-release. This is the current state today: a single-file, EV-signed `.exe` published on GitHub Releases, with a built-in tray UI exposing the same controls that any native or PowerToys version would.

The author has no plans to invest further in distribution surface (a custom Control Panel applet, an MSIX, a Microsoft Store listing) absent a Microsoft signal that the feature should land natively or as a PowerToys module. The user-mode self-release reaches the audience that knows to look for it; meaningfully reaching the populations identified in §1.2 (especially the accessibility slice) requires Microsoft to ship it. The reference implementation is offered as proof that the engineering is settled and the UX is right; what remains is the distribution decision, which only Microsoft can make.

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
- **LinkedIn:** [linkedin.com/in/owenpkent](https://www.linkedin.com/in/owenpkent/)
- **GitHub:** [github.com/owenpkent](https://github.com/owenpkent)
- **Project:** [github.com/owenpkent/windows-right-click-lock](https://github.com/owenpkent/windows-right-click-lock)
