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

## Addendum (2026-07-13): two races that made it paste the wrong text

Reported and reproduced: the user dictated Persian, pressed Translate, and their own Persian text
appeared in the chat instead of the English translation. A sentinel on the clipboard proved what was
happening, and there were two distinct defects, both now fixed in `ClipboardTextInjector`:

1. **The set was allowed to fail silently.** Another app can hold the clipboard open (a clipboard
   manager, the target itself), so `Clipboard.SetText` fails. The old code swallowed that and sent
   `Ctrl+V` anyway, which pasted whatever was on the clipboard BEFORE, which is how the user's own
   source text reached the chat. The injector now writes, **reads back to confirm**, and throws
   `TextInjectionException` rather than pasting on trust.
2. **The restore raced the paste.** The target reads the clipboard when *it* processes the paste, which
   can be long after `SendInput` returns. Restoring the user's clipboard on a short fixed timer meant
   the target sometimes read the restored (old) content. The restore now waits generously, runs off the
   caller's path so the box still dismisses at once, and is skipped if anything else has taken the
   clipboard meanwhile.

Related: the keystrokes are now sent only once the target really holds the foreground (polled, not
assumed), and the compose box hides *before* injecting, because while it held the foreground Windows
could refuse to hand it over and the paste landed in the box itself. If the target never comes forward,
the injector throws rather than pasting blindly.

On any injection failure the caller keeps the draft, brings the box back, and asks the user to press
Translate again. Losing a translation silently, or inserting the wrong text, are both worse than a
visible retry.

## Addendum (2026-07-13): the clipboard must never be touched from the UI thread

The fix above made the injector correct but left it running its clipboard work inline on the caller's
thread, which is the WPF UI thread. That is the thread that paints the compose box and accepts typing.

The Win32 clipboard is one global lock that any process may hold open, and WPF's `Clipboard` hides that
behind an internal retry loop built on `Thread.Sleep`. Measured cost to the UI thread of a single
injection, on an otherwise idle machine:

| | blocked the UI thread for |
| --- | --- |
| set + read-back verify | 143 ms, 211 ms, **7430 ms** across three runs |
| restore the user's clipboard | 10 ms, 1 ms, **23024 ms** across three runs |
| set, with a rival holding the board open | **2546 ms** |

For that whole time the box cannot repaint or take a keystroke, which is exactly the "the box got slow
and janky" the owner reported.

All clipboard access now goes through `StaClipboard`, which runs each operation on its own short-lived
STA thread (the apartment the clipboard requires anyway) and is **awaited**, so the UI thread is never
blocked. Verified by running a real WPF `Dispatcher` with a 10 ms timer during a contended injection and
recording the longest gap between ticks:

| | longest UI-thread stall |
| --- | --- |
| harness noise floor (no clipboard work at all) | 319 ms |
| before: clipboard inline on the UI thread | 1186 ms, 2269 ms |
| after: `StaClipboard`, awaited | 220 ms (at the noise floor) |

The retry budget is now time-based (5 s) rather than a fixed attempt count, because a generous wait costs
the user nothing once it no longer freezes the UI.

### The failure paths owe the user their clipboard back

Making the injector throw rather than paste blindly (previous addendum) introduced a defect of its own,
caught in a pre-release audit: the throw sat **between** the clipboard write and the restore, with no
`try`/`finally`. So when the target never took the foreground, the user's clipboard was destroyed and
their private translation was left on the global board for any process to read.

Once our text is on the clipboard, **every** exit path now restores it, and a board the user had left
empty is emptied again rather than keeping the translation. Verified by forcing the real failure (a target
window that never comes forward):

| Clipboard before | Injection | Clipboard after | Translation left on the board |
| --- | --- | --- | --- |
| the user's own text | fails | the user's own text | no |
| empty | fails | empty | no |
