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
| `autoAppearBadge` | bool | `true` | Show the badge automatically in allowlisted apps |
| `allowlist` | string[] | `["whatsapp","telegram"]` | Regex **monikers** matched (case-insensitive) against the foreground process name |
| `blocklist` | string[] | `[]` | Regex monikers to always suppress (takes precedence; e.g. password tools) |
| `appOffsets` | map | `{}` | Per-exe badge offset calibration `{ "exe": {"corner":1,"dx":64,"dy":-6} }` |
| `theme` | enum | `"system"` | `system` \| `light` \| `dark` |
| `uiLanguage` | string (BCP-47) | `"fa"` | Language of the app's own UI |
| `runAtStartup` | bool | `false` | Register an HKCU Run entry |
| `autoSend` | bool | `false` | If true, an explicit "translate & send" action presses the messenger Send (off by default) |

## Notes

- `allowlist`/`blocklist` mirror Grammarly's `Moniker` model: each entry is a **case-insensitive
  regular expression** tested against the foreground process file name (invalid regex falls back to a
  substring test). So `whatsapp` matches both `WhatsApp.exe` and the packaged `WhatsApp.Root.exe`, and
  `telegram` matches `Telegram.exe`. App identity is taken from the foreground top-level window, so
  WebView2 apps (WhatsApp) match their shell process, not `msedgewebview2.exe`
  ([ADR-0003](../architecture/decision-records/0003-focus-detection-winevent-uia-ia2.md) § Validation).
- `appOffsets.corner` follows the anchor enum used by the badge anchoring code; `dx`/`dy` are DIPs.
- Password and read-only fields are always skipped regardless of allowlist.
- Settings are read by `SettingsStore` and consumed by `LanguageDirector`, `FocusWatcher`,
  `HotkeyService`, and the windows; see [overview §8](../architecture/overview.md#8-cross-cutting-concerns).
