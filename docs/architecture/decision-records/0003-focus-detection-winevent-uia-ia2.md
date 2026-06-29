# ADR-0003: Field detection via SetWinEventHook + UI Automation + IAccessible2 (Grammarly model)

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-29

## Context

The defining feature is a Grammarly-style badge that appears beside the focused text field across
arbitrary apps. The hard problem: modern messengers are not native-control apps — current WhatsApp
Desktop is a **WebView2/Chromium** wrapper, Telegram is **Qt**, Discord/Slack are **Electron**.
Chromium-family apps build their accessibility tree **lazily**, only after an assistive-technology
client attaches, so the text field can be invisible to UI Automation until triggered.

Grammarly's installed binaries were inspected locally to learn how it solves this exact problem.

## Decision

Replicate Grammarly's technique:

1. **Trigger** — a global `SetWinEventHook` on a dedicated thread for `EVENT_OBJECT_FOCUS`,
   `EVENT_SYSTEM_FOREGROUND`, `EVENT_OBJECT_LOCATIONCHANGE` (`WINEVENT_OUTOFCONTEXT |
   WINEVENT_SKIPOWNPROCESS`). Cheap filtering by PID/exe (own process + blocklist dropped).
2. **Resolve** — the **modern COM UI Automation** (`IUIAutomation`), not the legacy managed
   `System.Windows.Automation`. Accept `Edit`/`Document` elements with `TextPattern`(2) or a writable
   `ValuePattern`; skip password/read-only/disabled.
3. **Wake Chromium** — replicate Grammarly's `ChromiumAccessibilityEnabler`: call
   `AccessibleObjectFromWindow` for **IAccessible2** on the render-widget window to force the
   renderer's accessibility tree on, then read the editable + caret via IA2/UIA.
4. **Anchor** — caret rect via `TextPattern2.GetCaretRange().GetBoundingRectangles()`, else element
   `BoundingRectangle`, else `GetGUIThreadInfo` caret, else foreground-window rect; DPI-correct to
   DIPs; per-`exe` offset calibration table (Grammarly's `ButtonPositions.json` model).
5. **Guaranteed path** — a configurable **global hotkey** always works regardless of detection,
   so auto-appearance can be best-effort per app.

## Alternatives considered

- **UIA `AutomationFocusChangedEventHandler` as the trigger** — cross-process COM, can block; better
  used as the resolver, not the high-frequency trigger.
- **Pure UIA without IA2** — fails on Chromium/WebView2 until accessibility is woken; insufficient
  for the primary WhatsApp target.
- **Hotkey only (no badge)** — simpler, but doesn't deliver the Grammarly-like auto-appearance the
  user asked for. Kept as the reliability backbone, not the whole UX.

## Consequences

- Need COM interop: `Microsoft.Windows.CsWin32` for Win32 P/Invoke; an `IUIAutomation` interop; an
  IAccessible2 interop. The `WINEVENTPROC` delegate must be kept alive (static/`GCHandle`).
- Never run UIA resolution on the hook callback/UI thread without a timeout (cross-process COM can
  hang). Filter by own PID first to avoid feedback loops from our own windows.
- Per-app fragility is expected (Grammarly ships a calibration table for the same reason). Qt
  (Telegram) may expose only element bounds, not a caret — acceptable.
- Forcing Chromium accessibility adds CPU cost to the target app — a known, documented trade-off.

## Validation (2026-06-29, on the dev machine)

A UIA tree-walk probe + a deep read of Grammarly's `Resources/Configuration/IntegrationOptions.json`
confirmed the model and drove these **refinements actually implemented** (M2):

- **App identity = the foreground top-level window, not the focus-event hwnd.** Modern WhatsApp is a
  WinUI shell **`WhatsApp.Root.exe`** (`WinUIDesktopWin32WindowClass`) hosting a `WebView2`; the focus
  event's window often belongs to `msedgewebview2.exe`. We resolve the app (and the injection target)
  from `GetForegroundWindow()` so it reads `WhatsApp.Root.exe`, while the field comes from UIA's
  system-wide focused element.
- **Allowlist/blocklist entries are regex "monikers"** matched against the process file name
  (Grammarly's `Moniker`). Defaults: `whatsapp`, `telegram` (so `WhatsApp.exe` *and* `WhatsApp.Root.exe`
  match). See [configuration.md](../../reference/configuration.md).
- **Managed `System.Windows.Automation` was used** (not COM `IUIAutomation`) — it needs no NuGet
  (offline build) and, on this build, resolved both targets without an explicit IA2 wake:
  - WhatsApp field: `ControlType.Edit`, Name `Type a message to <chat>`, patterns **Value(read-only)+Text**.
  - Telegram field: `ControlType.Edit`, ClassName `Ui::InputField::Inner`, patterns Value+Text.
  The explicit **IAccessible2 enabler stays a documented fallback** for apps whose UIA tree is empty.
- **`IsEditable` accepts an `Edit` with a read-only `ValuePattern` when it has a `TextPattern`** — the
  WhatsApp Chromium contenteditable case (a strict writable-Value check wrongly rejected it).
- **UIA resolution runs off the pump thread with a timeout** (M2 review fix), per the Consequences note.
- **Per-app `Offset`/`Corner`** mirror Grammarly's per-app `DefaultPosition` (observed e.g. Slack
  `TopRight, -20,-25`; Gmail body `TopRight, 10,-20`); ours live in `appOffsets`.
- Diagnostics: set `AITR_FOCUS_LOG=1` to trace the watcher's decisions to `%TEMP%\ai-translator-focus.log`.

## Sources

- Local DLL evidence: `Grammarly.Env.WinEvents.dll` (`SetWinEventHook`),
  `Grammarly.Desktop.Attachment.UIAutomation.dll` (`IUIAutomationTextPattern2`, `GetCaretRange`,
  `GetBoundingRectangles`), `Grammarly.Desktop.Attachment.Accessible2.dll`
  (`ChromiumAccessibilityEnabler.EnableChromiumAccessibility`, `GetIAccessible2`),
  `ButtonPositions.json` / `Blocklist.json` (per-app anchor + suppression).
- `SetWinEventHook` — https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwineventhook
- UIA TextPattern — https://learn.microsoft.com/dotnet/framework/ui-automation/ui-automation-textpattern-overview
- Chromium lazy accessibility / WM_GETOBJECT — https://chromium.googlesource.com/chromium/src/+/lkgr/docs/accessibility/overview.md
- WhatsApp → WebView2 wrapper (2025) — https://www.windowslatest.com/2025/07/21/whatsapp-for-windows-11-is-switching-back-to-chromium-web-wrapper-from-uwp-native/
- Grammarly integration requirements (UIA Text Pattern) — https://support.grammarly.com/hc/en-us/articles/10139846131213
