# Windows build checklist (M1)

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-29

The M1 code is complete but the `net10.0-windows` projects (Infrastructure, App) were authored on
Linux and **not compiled** there. This checklist is what to confirm on the **first Windows build**.
Each item is isolated behind an interface or a single file, so fixes are local.

## 0. Build

```powershell
dotnet restore src/AiTranslator.slnx
dotnet build   src/AiTranslator.slnx -c Debug
dotnet run --project src/AiTranslator.App
```

## 1. Package restore — offline ([offline-build.md](offline-build.md))

Restore is **offline**: the host can't reach `nuget.org`, so packages are served from the git-ignored
`.nuget-packages/` fallback folder via `RestoreFallbackFolders` in
[Directory.Build.props](../../src/Directory.Build.props) (which also `NoWarn`s `NU1507`, a
false-positive raised by a machine-global `github` source). Package versions are pinned to versions
verified on nuget.org. If restore reports a **missing** package, repopulate the folder from a
networked machine (see [offline-build.md](offline-build.md)).

Note: `Meziantou.Framework.Win32.CredentialManager` is held at `1.1.0` (classic API used by the
code); `2.x` exists but its API must be verified before bumping.

## 2. OpenAI SDK — [OpenAiTranslationService.cs](../../src/AiTranslator.Infrastructure/Translation/OpenAiTranslationService.cs)

API shape was verified via context7 but member names drift across minor versions. Confirm via
IntelliSense and adjust **only this file**:

- [ ] `ResponsesClient` ctor takes `apiKey:`.
- [ ] `CreateResponseOptions { Model, StreamingEnabled = true, Instructions }` — confirm `Instructions`
      is the system-prompt setter (else use a developer `ResponseItem`).
- [ ] `ResponseItem.CreateUserMessageItem(text)`.
- [ ] `client.CreateResponseStreamingAsync(options, ct)` returns an async stream of `StreamingResponseUpdate`.
- [ ] Text chunk type `StreamingResponseOutputTextDeltaUpdate` with `.Delta`.
- [ ] Default model `gpt-5.1` exists for your account (or pick a `mini`/`nano` variant in Settings).

## 3. Credential Manager — [CredentialManagerSecretStore.cs](../../src/AiTranslator.Infrastructure/Secrets/CredentialManagerSecretStore.cs)

- [ ] `CredentialManager.ReadCredential / WriteCredential / DeleteCredential`, `CredentialPersistence`,
      and `.Password` match the installed Meziantou package.

## 4. CsWin32 interop — [HotkeyService.cs](../../src/AiTranslator.Infrastructure/Input/HotkeyService.cs), [ClipboardTextInjector.cs](../../src/AiTranslator.Infrastructure/Input/ClipboardTextInjector.cs)

CsWin32 generates the P/Invokes from [NativeMethods.txt](../../src/AiTranslator.Infrastructure/NativeMethods.txt) at build. Confirm the generated shapes:

- [ ] `PInvoke.RegisterHotKey(HWND, int, HOT_KEY_MODIFIERS, uint)` and `HOT_KEY_MODIFIERS.MOD_*`.
- [ ] `INPUT` / `KEYBDINPUT` field names (`type`, `Anonymous.ki`, `wVk`, `dwFlags`), `VIRTUAL_KEY.VK_*`,
      `KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP`, and the `SendInput(ReadOnlySpan<INPUT>, int)` overload.
- [ ] Add any missing API to `NativeMethods.txt` and rebuild.

## 5. WPF — App.xaml, windows, tray

- [x] [App.xaml](../../src/AiTranslator.App/App.xaml): WPF-UI `xmlns:ui` + `ThemesDictionary` /
      `ControlsDictionary` — confirmed correct for WPF-UI 4.x (context7). An `MC3074
      "ThemesDictionary does not exist"` warning is a cascade of a failed restore (fix §1 first).
- [ ] [App.xaml.cs](../../src/AiTranslator.App/App.xaml.cs): `H.NotifyIcon` `TaskbarIcon`, `ForceCreate()`,
      `ShowNotification(...)`; tray `Icon = SystemIcons.Application` (placeholder — confirm
      `System.Drawing` resolves, or supply a branded `.ico`; M4).
- [ ] Overlay TextBox receives keyboard focus and a working Persian caret.

## 6. Acceptance test (manual)

1. Run the app → it lands in the tray; open Settings, paste an OpenAI key, set the language pair.
2. Open Notepad (simplest Win32 target), click in it, press the hotkey (`Ctrl+Alt+T`).
3. Type `سلام دنیا`, pause ~0.5 s → Notepad content is replaced with the English translation.
4. `Enter` in the overlay inserts a newline; `Esc` closes it; the clipboard is restored.
5. Repeat against WhatsApp/Telegram and note per-app quirks (badge auto-detection + per-app tuning is M2/M3).

## Known M1 limitations (by design — not bugs)

- Brief focus flicker on each live injection (overlay → target → overlay). Removed in M3 via a
  non-activating overlay + background injection.
- No badge / field auto-detection yet (hotkey only) — that is M2.
- Cannot inject into windows running as Administrator unless the app is elevated too (UIPI).

Related: [build-and-run.md](build-and-run.md) · [development-setup.md](development-setup.md) ·
[source-layout.md](../reference/source-layout.md)
