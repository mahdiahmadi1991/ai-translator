# Source layout (planned)

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-28

The intended `src/` solution. **Not created yet** — the solution is scaffolded during implementation
(milestone M1, see [overview §9](../architecture/overview.md#9-roadmap)), on Windows, where WPF can
build. Update this page in the same change as any structure change.

```
src/
  AiTranslator.sln
  AiTranslator.Core/              # net10.0 — pure, testable, no Windows interop
    Models/                       #   settings model, language pair, target descriptors
    Abstractions/                 #   IFocusWatcher, ITargetResolver, ITextInjector,
                                  #   ITranslationService, ISecretStore, ISettingsStore, IHotkeyService
    Translation/                  #   LanguageDirector, PromptBuilder (pure logic)
  AiTranslator.Infrastructure/    # net10.0-windows — concrete adapters
    Win32/                        #   SetWinEventHook, SendInput, clipboard, window styles (CsWin32)
    Automation/                   #   UI Automation (IUIAutomation) + IAccessible2 (Chromium enable)
    FocusWatcher.cs               #   focus/foreground/location hook
    TargetResolver.cs             #   classify + anchor + cache target
    TextInjector.cs               #   clipboard-paste + ValuePattern + SendInput
    HotkeyService.cs              #   RegisterHotKey / WM_HOTKEY
    OpenAiTranslationService.cs   #   OpenAI Responses streaming
    CredentialManagerSecretStore.cs
    JsonSettingsStore.cs          #   %APPDATA%\AI-Translator\settings.json
  AiTranslator.App/               # net10.0-windows — WPF UI + composition root
    App.xaml(.cs)                 #   startup, DI wiring, single-instance
    Shell/                        #   tray icon, run-at-startup
    Windows/                      #   BadgeWindow, OverlayInputWindow, SettingsWindow
    Resources/                    #   localized strings (en source), themes, fonts (Vazirmatn)
    app.manifest                  #   PerMonitorV2 DPI awareness
  AiTranslator.Tests/             # net10.0 — xUnit; covers Core logic with mocked abstractions
```

## Dependency direction

`App` → `Infrastructure` → `Core`. `Core` depends on nothing project-local. Windows interop lives
only in `Infrastructure`, behind the `Core/Abstractions` interfaces, so the core logic and the
translation/prompt code are unit-testable without a live desktop.

## Approved dependencies (from the ADRs)

| Package | Used in | ADR |
| --- | --- | --- |
| `OpenAI` | Infrastructure | [0002](../architecture/decision-records/0002-translation-openai-responses-streaming.md) |
| `Microsoft.Windows.CsWin32` | Infrastructure | [0003](../architecture/decision-records/0003-focus-detection-winevent-uia-ia2.md) |
| UI Automation COM interop + IAccessible2 interop | Infrastructure | [0003](../architecture/decision-records/0003-focus-detection-winevent-uia-ia2.md) |
| `Meziantou.Framework.Win32.CredentialManager` | Infrastructure | [0005](../architecture/decision-records/0005-secret-storage-credential-manager.md) |
| `WPF-UI` (≥ 4.3) | App | overview §4 |
| `H.NotifyIcon.Wpf` | App | overview §4 |
| `System.Reactive` | Infrastructure/App (event debouncing) | overview §6 |
| `Velopack` | App (packaging) | [0006](../architecture/decision-records/0006-distribution-velopack.md) |

Adding anything outside this list needs an ADR (per [CLAUDE.md](../../CLAUDE.md) / package-first rule).
