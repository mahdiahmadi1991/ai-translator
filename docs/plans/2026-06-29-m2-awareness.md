# M2 — Grammarly-style Awareness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Make the translation overlay appear **automatically** as a badge beside the focused text field inside allowlisted apps (the Grammarly model), replacing M1's hotkey-only trigger — while keeping the hotkey as the guaranteed fallback.

**Architecture:** A system-wide `SetWinEventHook` (`FocusWatcher`) detects focus/foreground changes; it filters by the foreground exe via the already-built `AppActivationPolicy`. `TargetResolver` classifies the focused UIA element and reads its on-screen rectangle (waking Chromium/WebView2 accessibility via IAccessible2 when needed). A non-activating `BadgeWindow` anchors beside the field; clicking it opens the existing M1 overlay, now targeted at the detected field. Grounded in [ADR-0003](../architecture/decision-records/0003-focus-detection-winevent-uia-ia2.md).

**Tech Stack:** .NET 10, WPF, CsWin32 (WinEvent hooks), UI Automation COM (`IUIAutomation`), IAccessible2 interop.

## Global Constraints

- **Prerequisite:** M1 must be **built and verified on Windows** first ([windows-build-checklist.md](../guides/windows-build-checklist.md)). M2 is additive on top of a working M1.
- **Platform:** all new code is `net10.0-windows` (Infrastructure/App) — builds/verifies **only on Windows**. The pure-logic pieces already landed in Core ([§ Foundation](#task-0-foundation-done)).
- **Reliability ladder:** the global hotkey stays the guaranteed path; badge auto-appearance is best-effort per app (ADR-0003). Never regress the M1 hotkey path.
- **No focus theft:** the badge is `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`; never foreground-steal from the messenger.
- **Verification:** Win32/UIA behavior is verified by manual run on Windows + Accessibility Insights / `Inspect.exe`, per app. Pure logic is unit-tested.
- **Git:** branch `feature/m2-awareness` off `develop`; commit per task; promote via squash + `--no-ff`.

---

## Task 0: Foundation (DONE)

Already implemented and unit-tested in Core (commit d10e0ac, suite 31/31):

- `AiTranslator.Core.Awareness.AppActivationPolicy.ShouldActivate(foregroundExe, allowlist, blocklist)` — decides whether to auto-appear for a foreground app.
- `AiTranslator.Core.Input.HotkeyCombination.TryParse(...)` — already used by `HotkeyService`; also reused by the settings hotkey validator (Task 7).

No action — listed so the executor reuses these instead of re-implementing them.

---

## Task 1: WinEvent + process P/Invoke surface

**Files:** Modify `src/AiTranslator.Infrastructure/NativeMethods.txt`

**Interfaces:** Produces CsWin32 P/Invokes for tasks 2–3.

- [ ] **Step 1:** Add to `NativeMethods.txt`:

```
SetWinEventHook
UnhookWinEvent
QueryFullProcessImageName
OpenProcess
CloseHandle
GetGUIThreadInfo
EVENT_OBJECT_FOCUS
EVENT_SYSTEM_FOREGROUND
EVENT_OBJECT_LOCATIONCHANGE
WINEVENT_OUTOFCONTEXT
WINEVENT_SKIPOWNPROCESS
```

- [ ] **Step 2:** Build Infrastructure on Windows; confirm `Windows.Win32.UI.Accessibility` types and the `WINEVENTPROC` delegate generate. Expected: PASS.
- [ ] **Step 3:** Commit (`chore(infra): declare WinEvent + process P/Invoke surface`).

---

## Task 2: FocusWatcher (Infrastructure, Windows)

**Files:** Create `src/AiTranslator.Infrastructure/Awareness/FocusWatcher.cs`; add `IFocusWatcher` to `Core/Abstractions`.

**Interfaces:**
- Produces: `IFocusWatcher` with `event EventHandler<FocusedFieldEventArgs> FieldFocused;` and `event EventHandler FieldUnfocused;`, plus `Start()/Stop()`. `FocusedFieldEventArgs` carries the target HWND, foreground exe, and (from Task 3) the screen rect.
- Consumes: `AppActivationPolicy`, the WinEvent P/Invokes.

- [ ] **Step 1:** Add the abstraction to Core:

```csharp
// Core/Abstractions/IFocusWatcher.cs
namespace AiTranslator.Core.Abstractions;
public sealed record FocusedField(nint WindowHandle, string ExeName, System.Drawing.Rectangle? FieldRect);
public interface IFocusWatcher : IDisposable
{
    void Start();
    void Stop();
    event EventHandler<FocusedField>? FieldFocused;
    event EventHandler? FieldUnfocused;
}
```
> Note: `System.Drawing.Rectangle` keeps Core free of WPF/UIA types while carrying pixel bounds. If you prefer no System.Drawing in Core, add a 4-int `PixelRect` record instead.

- [ ] **Step 2:** Implement `FocusWatcher` on a dedicated STA thread with a message loop. In the `WINEVENTPROC` (kept alive as a static field / `GCHandle`): `GetWindowThreadProcessId(hwnd, out pid)` → resolve exe via `OpenProcess(QUERY_LIMITED_INFORMATION)` + `QueryFullProcessImageName`. Drop our own pid. Call `AppActivationPolicy.ShouldActivate(exe, settings.Allowlist, settings.Blocklist)`; if false, raise `FieldUnfocused`. If true, hand the HWND to `TargetResolver` (Task 3, off the hook thread) and raise `FieldFocused` with the resolved rect. Debounce `EVENT_OBJECT_LOCATIONCHANGE` to keep the badge glued.

- [ ] **Step 3 (Windows verify):** Log focus events; switch between WhatsApp/Telegram/Notepad and confirm `FieldFocused` fires only for allowlisted apps with the right exe, and `FieldUnfocused` otherwise. No feedback loop from our own windows.
- [ ] **Step 4:** Commit.

---

## Task 3: TargetResolver (Infrastructure, Windows — UIA + IAccessible2)

**Files:** Create `src/AiTranslator.Infrastructure/Awareness/TargetResolver.cs`, `Automation/ChromiumAccessibilityEnabler.cs`; add UIA + IA2 interop package refs.

**Interfaces:**
- Produces: `TargetResolver.Resolve(nint hwnd) -> (bool isEditable, Rectangle rect)` — classify + locate the focused editable element.

- [ ] **Step 1:** Add the modern COM UIA interop (`Interop.UIAutomationClient` or `UIAComWrapper`) and an IAccessible2 interop; record both in [source-layout.md](../reference/source-layout.md) approved deps (already listed as M2/M3).
- [ ] **Step 2:** Implement classification: `IUIAutomation.GetFocusedElement()`; accept `ControlType.Edit/Document` with `TextPattern`(2) or writable `ValuePattern`, enabled, keyboard-focusable, not password. Read `CurrentBoundingRectangle`; prefer `TextPattern2.GetCaretRange().GetBoundingRectangles()` for caret precision; fall back to `GetGUIThreadInfo` caret, then the element rect.
- [ ] **Step 3:** Implement `ChromiumAccessibilityEnabler` (replicating Grammarly): `AccessibleObjectFromWindow(renderWidgetHwnd, OBJID_CLIENT, IID_IAccessible2, …)` + `QueryService` for IAccessible2 to wake the renderer's a11y tree, then read the editable + caret. Use for WhatsApp(WebView2)/Electron when UIA is empty.
- [ ] **Step 4 (Windows verify):** With Accessibility Insights open, confirm the focused input + rect resolve for: Notepad (Win32), WhatsApp (WebView2), Telegram (Qt — expect element rect only), Discord (Electron). Document per-app reality in a short note.
- [ ] **Step 5:** Commit.

---

## Task 4: NonActivatingWindow + BadgeWindow (App, Windows)

**Files:** Create `src/AiTranslator.App/Windows/NonActivatingWindow.cs`, `Windows/BadgeWindow.xaml(.cs)`

**Interfaces:**
- Produces: a borderless, `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, topmost badge with `event EventHandler Clicked;` and `ShowAt(Rectangle fieldRect, AppOffset offset)` / `Hide()`.

- [ ] **Step 1:** `NonActivatingWindow : Window` — in `SourceInitialized`, OR `WS_EX_NOACTIVATE (0x08000000) | WS_EX_TOOLWINDOW (0x00000080)` into `GWL_EXSTYLE`; `ShowActivated=false`, `Topmost=true`, `ShowInTaskbar=false`; handle `WM_MOUSEACTIVATE → MA_NOACTIVATE`. (Do NOT add `WS_EX_TRANSPARENT` — the badge must be clickable.)
- [ ] **Step 2:** `BadgeWindow` — a small rounded button-like control (the "translate" glyph). `ShowAt` converts the pixel `fieldRect` to DIPs for the target monitor (PerMonitorV2) and positions at `corner(rect) + offset`. Click raises `Clicked`.
- [ ] **Step 3 (Windows verify):** Show the badge at a fixed rect; confirm it appears on top, does not steal focus from the foreground app, and click fires `Clicked`. Check multi-monitor / mixed-DPI placement.
- [ ] **Step 4:** Commit.

---

## Task 5: Wire awareness into the App

**Files:** Modify `src/AiTranslator.App/App.xaml.cs`, `Composition/ServiceConfiguration.cs`; modify `Windows/OverlayInputWindow.xaml.cs` to accept an explicit target + anchor.

**Interfaces:** Consumes `IFocusWatcher`, `BadgeWindow`, the M1 `OverlayInputWindow`.

- [ ] **Step 1:** Register `IFocusWatcher` + `TargetResolver` in DI. Start the watcher at app startup (gated on `settings.AutoAppearBadge`).
- [ ] **Step 2:** On `FieldFocused`, `BadgeWindow.ShowAt(rect, offset)` and remember the target field (HWND + rect). On `FieldUnfocused`, hide the badge. On badge `Clicked`, open the overlay anchored near the field, targeting the detected field (instead of M1's "capture foreground"). Keep the hotkey path working unchanged.
- [ ] **Step 3:** Overlay change: add `ShowFor(FocusTarget target, Rectangle anchor)` so the badge path passes the resolved target/anchor; the hotkey path keeps the M1 `ShowFor()` (capture foreground).
- [ ] **Step 4 (Windows verify — M2 acceptance):** Focus the WhatsApp input → badge appears beside it → click → overlay opens anchored → type Persian → translation replaces the WhatsApp input. Repeat for Telegram. Hotkey still works in non-allowlisted apps.
- [ ] **Step 5:** Commit.

---

## Task 6: Per-app offset calibration + persistence

**Files:** Create `src/AiTranslator.Core/Awareness/AppOffset.cs` (+ tests); modify `SettingsWindow`, `AppSettings` already has `appOffsets`.

**Interfaces:** Produces `AppOffset(int Corner, int Dx, int Dy)` and a pure resolver `AppOffsets.For(exe, settings) -> AppOffset` with a default. Unit-tested in Core.

- [ ] **Step 1 (TDD, Core):** Test + implement `AppOffsets.For` (per-exe lookup with a sensible default, mirroring Grammarly's `ButtonPositions.json`).
- [ ] **Step 2:** `BadgeWindow.ShowAt` consumes the resolved offset. (Optional) a small drag-to-calibrate that writes back to `settings.appOffsets`.
- [ ] **Step 3:** Commit.

---

## Task 7: Settings + docs sync

- [ ] **Step 1:** `SettingsWindow`: add an allowlist/blocklist editor and validate the hotkey live with `HotkeyCombination.TryParse` (inline "invalid combo" feedback). Add an `AutoAppearBadge` toggle.
- [ ] **Step 2:** Docs: tick M2 in [overview §9](../architecture/overview.md#9-roadmap); update [source-layout.md](../reference/source-layout.md) (Awareness/, BadgeWindow, NonActivatingWindow now real); note per-app UIA reality from Task 3/4 in a short reference. Verify links.
- [ ] **Step 3:** Commit; promote `feature/m2-awareness → develop` (squash + `--no-ff`).

---

## Self-Review

- **Coverage:** auto-appear (Tasks 2–5), Chromium/WebView2 support (Task 3), badge UX (Task 4), per-app calibration (Task 6), settings (Task 7), hotkey fallback preserved (Task 5). The pure decision logic is already tested (Task 0).
- **Deferred to M3:** live-inject without focus flicker; deep Qt/Electron injection tuning; the non-activating overlay (the *overlay* — Task 4 only makes the *badge* non-activating).
- **Verification honesty:** every Windows-only task carries a manual on-Windows verification; only Core logic (Tasks 0, 6) is unit-tested. Per-app behavior (WhatsApp/Telegram) is validated app-by-app, as ADR-0003 requires.
