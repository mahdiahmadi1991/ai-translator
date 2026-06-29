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

## 1. Package versions ([Directory.Packages.props](../../src/Directory.Packages.props))

The Windows-only package versions are best-effort estimates. Confirm each resolves; a missing one
is `NU1102`. Bump to the latest stable:

```powershell
dotnet list src/AiTranslator.slnx package --outdated
```

Packages to verify: `OpenAI`, `Meziantou.Framework.Win32.CredentialManager`,
`Microsoft.Windows.CsWin32`, `WPF-UI`, `H.NotifyIcon.Wpf`, `Microsoft.Extensions.DependencyInjection`.

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

- [ ] [App.xaml](../../src/AiTranslator.App/App.xaml): WPF-UI `xmlns:ui` + `ThemesDictionary` /
      `ControlsDictionary` match WPF-UI 4.x.
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
