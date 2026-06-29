# Source layout

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-29

The `src/` solution. Update this page in the same change as any structure change.

> **Build status.** All layers **build green on Windows** (`dotnet build src/AiTranslator.slnx` → 0/0;
> `dotnet test` → 40/40). **Core + Tests** (`net10.0`) are also green on Linux/CI. **Infrastructure +
> App** (`net10.0-windows`) compile and run on Windows — M1 plus the **M2 awareness machinery**
> (FocusWatcher, TargetResolver, BadgeWindow). The remaining M1 gate is the manual runtime acceptance
> test; M2's per-app badge behaviour is validated app-by-app
> ([ADR-0001](../architecture/decision-records/0001-platform-dotnet10-wpf.md),
> [windows build checklist](../guides/windows-build-checklist.md)).

```
src/
  AiTranslator.slnx
  Directory.Build.props · Directory.Packages.props · nuget.config
  AiTranslator.Core/              # net10.0 — pure, testable, no Windows interop   [BUILT + GREEN]
    Models/                       #   AppSettings (incl. appOffsets), LanguagePair, TranslationDirection
    Abstractions/                 #   ITranslationService, ITextInjector, IFocusTargetProvider,
                                  #   IHotkeyService, ISecretStore, ISettingsStore,
                                  #   IFocusWatcher (M2), ITargetResolver (M2)
    Awareness/                    #   AppActivationPolicy, AppOffset(s), ExeName (pure logic, M2)
    Input/                        #   HotkeyCombination (parser)
    Translation/                  #   LanguageDirector, PromptBuilder (pure logic)
    Settings/                     #   JsonSettingsStore (%APPDATA%\AI-Translator\settings.json)
  AiTranslator.Infrastructure/    # net10.0-windows — adapters                     [BUILT + GREEN]
    NativeMethods.txt             #   CsWin32 P/Invoke list (hotkey, input, WinEvent, process)
    Translation/OpenAiTranslationService.cs  #   OpenAI Responses streaming
    Secrets/CredentialManagerSecretStore.cs
    Input/HotkeyService.cs        #   RegisterHotKey / WM_HOTKEY
    Input/ForegroundFocusTargetProvider.cs   #   capture target HWND (M1)
    Input/ClipboardTextInjector.cs           #   save/set clipboard, focus, Ctrl+A/V, restore
    Awareness/FocusWatcher.cs     #   SetWinEventHook focus/foreground watch (M2)
    Awareness/TargetResolver.cs   #   UIA field classify + locate (M2)
  AiTranslator.App/               # net10.0-windows — WPF + composition            [BUILT + GREEN]
    App.xaml(.cs)                 #   tray startup, DI, message window, global hotkey, badge, first-run
    Composition/ServiceConfiguration.cs      #   DI container
    Shell/StartupManager.cs       #   HKCU Run (run-at-startup)
    Resources/UiStrings.cs        #   localization seam (English source)
    Windows/OverlayInputWindow.*  #   floating input; debounced translate + inject
    Windows/SettingsWindow.*      #   key, langs, model, hotkey (live-validated), badge, allow/blocklist
    Windows/BadgeWindow.* · NonActivatingWindow.cs   #   WS_EX_NOACTIVATE badge (M2)
    app.manifest                  #   PerMonitorV2 DPI awareness
  AiTranslator.Tests/             # net10.0 — xUnit over Core           [BUILT + GREEN, 40/40]
```

> Current deviations from the full design: the **overlay** is still a normal activating window (so
> typing works) that captures/targets the field and re-focuses itself after injection. The
> non-activating **badge** (`WS_EX_NOACTIVATE`) and field auto-detection **landed in M2**
> (build-verified; per-app behaviour validated manually). A non-activating *overlay* and live-inject
> without focus flicker remain M3 (see [overview §9](../architecture/overview.md#9-roadmap)).

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
| `System.Windows.Automation` (managed UIA — in WindowsDesktop framework, **no NuGet**) | Infrastructure | M2 | [0003](../architecture/decision-records/0003-focus-detection-winevent-uia-ia2.md) |
| IAccessible2 interop (Chromium renderer wake) | Infrastructure | deferred — only if manual per-app testing shows UIA is insufficient | [0003](../architecture/decision-records/0003-focus-detection-winevent-uia-ia2.md) |
| `Velopack` | App (packaging) | M4 | [0006](../architecture/decision-records/0006-distribution-velopack.md) |

Adding anything outside this list needs an ADR (per [CLAUDE.md](../../CLAUDE.md) / package-first rule).
