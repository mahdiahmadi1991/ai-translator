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
- **Opt-out activation:** the badge appears in any editable field; the only filter is a `blocklist`
  of regex "monikers" matched against the process file name (Grammarly's `Moniker`). The earlier
  allowlist was removed — users expect translation everywhere, not just messengers. See
  [configuration.md](../../reference/configuration.md).
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

## Validation (2026-06-30, on the dev machine) — the WhatsApp "separate window" finding

The badge worked nearly everywhere but **never in WhatsApp**. A throwaway UIA probe run directly
against the live process (`scratchpad/wa-probe`, no keyboard focus needed) pinned down why and drove
the implemented fix:

- **WhatsApp Business hosts its chat in a SEPARATE top-level window, not a child of the foreground.**
  The foreground is `WhatsApp.Root.exe` (`WinUIDesktopWin32WindowClass`); its only Chromium child is a
  **0×0 placeholder** `Chrome_WidgetWin_0`, and its UIA tree has **zero** editable elements. The chat
  actually lives in an independent top-level window — class `Chrome_WidgetWin_1`, title
  `"WhatsApp Business"`, in an `msedgewebview2.exe` process — with **no owner/parent** link to the
  foreground window. `EnumChildWindows(foreground)` therefore can never reach it, which defeated every
  child-only approach.
- **The reliable link is process ancestry.** WebView2 spawns `msedgewebview2.exe` as a **child process**
  of the host app (verified: the webview window's PID's parent is `WhatsApp.Root.exe`). The resolver now
  also scans **top-level `Chrome_*` windows whose process belongs to the foreground app's process family**
  (computed from a `CreateToolhelp32Snapshot` parent map), collects their render widgets, and drills each.
- **The field is reachable and editable once the right window is drilled.** Under the render widget
  (`Chrome_RenderWidgetHostHWND`), `FindFirst(TreeScope.Descendants, HasKeyboardFocus=true)` returns
  exactly the message box: `ControlType.Edit`, Name `Type a message to <chat>`, a **writable**
  ValuePattern and a TextPattern, with a valid `BoundingRectangle`. (So the earlier note that Value is
  read-only was specific to an older build; the current contenteditable exposes a *writable* Value.)
- **`HasKeyboardFocus` is set even though the webview window is not the OS foreground** — Chromium reports
  DOM focus regardless — so drilling the render widget's subtree for the focused element is sound.
- **Implementation:** `TargetResolver` wakes each candidate render widget via
  `AccessibleObjectFromWindow(OBJID_CLIENT)` (the MSAA handshake), tries the OS-wide focus first, then
  drills. A freshly woken renderer builds its tree asynchronously, so `Resolve` is **non-blocking** and
  returns a `FieldStatus.Pending`; `FocusWatcher` then runs a bounded, self-driven `DispatcherTimer`
  retry instead of tearing the badge down or depending on a foreground-thread event that never fires for
  the separate render thread. (Hardened after a multi-agent adversarial review of the first cut.)
- Diagnostics are **on by default** during this work (`AITR_FOCUS_LOG=0` disables); they log the focused
  element + each drilled candidate. To be gated back off once WhatsApp is confirmed in normal use.

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
