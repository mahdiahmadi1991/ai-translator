# ADR-0004: Text injection via clipboard-paste primary, with fallbacks

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-28

## Context

The translation must land in the messenger's real input box, and — because translation is live —
each update must **replace** the box's current content. No single API works across Electron/Chromium,
Qt, and WinUI inputs: `ValuePattern.SetValue` is unsupported on the rich/`contenteditable` composers
these apps use. Grammarly's binaries show a clipboard-paste pipeline as its primary insertion method.

## Decision

Layered injection, replicating Grammarly's approach:

1. **Primary — clipboard paste with replace:** snapshot the user's clipboard (all formats, not just
   text) → set the translated text → restore foreground/focus to the cached target HWND
   (`AttachThreadInput` + `SetForegroundWindow`, detach in `finally`) → `SendInput` **Ctrl+A** then
   **Ctrl+V** to replace the whole field → verify via `GetClipboardSequenceNumber` with retry →
   restore the original clipboard after a delay (~150–300 ms) so the target has read it.
2. **Fast path — `ValuePattern.SetValue`** when the focused UIA element supports it (simple native
   edits / new WinUI WhatsApp): atomic, clipboard-free.
3. **Last resort — `SendInput` `KEYEVENTF_UNICODE`** typing (iterate UTF-16 code units so emoji
   surrogate pairs work) when paste is blocked.

**Never auto-send.** Injection places text ready-to-send; the user presses the messenger's own Send.

## Alternatives considered

- **`ValuePattern.SetValue` as primary** — cleanest, but throws/no-ops on Electron/Qt contenteditable
  composers; only a fast path.
- **Pure `SendInput` typing as primary** — slow, fights IME/autocomplete, RTL reordering risk. Last
  resort only.

## Consequences

- Clipboard work runs on an **STA** thread with retry (`CLIPBRD_E_CANT_OPEN 0x800401D0` is common).
- Restoring the clipboard too early pastes empty/stale text → must delay and verify the sequence id.
- `AttachThreadInput` must always detach in `finally` or the UI can hang.
- Live-replace cadence is driven by the 500 ms debounce, not every keystroke, to limit paste churn.
- Cannot inject into elevated windows unless elevated (UIPI) — see [overview §8](../overview.md#8-cross-cutting-concerns).

## Sources

- Local: `Grammarly.Desktop.Attachment.dll` clipboard pipeline (`ClipboardPasteInsertion`,
  `EnableClipboardSequenceIdCheck`, `CopyByKeyboardShortcutBlocking`, `EnableNoClipboardFallback`),
  `Grammarly.Env.Keyboard.dll` (`SendInput`).
- KEYEVENTF_UNICODE / surrogate pairs — https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-keybdinput
- TextPattern elements don't support ValuePattern — https://learn.microsoft.com/dotnet/framework/ui-automation/ui-automation-textpattern-overview
- SetForegroundWindow + AttachThreadInput pattern — https://weblog.west-wind.com/posts/2020/Oct/12/Window-Activation-Headaches-in-WPF
