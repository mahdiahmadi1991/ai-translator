# Configuration reference

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-28

Non-secret settings persist as JSON at `%APPDATA%\AI-Translator\settings.json`. The API key is **not**
here — it lives in Windows Credential Manager
([ADR-0005](../architecture/decision-records/0005-secret-storage-credential-manager.md)). This page is
the canonical schema; update it in the same change as any settings-shape change.

## `settings.json` schema (v1)

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `schemaVersion` | int | `1` | Migration marker; bumped on breaking shape changes |
| `languagePair.primary` | string (BCP-47) | `"fa"` | One side of the auto-direction pair |
| `languagePair.secondary` | string (BCP-47) | `"en"` | The other side |
| `autoDirection` | bool | `true` | Detect which side the input is in and translate to the other |
| `model` | string | _(see [openai-models.md](openai-models.md))_ | OpenAI model id; configurable, not hard-coded |
| `debounceMs` | int | `500` | Idle delay before translating while typing |
| `hotkey` | string | `"Ctrl+Alt+T"` | Global shortcut to open the input box; rebindable |
| `autoAppearBadge` | bool | `true` | Show the badge automatically in editable fields (everywhere except the blocklist) |
| `selectionTranslator` | bool | `true` | Read mode: show a translate icon when text is selected anywhere ([ADR-0007](../architecture/decision-records/0007-rewrite-styles-and-humanizer.md) is compose-mode; read mode is the selection feature) |
| `selectionHotkey` | string | `"Ctrl+Alt+S"` | Global shortcut that translates the current selection (the guaranteed read-mode path) |
| `rewriteStyle` | enum | `"Original"` | **Default** compose-box style, used for apps with no remembered choice: `Original` \| `Professional` \| `Formal` \| `Friendly` \| `Email` \| `Concise` \| `Expand` ([ADR-0007](../architecture/decision-records/0007-rewrite-styles-and-humanizer.md)) |
| `appStyles` | map | `{}` | Per-exe rewrite style — each app remembers the style last used in it, e.g. `{ "ms-teams.exe": "Formal", "chrome.exe": "Friendly" }`. Falls back to `rewriteStyle` ([ADR-0008](../architecture/decision-records/0008-per-app-rewrite-style-memory.md)) |
| `humanizeTranslations` | bool | `true` | Make translations read like a person wrote them (the "humanizer" prompt layer); applies to both read and compose modes |
| `dictation` | bool | `true` | Show the microphone in the compose box so you can speak instead of typing. Audio is streamed to OpenAI **only** between an explicit start and stop ([ADR-0009](../architecture/decision-records/0009-speech-to-text-dictation.md)) |
| `speechModel` | string | `"gpt-realtime-whisper"` | Speech-to-text model; configurable for the same reason `model` is (names drift). See [openai-models.md](openai-models.md) |
| `autoCorrect` | bool | `true` | Fix typos, repair words dictation misheard, and write English terms in Latin script. Runs as a proof-read of the box after dictation, and rides inside the translation prompt otherwise (no extra call). Corrects spelling, never style ([ADR-0010](../architecture/decision-records/0010-auto-correct-pass.md)) |
| `blocklist` | string[] | `[]` | Regex **monikers** (matched case-insensitively against the foreground process name) where the badge is suppressed; empty ⇒ it appears everywhere |
| `appOffsets` | map | `{}` | Per-exe badge offset calibration `{ "exe": {"corner":1,"dx":64,"dy":-6} }` |
| `theme` | enum | `"system"` | `system` \| `light` \| `dark` |
| `uiLanguage` | string (BCP-47) | `"fa"` | Language of the app's own UI |
| `runAtStartup` | bool | `false` | Register an HKCU Run entry |
| `autoSend` | bool | `false` | If true, an explicit "translate & send" action presses the messenger Send (off by default) |

## Notes

- **Opt-out model:** the badge auto-appears in _any_ editable field; `blocklist` is the only filter.
  Each entry is a **case-insensitive regular expression** tested against the foreground process file
  name (invalid regex falls back to a substring test) — Grammarly's `Moniker` style. So `whatsapp`
  matches both `WhatsApp.exe` and the packaged `WhatsApp.Root.exe`, and `1password` matches
  `1Password.exe`. App identity is taken from the foreground top-level window, so WebView2 apps match
  their shell process, not `msedgewebview2.exe`
  ([ADR-0003](../architecture/decision-records/0003-focus-detection-winevent-uia-ia2.md) § Validation).
- `appOffsets.corner` follows the anchor enum used by the badge anchoring code; `dx`/`dy` are DIPs.
- Password and read-only fields are always skipped regardless of allowlist.
- Settings are read by `SettingsStore` and consumed by `LanguageDirector`, `FocusWatcher`,
  `HotkeyService`, and the windows; see [overview §8](../architecture/overview.md#8-cross-cutting-concerns).
