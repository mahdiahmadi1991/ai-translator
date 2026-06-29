# Architecture overview (design spec)

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-28

The canonical design for **AI Translator**. This is the spec the implementation follows. Decision
*rationale* and alternatives considered live in the [ADRs](decision-records/README.md); this
document describes *what the system is and how its parts fit*. Config keys live in
[reference/configuration.md](../reference/configuration.md); model/pricing facts in
[reference/openai-models.md](../reference/openai-models.md).

Related: [ADR index](decision-records/README.md) · [diagrams](diagrams/component-and-flow.md) ·
[source layout](../reference/source-layout.md)

---

## 1. Purpose & scope

A Windows desktop utility that replicates **Grammarly's in-field assistant UX** for translation.
When the user focuses an editable text field (in any app, except ones they blocklist), a small
**badge** appears beside the field. Clicking it opens a floating **input box**; the user types in
their own language and the translation goes **into the app's real input box**, ready to send.

**In scope:** typed-text translation, multi-language with auto-direction, badge-beside-field auto
appearance in any editable field (opt-out via a blocklist), a manual global hotkey, a settings window,
secure per-user API-key storage.

**Out of scope (now):** voice/speech translation, mobile, translating *incoming* messages, and any
server-side backend. These are noted in [§9 Roadmap](#9-roadmap) but not built first.

## 2. Core behavior (the user-visible contract)

1. **Detect** — A system-wide focus watcher notices when an editable text field gains focus in any
   app, except ones the user has blocklisted. Password and read-only fields are skipped.
2. **Badge** — A small always-on-top badge window anchors next to the field (bottom-right corner by
   default, with a per-app offset). It tracks the field as the window moves/scrolls and disappears
   when focus leaves the field. A configurable **global hotkey** is an always-available alternative
   to clicking the badge.
3. **Open input box** — Clicking the badge (or pressing the hotkey) opens a simple floating input
   box anchored near the field. The box never steals foreground focus from the messenger (it is a
   `WS_EX_NOACTIVATE` tool window) but still accepts typing.
4. **Type & translate** — The user types source text. After a **500 ms idle debounce**, the text is
   translated via OpenAI streaming. `Enter` inside the box inserts a **newline** (it does not submit
   or close).
5. **Inject** — Each completed translation **replaces the entire content of the messenger's real
   input box** (select-all + paste), so the messenger box always mirrors the latest translation.
   The app never auto-presses Send — the user reviews and sends using the messenger itself.
6. **Dismiss** — `Esc` or clicking away closes the input box; the badge remains while the field
   stays focused.

The behavior is deliberately modeled on Grammarly, whose Windows desktop integration was inspected
to ground these mechanics — see [ADR-0003](decision-records/0003-focus-detection-winevent-uia-ia2.md).

## 3. High-level shape

A single background WPF process with a tray icon. No backend; the only network call is to the
OpenAI API. Three cooperating concerns:

- **Awareness** — watch focus, resolve the target field, anchor UI to it.
- **Interaction** — badge, input box, settings window, global hotkey, tray.
- **Action** — translate (OpenAI streaming) and inject text into the target.

See the diagram in [diagrams/component-and-flow.md](diagrams/component-and-flow.md).

## 4. Components (one responsibility each)

Each is a small, independently testable unit. Win32/UIA/clipboard interop is isolated behind
interfaces so the core logic is unit-testable without a live desktop.

| Component | Responsibility | Key dependencies |
| --- | --- | --- |
| `FocusWatcher` | System-wide focus/foreground/location hook on a dedicated STA thread; emits "an editable field in an allowlisted app was focused/moved" events. Filters out our own process and blocklisted apps. | `SetWinEventHook` (via CsWin32) |
| `TargetResolver` | Resolves the focused element to an editable text target: classifies it (Edit/Document, TextPattern/ValuePattern, not password/read-only), reads its bounding/caret rectangle, and force-enables Chromium/WebView2 accessibility (IAccessible2) when needed. Caches the target HWND/element. | UI Automation COM (`IUIAutomation`), IAccessible2 |
| `BadgeWindow` | The always-on-top, non-activating badge anchored to the target rectangle (corner + per-app offset, DPI-correct). Raises "user invoked translation". | WPF, `WS_EX_NOACTIVATE` interop |
| `OverlayInputWindow` | The simple floating input box (source text). Non-activating but typeable; RTL-aware; routes keystrokes (`Enter`=newline, `Esc`=close). | WPF, WPF-UI theme |
| `HotkeyService` | Registers a configurable system-wide hotkey (`RegisterHotKey`/`WM_HOTKEY`); reports registration failures so the user can rebind. | Win32 hotkey interop |
| `TranslationService` | Streams a translation from OpenAI: builds the prompt, debounces input, cancels the in-flight request when input changes, surfaces partial/final text. | `OpenAI` SDK (Responses API) |
| `TextInjector` | Places translated text into the target field: select-all + clipboard-paste primary, `ValuePattern.SetValue` fast path, `SendInput` Unicode fallback; saves/restores the user's clipboard; restores foreground focus (AttachThreadInput). | Win32 clipboard/SendInput, UIA |
| `LanguageDirector` | Holds the configured language pair and resolves direction (auto-detect source → translate to the other). | — (pure logic) |
| `SettingsStore` | Loads/saves non-secret settings JSON in `%APPDATA%\AI-Translator\`. Schema-versioned. | file I/O, `System.Text.Json` |
| `SecretStore` | Reads/writes the OpenAI key in Windows Credential Manager; first-run capture flow. | Credential Manager |
| `AppShell` (tray) | Process lifetime, tray icon/menu, run-at-startup, opening the settings window, wiring the components. | `H.NotifyIcon.Wpf` |
| `SettingsWindow` | Full settings UI: language pair, target/source, model, hotkey, app allowlist/blocklist, API key. Launched from the badge/overlay and tray. | WPF, WPF-UI |

## 5. Primary data flow

```
focus change ─▶ FocusWatcher ─▶ TargetResolver ─▶ (editable? allowlisted?) ─▶ BadgeWindow shows
                                                            │
user clicks badge / presses hotkey ─────────────────────────┘
        ▼
OverlayInputWindow opens ─▶ user types ─▶ [debounce 500ms] ─▶ TranslationService.StreamAsync
                                                                      │ (cancel previous)
                                                                      ▼
                                            partial deltas rendered; on complete:
                                                                      ▼
                          TextInjector replaces messenger field content (select-all + paste)
```

Detailed sequence: [diagrams/component-and-flow.md](diagrams/component-and-flow.md).

## 6. Field detection & anchoring (the hard part)

Grounded in an inspection of Grammarly's own Windows integration ([ADR-0003](decision-records/0003-focus-detection-winevent-uia-ia2.md)).

- **Trigger:** a single global `SetWinEventHook` for `EVENT_OBJECT_FOCUS`, `EVENT_SYSTEM_FOREGROUND`,
  and `EVENT_OBJECT_LOCATIONCHANGE`, `WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS`, on a thread
  with a message loop. The callback does only cheap work (HWND → PID → exe; drop own/non-allowlisted).
- **Resolve:** off the hook thread, use the **modern COM UI Automation** (`IUIAutomation`, not the
  legacy managed wrapper). Accept the element if it is `Edit`/`Document` with `TextPattern`(2) or a
  writable `ValuePattern`, is enabled, keyboard-focusable, and **not** a password field.
- **Chromium/WebView2/Electron (covers current WhatsApp):** these expose their text inputs only once
  an assistive-technology client attaches. We replicate Grammarly's `ChromiumAccessibilityEnabler`:
  call `AccessibleObjectFromWindow` for **IAccessible2** on the render-widget window to wake the
  renderer's accessibility tree, then read the editable + caret via IA2/UIA.
- **Anchor:** prefer `TextPattern2.GetCaretRange().GetBoundingRectangles()` for caret precision,
  else the element `BoundingRectangle`, else `GetGUIThreadInfo` caret, else the foreground window
  rect. Convert physical pixels → WPF DIPs per the target monitor (PerMonitorV2). A small per-`exe`
  offset table calibrates placement, exactly as Grammarly's `ButtonPositions.json` does.
- **Reliability ladder:** the **global hotkey is the guaranteed path** and always works; precise
  badge auto-appearance is best-effort per app and may need per-app tuning. Qt apps (Telegram) and
  some renderers may only expose the element rectangle, not a caret — that is acceptable.

## 7. Translation pipeline

See [ADR-0002](decision-records/0002-translation-openai-responses-streaming.md) and
[reference/openai-models.md](../reference/openai-models.md).

- Official `OpenAI` .NET SDK, **Responses API streaming** (not the audio-only Realtime API).
- One `CancellationTokenSource` per in-flight request; on each post-debounce change, cancel+dispose
  the previous and start fresh, so only the latest input is translated.
- A strict system prompt returns **translation only** (no chatter), with the configured language
  pair injected and **auto-direction** detection. Plain text output (no JSON wrapping).
- Default model is configurable; see the reference doc for current options and the live-verify note.

## 8. Cross-cutting concerns

- **Settings & secrets:** non-secret config is JSON under `%APPDATA%\AI-Translator\`
  ([reference/configuration.md](../reference/configuration.md)); the API key is in Windows Credential
  Manager ([ADR-0005](decision-records/0005-secret-storage-credential-manager.md)). The key is never
  in the repo, code, or committed config.
- **Localization:** English is the source language; all user-facing strings go through resources. No
  hard-coded non-English UI text.
- **Logging:** structured local logging with secrets redacted (`***`); never log key or message text
  at info level.
- **Distribution:** Velopack installer + silent auto-update
  ([ADR-0006](decision-records/0006-distribution-velopack.md)).
- **Security & honest limits:**
  - Cannot inject into windows running **as Administrator** unless the app is elevated too (UIPI).
  - Global focus hook + `SendInput` resemble keylogger/injector patterns and can trip AV/SmartScreen
    heuristics → **Authenticate-sign the release**.
  - Credential Manager / DPAPI protect against other users and at-rest theft, **not** same-user
    malware; the key is user-supplied and revocable.
  - Forcing Chromium accessibility adds CPU overhead to the target app (a known Chromium cost).

## 9. Roadmap

1. **M0 — Workspace & design** (done): repo, standards-compliant docs, ADRs, this spec.
2. **M1 — Walking skeleton** (built on Windows): tray app + settings + Credential Manager key +
   manual-hotkey overlay + OpenAI streaming translation + clipboard injection into the *currently
   focused* field (no badge yet). Proves the end-to-end path on Win32 targets.
   *Status:* all layers **build green on Windows** (`dotnet build` 0/0, `dotnet test` 40/40) and the
   app launches; the remaining gate is the manual runtime acceptance test —
   [windows-build-checklist.md](../guides/windows-build-checklist.md). Plan:
   [plans/2026-06-28-m1-walking-skeleton.md](../plans/2026-06-28-m1-walking-skeleton.md).
3. **M2 — Grammarly-style awareness** (built on Windows): `FocusWatcher` (SetWinEventHook) +
   `TargetResolver` (managed UIA) + non-activating `BadgeWindow`; allowlist/blocklist editor;
   per-app offset calibration (`appOffsets`). *Status:* machinery built (0/0) and the watcher runs;
   per-app badge behaviour (WhatsApp/Telegram) is validated app-by-app.
   Plan: [plans/2026-06-29-m2-awareness.md](../plans/2026-06-29-m2-awareness.md).
4. **M3 — Chromium/WebView2 + Qt coverage:** IAccessible2 enable path for WhatsApp(WebView2)/Electron
   (if UIA proves insufficient); Telegram(Qt) anchoring; non-activating overlay + live-replace
   injection tuning (no focus flicker).
5. **M4 — Hardening & release:** logging, error states, code signing, Velopack auto-update.

Each milestone gets its own plan via the writing-plans workflow before code is written.
