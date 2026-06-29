# Source layout

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-29

The `src/` solution. Update this page in the same change as any structure change.

> **Build status (M1).** All three layers' code is written. **Core + Tests** target `net10.0` and are
> **built and green on Linux/CI** (`dotnet test` → 10/10). **Infrastructure + App** target
> `net10.0-windows` and are written but **not yet built** — WPF/Win32 compile only on Windows
> ([ADR-0001](../architecture/decision-records/0001-platform-dotnet10-wpf.md)). See the
> [Windows build checklist](../guides/windows-build-checklist.md) for what to confirm on the first
> Windows build.

```
src/
  AiTranslator.slnx
  Directory.Build.props · Directory.Packages.props · nuget.config
  AiTranslator.Core/              # net10.0 — pure, testable, no Windows interop   [BUILT + GREEN]
    Models/                       #   AppSettings, LanguagePair, TranslationDirection
    Abstractions/                 #   ITranslationService, ITextInjector, IFocusTargetProvider,
                                  #   IHotkeyService, ISecretStore, ISettingsStore
    Translation/                  #   LanguageDirector, PromptBuilder (pure logic)
    Settings/                     #   JsonSettingsStore (%APPDATA%\AI-Translator\settings.json)
  AiTranslator.Infrastructure/    # net10.0-windows — adapters       [WRITTEN, pending Windows build]
    NativeMethods.txt             #   CsWin32 P/Invoke list
    Translation/OpenAiTranslationService.cs  #   OpenAI Responses streaming
    Secrets/CredentialManagerSecretStore.cs
    Input/HotkeyService.cs        #   RegisterHotKey / WM_HOTKEY
    Input/ForegroundFocusTargetProvider.cs   #   capture target HWND (M1)
    Input/ClipboardTextInjector.cs           #   save/set clipboard, focus, Ctrl+A/V, restore
    (M2/M3) Automation/ · FocusWatcher.cs · TargetResolver.cs  #   badge auto-detection
  AiTranslator.App/               # net10.0-windows — WPF + composition   [WRITTEN, pending Windows build]
    App.xaml(.cs)                 #   tray startup, DI, message window, global hotkey, first-run
    Composition/ServiceConfiguration.cs      #   DI container
    Shell/StartupManager.cs       #   HKCU Run (run-at-startup)
    Resources/UiStrings.cs        #   localization seam (English source)
    Windows/OverlayInputWindow.*  #   floating input; debounced translate + inject
    Windows/SettingsWindow.*      #   key, language pair, model, hotkey
    app.manifest                  #   PerMonitorV2 DPI awareness
    (M2) Windows/BadgeWindow.* · NonActivatingWindow.cs  #   WS_EX_NOACTIVATE badge
  AiTranslator.Tests/             # net10.0 — xUnit over Core           [BUILT + GREEN]
```

> M1 deviations from the full design: the overlay is a normal activating window (so typing works) that
> captures the foreground target before showing and re-focuses itself after injection. The
> non-activating badge (`WS_EX_NOACTIVATE`) and field auto-detection are M2; live-inject without focus
> flicker is M3 (see [overview §9](../architecture/overview.md#9-roadmap)).

## Dependency direction

`App` → `Infrastructure` → `Core`. `Core` depends on nothing project-local. Windows interop lives
only in `Infrastructure`, behind the `Core/Abstractions` interfaces, so the core logic and the
translation/prompt code are unit-testable without a live desktop.

## Approved dependencies (from the ADRs)

| Package | Used in | Milestone | ADR / source |
| --- | --- | --- | --- |
| `OpenAI` | Infrastructure | M1 | [0002](../architecture/decision-records/0002-translation-openai-responses-streaming.md) |
| `Meziantou.Framework.Win32.CredentialManager` | Infrastructure | M1 | [0005](../architecture/decision-records/0005-secret-storage-credential-manager.md) |
| `Microsoft.Windows.CsWin32` | Infrastructure | M1 | [0003](../architecture/decision-records/0003-focus-detection-winevent-uia-ia2.md) |
| `WPF-UI` (≥ 4.3) | App | M1 | overview §4 |
| `H.NotifyIcon.Wpf` | App | M1 | overview §4 |
| `Microsoft.Extensions.DependencyInjection` | App | M1 | overview §4 |
| UI Automation COM interop + IAccessible2 interop | Infrastructure | M2/M3 | [0003](../architecture/decision-records/0003-focus-detection-winevent-uia-ia2.md) |
| `Velopack` | App (packaging) | M4 | [0006](../architecture/decision-records/0006-distribution-velopack.md) |

Adding anything outside this list needs an ADR (per [CLAUDE.md](../../CLAUDE.md) / package-first rule).
