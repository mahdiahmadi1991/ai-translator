# M1 — Walking Skeleton Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove the end-to-end path — open an overlay with a global hotkey, type source text, stream a translation from OpenAI, and inject it into the currently-focused field — plus a tray app, settings window, and secure key storage. No badge / auto field-detection yet (that is M2/M3).

**Architecture:** A background WPF process (tray) hosts a non-activating overlay input box opened by a global hotkey. Typing is debounced (500 ms); `TranslationService` streams an OpenAI translation; `TextInjector` replaces the captured target field's content via clipboard-paste. Platform-agnostic logic (settings, language direction, prompt building) lives in `AiTranslator.Core` and is fully unit-tested; Windows interop and the OpenAI client live in `AiTranslator.Infrastructure` behind `Core` interfaces; `AiTranslator.App` is the WPF composition root.

**Tech Stack:** .NET 10 (`net10.0` for Core/Tests, `net10.0-windows` for Infrastructure/App), WPF, WPF-UI, H.NotifyIcon.Wpf, OpenAI .NET SDK (Responses API streaming), Meziantou.Framework.Win32.CredentialManager, Microsoft.Windows.CsWin32, xUnit.

## Global Constraints

- **Platform:** Windows-only build/run. `Core` + `Tests` target `net10.0` and build/test anywhere; `Infrastructure` + `App` target `net10.0-windows` and build/run **only on Windows**. Never claim a `net10.0-windows` build/run passed from Linux/WSL — it cannot. (ADR-0001)
- **Translation API:** OpenAI **Responses API streaming** only; never the Realtime/audio API. Isolate all OpenAI types inside `OpenAiTranslationService`. (ADR-0002)
- **Secrets:** the OpenAI key is read from Windows Credential Manager at runtime, or from `.secrets/providers/openai.secrets.toml` only in DEBUG. Never hard-code, log, or commit a key. (ADR-0005, secrets standard)
- **Injection:** clipboard-paste primary; never auto-press Send. `Enter` in the overlay inserts a newline. (ADR-0004, spec §2)
- **Naming/style:** files/dirs `lowercase-kebab-case`; C# per `.editorconfig` (file-scoped namespaces, nullable enabled, `_camelCase` private fields, one public type per file). User-facing strings via resources (English source). (naming-conventions)
- **Git:** work on this branch `feature/m1-walking-skeleton` (off `develop`). Commit per task. Never push/merge without explicit permission. Commit trailer: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- **Model default:** `gpt-5.1` (verified present in the SDK examples); cheaper `mini`/`nano` variants are user-selectable. Verify available models against `/v1/models` at runtime. (reference/openai-models.md)

---

## File Structure

```
src/
  AiTranslator.slnx                     # .NET 10 default solution format
  Directory.Build.props                 # shared: nullable, langversion, file-scoped ns analyzers
  Directory.Packages.props              # central package versions
  AiTranslator.Core/                    # net10.0 — pure, fully unit-tested
    AiTranslator.Core.csproj
    Models/AppSettings.cs               # settings record + defaults
    Models/LanguagePair.cs             # primary/secondary BCP-47 pair
    Models/TranslationDirection.cs     # enum + resolved source/target
    Abstractions/ISettingsStore.cs
    Abstractions/ISecretStore.cs
    Abstractions/ITranslationService.cs
    Abstractions/IHotkeyService.cs
    Abstractions/ITextInjector.cs
    Abstractions/IFocusTargetProvider.cs
    Translation/LanguageDirector.cs    # decide direction from input + pair
    Translation/PromptBuilder.cs       # build translation-only system prompt
    Settings/JsonSettingsStore.cs      # %APPDATA%\AI-Translator\settings.json (cross-platform I/O)
  AiTranslator.Infrastructure/          # net10.0-windows — adapters
    AiTranslator.Infrastructure.csproj
    NativeMethods.txt                   # CsWin32 P/Invoke list
    Translation/OpenAiTranslationService.cs
    Secrets/CredentialManagerSecretStore.cs
    Input/HotkeyService.cs              # RegisterHotKey / WM_HOTKEY
    Input/ForegroundFocusTargetProvider.cs  # capture target HWND
    Input/ClipboardTextInjector.cs     # save/set clipboard, focus, Ctrl+A, Ctrl+V, restore
  AiTranslator.App/                     # net10.0-windows — WPF composition root
    AiTranslator.App.csproj
    app.manifest                        # PerMonitorV2 DPI awareness
    App.xaml / App.xaml.cs              # startup, DI, single-instance
    Composition/ServiceConfiguration.cs # DI wiring
    Shell/TrayIcon.xaml(.cs)           # tray menu, run-at-startup
    Windows/OverlayInputWindow.xaml(.cs)
    Windows/SettingsWindow.xaml(.cs)
    Windows/NonActivatingWindow.cs      # WS_EX_NOACTIVATE base
    Resources/Strings.en.resx
  AiTranslator.Tests/                   # net10.0 — xUnit (Core only)
    AiTranslator.Tests.csproj
    LanguageDirectorTests.cs
    PromptBuilderTests.cs
    JsonSettingsStoreTests.cs
    AppSettingsTests.cs
```

**Testability split:** `Core` + `Tests` are cross-platform and TDD'd with real `dotnet test`. `Infrastructure` + `App` wrap Win32/WPF/network and are verified by **manual run on Windows** (the honest analog — Win32 focus/clipboard/GUI behavior is per-app and not unit-testable). Each such task ends with an explicit Windows verification.

---

### Task 1: Solution & project scaffold

**Files:**
- Create: `src/AiTranslator.slnx`, `src/Directory.Build.props`, `src/Directory.Packages.props`
- Create: the four `.csproj` files listed above

**Interfaces:**
- Produces: the solution + project graph (`App` → `Infrastructure` → `Core`; `Tests` → `Core`).

- [ ] **Step 1: Create solution and projects**

```bash
cd src
dotnet new sln -n AiTranslator
dotnet new classlib -n AiTranslator.Core         -f net10.0
dotnet new classlib -n AiTranslator.Infrastructure -f net10.0-windows
dotnet new wpf      -n AiTranslator.App           -f net10.0-windows
dotnet new xunit    -n AiTranslator.Tests         -f net10.0
dotnet sln add AiTranslator.Core AiTranslator.Infrastructure AiTranslator.App AiTranslator.Tests
dotnet add AiTranslator.Infrastructure reference AiTranslator.Core
dotnet add AiTranslator.App reference AiTranslator.Infrastructure AiTranslator.Core
dotnet add AiTranslator.Tests reference AiTranslator.Core
rm AiTranslator.Core/Class1.cs AiTranslator.Infrastructure/Class1.cs
```

- [ ] **Step 2: Add `Directory.Build.props`** (shared compiler settings)

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Add `Directory.Packages.props`** (central versions; pin during restore)

```xml
<Project>
  <ItemGroup>
    <PackageVersion Include="OpenAI" Version="2.*" />
    <PackageVersion Include="Meziantou.Framework.Win32.CredentialManager" Version="1.*" />
    <PackageVersion Include="Microsoft.Windows.CsWin32" Version="0.3.*" />
    <PackageVersion Include="WPF-UI" Version="4.*" />
    <PackageVersion Include="H.NotifyIcon.Wpf" Version="2.*" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="9.*" />
    <PackageVersion Include="Tomlyn" Version="0.*" />
    <PackageVersion Include="xunit" Version="2.*" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.*" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.*" />
  </ItemGroup>
</Project>
```

> Verification note: pin each `*` to the exact latest stable on first `dotnet restore` (run `dotnet list package --outdated` and replace). Central management means versions live only here.

- [ ] **Step 4: Build Core + Tests (cross-platform, runnable here)**

Run: `dotnet build src/AiTranslator.Core src/AiTranslator.Tests`
Expected: PASS (no code yet beyond templates).

- [ ] **Step 5: Commit**

```bash
git add src/
git commit -m "chore: scaffold .NET 10 solution (Core/Infrastructure/App/Tests)"
```

---

### Task 2: AppSettings model + defaults

**Files:**
- Create: `src/AiTranslator.Core/Models/LanguagePair.cs`, `Models/AppSettings.cs`
- Test: `src/AiTranslator.Tests/AppSettingsTests.cs`

**Interfaces:**
- Produces: `record LanguagePair(string Primary, string Secondary)`; `record AppSettings { ... }` with a static `AppSettings Default`. Field names match [reference/configuration.md](../reference/configuration.md).

- [ ] **Step 1: Write the failing test**

```csharp
using AiTranslator.Core.Models;
using Xunit;

public class AppSettingsTests
{
    [Fact]
    public void Default_has_fa_en_pair_and_safe_defaults()
    {
        var s = AppSettings.Default;
        Assert.Equal(1, s.SchemaVersion);
        Assert.Equal("fa", s.LanguagePair.Primary);
        Assert.Equal("en", s.LanguagePair.Secondary);
        Assert.True(s.AutoDirection);
        Assert.Equal(500, s.DebounceMs);
        Assert.Equal("Ctrl+Alt+T", s.Hotkey);
        Assert.Equal("gpt-5.1", s.Model);
        Assert.False(s.AutoSend);
        Assert.Contains("WhatsApp.exe", s.Allowlist);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/AiTranslator.Tests --filter AppSettingsTests`
Expected: FAIL (compile error — `AppSettings` not defined).

- [ ] **Step 3: Implement the models**

```csharp
// LanguagePair.cs
namespace AiTranslator.Core.Models;

public sealed record LanguagePair(string Primary, string Secondary);
```

```csharp
// AppSettings.cs
namespace AiTranslator.Core.Models;

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public LanguagePair LanguagePair { get; init; } = new("fa", "en");
    public bool AutoDirection { get; init; } = true;
    public string Model { get; init; } = "gpt-5.1";
    public int DebounceMs { get; init; } = 500;
    public string Hotkey { get; init; } = "Ctrl+Alt+T";
    public bool AutoAppearBadge { get; init; } = true;
    public IReadOnlyList<string> Allowlist { get; init; } = ["WhatsApp.exe", "Telegram.exe"];
    public IReadOnlyList<string> Blocklist { get; init; } = [];
    public string Theme { get; init; } = "system";
    public string UiLanguage { get; init; } = "fa";
    public bool RunAtStartup { get; init; } = false;
    public bool AutoSend { get; init; } = false;

    public static AppSettings Default { get; } = new();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/AiTranslator.Tests --filter AppSettingsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AiTranslator.Core/Models src/AiTranslator.Tests/AppSettingsTests.cs
git commit -m "feat(core): add AppSettings model with defaults"
```

---

### Task 3: LanguageDirector (direction resolution)

**Files:**
- Create: `src/AiTranslator.Core/Models/TranslationDirection.cs`, `Translation/LanguageDirector.cs`
- Test: `src/AiTranslator.Tests/LanguageDirectorTests.cs`

**Interfaces:**
- Produces: `readonly record struct TranslationDirection(string SourceLang, string TargetLang)`;
  `LanguageDirector.Resolve(string text, LanguagePair pair, bool autoDirection) -> TranslationDirection`.
  Rule: if `autoDirection`, detect whether `text` is mostly Arabic-script (Persian) → source=Primary,
  target=Secondary when text matches Primary's script, else source=Secondary,target=Primary. With
  `autoDirection=false`, always Primary→Secondary.

- [ ] **Step 1: Write the failing test**

```csharp
using AiTranslator.Core.Models;
using AiTranslator.Core.Translation;
using Xunit;

public class LanguageDirectorTests
{
    private static readonly LanguagePair FaEn = new("fa", "en");

    [Fact]
    public void Persian_input_translates_to_english()
    {
        var d = LanguageDirector.Resolve("سلام خوبی؟", FaEn, autoDirection: true);
        Assert.Equal("fa", d.SourceLang);
        Assert.Equal("en", d.TargetLang);
    }

    [Fact]
    public void English_input_translates_to_persian()
    {
        var d = LanguageDirector.Resolve("Hello there", FaEn, autoDirection: true);
        Assert.Equal("en", d.SourceLang);
        Assert.Equal("fa", d.TargetLang);
    }

    [Fact]
    public void Auto_direction_off_always_primary_to_secondary()
    {
        var d = LanguageDirector.Resolve("Hello", FaEn, autoDirection: false);
        Assert.Equal("fa", d.SourceLang);
        Assert.Equal("en", d.TargetLang);
    }

    [Fact]
    public void Mixed_text_uses_dominant_script()
    {
        var d = LanguageDirector.Resolve("سلام world این متن فارسی است", FaEn, autoDirection: true);
        Assert.Equal("fa", d.SourceLang);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/AiTranslator.Tests --filter LanguageDirectorTests`
Expected: FAIL (compile error — `LanguageDirector` not defined).

- [ ] **Step 3: Implement**

```csharp
// TranslationDirection.cs
namespace AiTranslator.Core.Models;

public readonly record struct TranslationDirection(string SourceLang, string TargetLang);
```

```csharp
// LanguageDirector.cs
using System.Globalization;
using AiTranslator.Core.Models;

namespace AiTranslator.Core.Translation;

/// <summary>Decides which way to translate based on the input's dominant script.</summary>
public static class LanguageDirector
{
    // Languages whose script is Arabic/Persian. Extend as the supported set grows.
    private static readonly HashSet<string> ArabicScriptLangs = new(StringComparer.OrdinalIgnoreCase)
    {
        "fa", "ar", "ur", "ps",
    };

    public static TranslationDirection Resolve(string text, LanguagePair pair, bool autoDirection)
    {
        if (!autoDirection || string.IsNullOrWhiteSpace(text))
        {
            return new TranslationDirection(pair.Primary, pair.Secondary);
        }

        bool inputIsArabicScript = IsMostlyArabicScript(text);
        bool primaryIsArabicScript = ArabicScriptLangs.Contains(pair.Primary);

        // If the input's script matches the primary language's script, source = primary.
        bool sourceIsPrimary = inputIsArabicScript == primaryIsArabicScript;
        return sourceIsPrimary
            ? new TranslationDirection(pair.Primary, pair.Secondary)
            : new TranslationDirection(pair.Secondary, pair.Primary);
    }

    private static bool IsMostlyArabicScript(string text)
    {
        int arabic = 0, latin = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            // Arabic block U+0600–U+06FF, plus Persian-specific presentation ranges.
            if (rune.Value is >= 0x0600 and <= 0x06FF or >= 0x0750 and <= 0x077F or >= 0xFB50 and <= 0xFDFF or >= 0xFE70 and <= 0xFEFF)
            {
                arabic++;
            }
            else if (char.IsLetter((char)Math.Min(rune.Value, 0xFFFF)) && rune.Value < 0x0250)
            {
                latin++;
            }
        }

        return arabic >= latin;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/AiTranslator.Tests --filter LanguageDirectorTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/AiTranslator.Core/Models/TranslationDirection.cs src/AiTranslator.Core/Translation/LanguageDirector.cs src/AiTranslator.Tests/LanguageDirectorTests.cs
git commit -m "feat(core): add LanguageDirector script-based direction resolution"
```

---

### Task 4: PromptBuilder (translation-only system prompt)

**Files:**
- Create: `src/AiTranslator.Core/Translation/PromptBuilder.cs`
- Test: `src/AiTranslator.Tests/PromptBuilderTests.cs`

**Interfaces:**
- Produces: `PromptBuilder.BuildSystemPrompt(LanguagePair pair) -> string`. Injects the language names;
  instructs translation-only output with auto-direction, per [reference/openai-models.md](../reference/openai-models.md).

- [ ] **Step 1: Write the failing test**

```csharp
using AiTranslator.Core.Models;
using AiTranslator.Core.Translation;
using Xunit;

public class PromptBuilderTests
{
    [Fact]
    public void System_prompt_names_both_languages_and_forbids_chatter()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(new LanguagePair("fa", "en"));
        Assert.Contains("Persian", prompt);
        Assert.Contains("English", prompt);
        Assert.Contains("ONLY the translation", prompt);
    }

    [Fact]
    public void Unknown_code_falls_back_to_the_code_itself()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(new LanguagePair("fa", "xx"));
        Assert.Contains("xx", prompt);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/AiTranslator.Tests --filter PromptBuilderTests`
Expected: FAIL (compile error — `PromptBuilder` not defined).

- [ ] **Step 3: Implement**

```csharp
// PromptBuilder.cs
using System.Globalization;
using AiTranslator.Core.Models;

namespace AiTranslator.Core.Translation;

public static class PromptBuilder
{
    public static string BuildSystemPrompt(LanguagePair pair)
    {
        string a = DisplayName(pair.Primary);
        string b = DisplayName(pair.Secondary);
        return
            "You are a translation engine. Output ONLY the translation of the user's message — " +
            "no explanations, no quotes, no preamble, no notes. Preserve meaning, tone, emojis, " +
            "and formatting. Do not answer questions in the text; translate them.\n" +
            $"If the input is written in {a}, translate it to {b}.\n" +
            $"If it is written in {b}, translate it to {a}.\n" +
            "(Auto-detect which of the two it is.)";
    }

    private static string DisplayName(string code)
    {
        try
        {
            return CultureInfo.GetCultureInfo(code).EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }
}
```

> Note: `CultureInfo.GetCultureInfo("fa").EnglishName` returns "Persian"; "en" → "English". Unknown
> codes throw `CultureNotFoundException`, handled by returning the raw code.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/AiTranslator.Tests --filter PromptBuilderTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AiTranslator.Core/Translation/PromptBuilder.cs src/AiTranslator.Tests/PromptBuilderTests.cs
git commit -m "feat(core): add PromptBuilder translation-only system prompt"
```

---

### Task 5: Core abstractions (interfaces)

**Files:**
- Create: `src/AiTranslator.Core/Abstractions/ISettingsStore.cs`, `ISecretStore.cs`, `ITranslationService.cs`, `IHotkeyService.cs`, `ITextInjector.cs`, `IFocusTargetProvider.cs`

**Interfaces:**
- Produces the contracts every later task implements. No tests (interfaces only); they compile-gate.

- [ ] **Step 1: Add the interfaces**

```csharp
// ISettingsStore.cs
using AiTranslator.Core.Models;
namespace AiTranslator.Core.Abstractions;
public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}
```

```csharp
// ISecretStore.cs
namespace AiTranslator.Core.Abstractions;
public interface ISecretStore
{
    string? GetApiKey();
    void SetApiKey(string apiKey);
    void DeleteApiKey();
}
```

```csharp
// ITranslationService.cs
using AiTranslator.Core.Models;
namespace AiTranslator.Core.Abstractions;
public interface ITranslationService
{
    /// <summary>Streams the translation as incremental text chunks. Honors cancellation.</summary>
    IAsyncEnumerable<string> TranslateStreamAsync(
        string text, TranslationDirection direction, string model, CancellationToken ct);
}
```

```csharp
// IHotkeyService.cs
namespace AiTranslator.Core.Abstractions;
public interface IHotkeyService : IDisposable
{
    /// <summary>Registers the hotkey (e.g. "Ctrl+Alt+T"). Returns false if the combo is taken.</summary>
    bool Register(string hotkey);
    void Unregister();
    event EventHandler? HotkeyPressed;
}
```

```csharp
// IFocusTargetProvider.cs
namespace AiTranslator.Core.Abstractions;
/// <summary>An opaque handle to the field that was focused before our overlay opened.</summary>
public readonly record struct FocusTarget(nint WindowHandle);
public interface IFocusTargetProvider
{
    /// <summary>Capture the currently-focused foreign window as the injection target.</summary>
    FocusTarget CaptureCurrent();
}
```

```csharp
// ITextInjector.cs
namespace AiTranslator.Core.Abstractions;
public interface ITextInjector
{
    /// <summary>Replace the entire content of the target field with <paramref name="text"/>.</summary>
    Task ReplaceTextAsync(FocusTarget target, string text, CancellationToken ct);
}
```

- [ ] **Step 2: Build Core**

Run: `dotnet build src/AiTranslator.Core`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/AiTranslator.Core/Abstractions
git commit -m "feat(core): add service abstractions"
```

---

### Task 6: JsonSettingsStore (cross-platform, TDD)

**Files:**
- Create: `src/AiTranslator.Core/Settings/JsonSettingsStore.cs`
- Test: `src/AiTranslator.Tests/JsonSettingsStoreTests.cs`

**Interfaces:**
- Consumes: `ISettingsStore`, `AppSettings`.
- Produces: `JsonSettingsStore(string filePath)`; `Load()` returns `AppSettings.Default` when the file
  is missing/corrupt; `Save()` writes indented JSON and creates the directory.

- [ ] **Step 1: Write the failing test**

```csharp
using AiTranslator.Core.Models;
using AiTranslator.Core.Settings;
using Xunit;

public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"ait-{Guid.NewGuid():N}", "settings.json");

    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        var store = new JsonSettingsStore(_path);
        Assert.Equal(AppSettings.Default, store.Load());
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var store = new JsonSettingsStore(_path);
        var custom = AppSettings.Default with { Model = "gpt-5.1-mini", DebounceMs = 750 };
        store.Save(custom);

        var reloaded = new JsonSettingsStore(_path).Load();
        Assert.Equal("gpt-5.1-mini", reloaded.Model);
        Assert.Equal(750, reloaded.DebounceMs);
    }

    [Fact]
    public void Load_returns_defaults_on_corrupt_json()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ not valid json");
        Assert.Equal(AppSettings.Default, new JsonSettingsStore(_path).Load());
    }

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/AiTranslator.Tests --filter JsonSettingsStoreTests`
Expected: FAIL (compile error — `JsonSettingsStore` not defined).

- [ ] **Step 3: Implement**

```csharp
// JsonSettingsStore.cs
using System.Text.Json;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Models;

namespace AiTranslator.Core.Settings;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _filePath;

    public JsonSettingsStore(string filePath) => _filePath = filePath;

    /// <summary>The canonical per-user settings path: %APPDATA%\AI-Translator\settings.json.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AI-Translator", "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return AppSettings.Default;
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? AppSettings.Default;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return AppSettings.Default;
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, Options));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/AiTranslator.Tests --filter JsonSettingsStoreTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Run the full Core suite**

Run: `dotnet test src/AiTranslator.Tests`
Expected: PASS (all tasks 2–6).

- [ ] **Step 6: Commit**

```bash
git add src/AiTranslator.Core/Settings/JsonSettingsStore.cs src/AiTranslator.Tests/JsonSettingsStoreTests.cs
git commit -m "feat(core): add JSON settings store with default-on-error"
```

---

### Task 7: OpenAiTranslationService (Infrastructure)

**Files:**
- Create: `src/AiTranslator.Infrastructure/Translation/OpenAiTranslationService.cs`

**Interfaces:**
- Consumes: `ITranslationService`, `TranslationDirection`, `PromptBuilder`.
- Produces: `OpenAiTranslationService(Func<string?> apiKeyProvider)` implementing streaming translation.

> Verified via context7 (`/openai/openai-dotnet`): streaming uses a responses client +
> `CreateResponseOptions { StreamingEnabled = true }` + `CreateResponseStreamingAsync(options)`
> yielding `StreamingResponseUpdate`; text chunks are `StreamingResponseOutputTextDeltaUpdate.Delta`.
> **The exact client class name and the instructions-setter differ across SDK minor versions** — this
> is why all OpenAI types are confined to this one file behind `ITranslationService`. Confirm the
> exact names against the installed package via IntelliSense and adjust this file only.

- [ ] **Step 1: Add the OpenAI package**

```bash
dotnet add src/AiTranslator.Infrastructure package OpenAI
```

- [ ] **Step 2: Implement (confirm member names against installed SDK)**

```csharp
// OpenAiTranslationService.cs
using System.Runtime.CompilerServices;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Models;
using AiTranslator.Core.Translation;
using OpenAI.Responses;

namespace AiTranslator.Infrastructure.Translation;

/// <summary>
/// Streams a translation via the OpenAI Responses API. All OpenAI-specific types are isolated here
/// so SDK version drift touches only this file (see ADR-0002).
/// </summary>
public sealed class OpenAiTranslationService : ITranslationService
{
    private readonly Func<string?> _apiKeyProvider;

    public OpenAiTranslationService(Func<string?> apiKeyProvider) => _apiKeyProvider = apiKeyProvider;

    public async IAsyncEnumerable<string> TranslateStreamAsync(
        string text, TranslationDirection direction, string model,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var apiKey = _apiKeyProvider()
            ?? throw new InvalidOperationException("No OpenAI API key configured.");

        var pair = new LanguagePair(direction.SourceLang, direction.TargetLang);
        var systemPrompt = PromptBuilder.BuildSystemPrompt(pair);

        var client = new ResponsesClient(apiKey: apiKey);
        var options = new CreateResponseOptions { Model = model, StreamingEnabled = true };
        // System/developer instruction + the user's text. Confirm the exact factory names in the SDK.
        options.InputItems.Add(ResponseItem.CreateDeveloperMessageItem(systemPrompt));
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(text));

        await foreach (var update in client.CreateResponseStreamingAsync(options, ct))
        {
            if (update is StreamingResponseOutputTextDeltaUpdate delta && !string.IsNullOrEmpty(delta.Delta))
            {
                yield return delta.Delta;
            }
        }
    }
}
```

- [ ] **Step 3: Build Infrastructure (Windows)**

Run (on Windows): `dotnet build src/AiTranslator.Infrastructure`
Expected: PASS. If member names mismatch, fix them here only (see the verification note above).

- [ ] **Step 4: Manual integration check (Windows, optional, needs a dev key)**

Add a temporary throwaway console call (or a `[Trait("Integration","true")]` xUnit test excluded from
default runs) that loads the dev key from `.secrets/providers/openai.secrets.toml`, calls
`TranslateStreamAsync("سلام دنیا", new("fa","en"), "gpt-5.1", default)`, and prints chunks.
Expected: streamed English text (e.g. "Hello world"). Remove the throwaway code before committing.

- [ ] **Step 5: Commit**

```bash
git add src/AiTranslator.Infrastructure/Translation/OpenAiTranslationService.cs src/Directory.Packages.props
git commit -m "feat(infra): add OpenAI Responses streaming translation service"
```

---

### Task 8: CredentialManagerSecretStore (Infrastructure)

**Files:**
- Create: `src/AiTranslator.Infrastructure/Secrets/CredentialManagerSecretStore.cs`

**Interfaces:**
- Consumes: `ISecretStore`.
- Produces: `CredentialManagerSecretStore` storing the key under Credential Manager target `AI-Translator:OpenAI`.

- [ ] **Step 1: Add the package**

```bash
dotnet add src/AiTranslator.Infrastructure package Meziantou.Framework.Win32.CredentialManager
```

- [ ] **Step 2: Implement**

```csharp
// CredentialManagerSecretStore.cs
using AiTranslator.Core.Abstractions;
using Meziantou.Framework.Win32;

namespace AiTranslator.Infrastructure.Secrets;

public sealed class CredentialManagerSecretStore : ISecretStore
{
    private const string Target = "AI-Translator:OpenAI";
    private const string User = "openai";

    public string? GetApiKey()
        => CredentialManager.ReadCredential(Target)?.Password;

    public void SetApiKey(string apiKey)
        => CredentialManager.WriteCredential(Target, User, apiKey, CredentialPersistence.LocalMachine);

    public void DeleteApiKey()
        => CredentialManager.DeleteCredential(Target);
}
```

> Verify the exact method/enum names against the installed package (`ReadCredential`/`WriteCredential`/
> `DeleteCredential`, `CredentialPersistence`). Confirm `.Password` is the secret accessor.

- [ ] **Step 3: Build (Windows)**

Run (on Windows): `dotnet build src/AiTranslator.Infrastructure`
Expected: PASS.

- [ ] **Step 4: Manual verification (Windows)**

Throwaway: `var s = new CredentialManagerSecretStore(); s.SetApiKey("test123"); Console.WriteLine(s.GetApiKey()); s.DeleteApiKey();`
Expected: prints `test123`; then the credential disappears from Control Panel → Credential Manager →
Windows Credentials. Remove throwaway code.

- [ ] **Step 5: Commit**

```bash
git add src/AiTranslator.Infrastructure/Secrets/CredentialManagerSecretStore.cs src/Directory.Packages.props
git commit -m "feat(infra): store OpenAI key in Windows Credential Manager"
```

---

### Task 9: CsWin32 P/Invoke surface

**Files:**
- Create: `src/AiTranslator.Infrastructure/NativeMethods.txt`

**Interfaces:**
- Produces: generated P/Invokes used by tasks 10–12.

- [ ] **Step 1: Add CsWin32**

```bash
dotnet add src/AiTranslator.Infrastructure package Microsoft.Windows.CsWin32
```

- [ ] **Step 2: List the needed APIs in `NativeMethods.txt`**

```
RegisterHotKey
UnregisterHotKey
GetForegroundWindow
SetForegroundWindow
GetWindowThreadProcessId
AttachThreadInput
SendInput
GetClipboardSequenceNumber
VK_CONTROL
VK_A
VK_V
MOD_CONTROL
MOD_ALT
MOD_SHIFT
MOD_NOREPEAT
WM_HOTKEY
```

- [ ] **Step 3: Build (Windows) to generate the interop**

Run (on Windows): `dotnet build src/AiTranslator.Infrastructure`
Expected: PASS; `Windows.Win32.*` types become available (CsWin32 generates them at build).

- [ ] **Step 4: Commit**

```bash
git add src/AiTranslator.Infrastructure/NativeMethods.txt src/Directory.Packages.props
git commit -m "chore(infra): declare Win32 P/Invoke surface via CsWin32"
```

---

### Task 10: HotkeyService (Infrastructure, Win32)

**Files:**
- Create: `src/AiTranslator.Infrastructure/Input/HotkeyService.cs`

**Interfaces:**
- Consumes: `IHotkeyService`, CsWin32 `RegisterHotKey`/`UnregisterHotKey`, `WM_HOTKEY`.
- Produces: `HotkeyService(nint messageWindowHandle)` — the App passes its message-window HWND;
  raises `HotkeyPressed` when `WM_HOTKEY` arrives (the App forwards WM_HOTKEY to `OnMessage`).

- [ ] **Step 1: Implement** (parse "Ctrl+Alt+T" → modifiers + vk; register; expose a WndProc hook)

```csharp
// HotkeyService.cs
using AiTranslator.Core.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace AiTranslator.Infrastructure.Input;

public sealed class HotkeyService : IHotkeyService
{
    private const int HotkeyId = 0xA11;          // arbitrary app-unique id (0x0000–0xBFFF)
    private const uint ModNoRepeat = 0x4000;
    private readonly HWND _hwnd;
    private bool _registered;

    public HotkeyService(nint messageWindowHandle) => _hwnd = (HWND)messageWindowHandle;

    public event EventHandler? HotkeyPressed;

    public bool Register(string hotkey)
    {
        Unregister();
        var (mods, vk) = Parse(hotkey);
        _registered = PInvoke.RegisterHotKey(_hwnd, HotkeyId, (HOT_KEY_MODIFIERS)(mods | ModNoRepeat), vk);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            PInvoke.UnregisterHotKey(_hwnd, HotkeyId);
            _registered = false;
        }
    }

    /// <summary>Call from the host window's WndProc. Returns true if it handled WM_HOTKEY.</summary>
    public bool OnMessage(uint msg, nint wParam)
    {
        const uint WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && (int)wParam == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return false;
    }

    private static (uint Mods, uint Vk) Parse(string hotkey)
    {
        uint mods = 0, vk = 0;
        foreach (var raw in hotkey.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": mods |= 0x0002; break; // MOD_CONTROL
                case "ALT": mods |= 0x0001; break;               // MOD_ALT
                case "SHIFT": mods |= 0x0004; break;             // MOD_SHIFT
                case "WIN": mods |= 0x0008; break;               // MOD_WIN
                default:
                    vk = raw.Length == 1 ? raw[0] : vk;          // letters/digits map to their ASCII VK
                    break;
            }
        }
        return (mods, vk);
    }

    public void Dispose() => Unregister();
}
```

> Verify the CsWin32 `RegisterHotKey` overload signature (`HWND, int, HOT_KEY_MODIFIERS, uint`) and the
> `HOT_KEY_MODIFIERS` flag values against the generated interop; adjust the casts if needed.

- [ ] **Step 2: Build (Windows)**

Run (on Windows): `dotnet build src/AiTranslator.Infrastructure`
Expected: PASS.

- [ ] **Step 3: Manual verification deferred to Task 12** (needs the App's message window).

- [ ] **Step 4: Commit**

```bash
git add src/AiTranslator.Infrastructure/Input/HotkeyService.cs
git commit -m "feat(infra): add global hotkey service (RegisterHotKey)"
```

---

### Task 11: Focus capture + clipboard injector (Infrastructure, Win32)

**Files:**
- Create: `src/AiTranslator.Infrastructure/Input/ForegroundFocusTargetProvider.cs`, `Input/ClipboardTextInjector.cs`

**Interfaces:**
- Consumes: `IFocusTargetProvider`, `ITextInjector`, `FocusTarget`, CsWin32 input/clipboard P/Invokes.
- Produces: capture the foreground HWND before the overlay opens; replace target content via
  save-clipboard → set text → focus target → Ctrl+A → Ctrl+V → restore clipboard.

- [ ] **Step 1: Implement focus capture**

```csharp
// ForegroundFocusTargetProvider.cs
using AiTranslator.Core.Abstractions;
using Windows.Win32;

namespace AiTranslator.Infrastructure.Input;

public sealed class ForegroundFocusTargetProvider : IFocusTargetProvider
{
    public FocusTarget CaptureCurrent() => new((nint)PInvoke.GetForegroundWindow().Value);
}
```

- [ ] **Step 2: Implement the clipboard injector**

```csharp
// ClipboardTextInjector.cs
using System.Windows;                 // WPF Clipboard (STA)
using AiTranslator.Core.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace AiTranslator.Infrastructure.Input;

/// <summary>Replaces a target field's content via clipboard paste (ADR-0004).</summary>
public sealed class ClipboardTextInjector : ITextInjector
{
    public async Task ReplaceTextAsync(FocusTarget target, string text, CancellationToken ct)
    {
        // 1. Snapshot the user's clipboard (text only for M1; richer formats are M3).
        string? previous = SafeGetClipboardText();

        // 2. Put our text on the clipboard with retry (clipboard may be briefly locked).
        SetClipboardTextWithRetry(text);

        // 3. Focus the target window so the paste lands there.
        FocusTargetWindow((HWND)target.WindowHandle);
        await Task.Delay(40, ct);     // settle after activation

        // 4. Select-all then paste (replace whole content).
        SendCtrl(VIRTUAL_KEY.VK_A);
        SendCtrl(VIRTUAL_KEY.VK_V);

        // 5. Restore the user's clipboard after the target has read it.
        await Task.Delay(250, ct);
        if (previous is not null) SetClipboardTextWithRetry(previous);
    }

    private static void FocusTargetWindow(HWND target)
    {
        HWND fg = PInvoke.GetForegroundWindow();
        uint fgThread = PInvoke.GetWindowThreadProcessId(fg, default);
        uint tgtThread = PInvoke.GetWindowThreadProcessId(target, default);
        if (fgThread != tgtThread) PInvoke.AttachThreadInput(fgThread, tgtThread, true);
        try { PInvoke.SetForegroundWindow(target); }
        finally { if (fgThread != tgtThread) PInvoke.AttachThreadInput(fgThread, tgtThread, false); }
    }

    private static unsafe void SendCtrl(VIRTUAL_KEY key)
    {
        Span<INPUT> inputs =
        [
            KeyDown(VIRTUAL_KEY.VK_CONTROL), KeyDown(key),
            KeyUp(key), KeyUp(VIRTUAL_KEY.VK_CONTROL),
        ];
        PInvoke.SendInput(inputs, sizeof(INPUT));
    }

    private static INPUT KeyDown(VIRTUAL_KEY vk) => Key(vk, down: true);
    private static INPUT KeyUp(VIRTUAL_KEY vk) => Key(vk, down: false);

    private static INPUT Key(VIRTUAL_KEY vk, bool down) => new()
    {
        type = INPUT_TYPE.INPUT_KEYBOARD,
        Anonymous = new INPUT._Anonymous_e__Union
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                dwFlags = down ? 0 : KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP,
            },
        },
    };

    private static string? SafeGetClipboardText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch { return null; }
    }

    private static void SetClipboardTextWithRetry(string text)
    {
        for (int i = 0; i < 8; i++)
        {
            try { Clipboard.SetText(text); return; }
            catch { Thread.Sleep(30); }   // CLIPBRD_E_CANT_OPEN — another app holds the clipboard
        }
    }
}
```

> Notes: `System.Windows.Clipboard` requires STA — the App's UI thread is STA, so call injection on
> the dispatcher. Verify CsWin32 `INPUT`/`KEYBDINPUT` field names (`type`, `Anonymous.ki`, `wVk`,
> `dwFlags`) against the generated interop. M1 restores only text clipboard content; full-format
> snapshot/restore is M3 (ADR-0004).

- [ ] **Step 3: Build (Windows)**

Run (on Windows): `dotnet build src/AiTranslator.Infrastructure`
Expected: PASS.

- [ ] **Step 4: Manual verification deferred to Task 13** (needs the overlay).

- [ ] **Step 5: Commit**

```bash
git add src/AiTranslator.Infrastructure/Input/ForegroundFocusTargetProvider.cs src/AiTranslator.Infrastructure/Input/ClipboardTextInjector.cs
git commit -m "feat(infra): add focus capture + clipboard-paste text injector"
```

---

### Task 12: App shell — tray, DI, message window, hotkey wiring

**Files:**
- Create: `src/AiTranslator.App/app.manifest`, `Composition/ServiceConfiguration.cs`, `Shell/TrayIcon.xaml(.cs)`, `Windows/NonActivatingWindow.cs`; Modify: `App.xaml`, `App.xaml.cs`

**Interfaces:**
- Consumes: all Infrastructure services + Core interfaces.
- Produces: a running tray app; a hidden message window hosting the hotkey; DI container.

- [ ] **Step 1: Add App packages**

```bash
dotnet add src/AiTranslator.App package WPF-UI
dotnet add src/AiTranslator.App package H.NotifyIcon.Wpf
dotnet add src/AiTranslator.App package Microsoft.Extensions.DependencyInjection
```

- [ ] **Step 2: Add `app.manifest`** (PerMonitorV2 DPI) and reference it in the csproj
  (`<ApplicationManifest>app.manifest</ApplicationManifest>`).

```xml
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 3: DI composition**

```csharp
// ServiceConfiguration.cs
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Settings;
using AiTranslator.Infrastructure.Input;
using AiTranslator.Infrastructure.Secrets;
using AiTranslator.Infrastructure.Translation;
using Microsoft.Extensions.DependencyInjection;

namespace AiTranslator.App.Composition;

public static class ServiceConfiguration
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore(JsonSettingsStore.DefaultPath));
        services.AddSingleton<ISecretStore, CredentialManagerSecretStore>();
        services.AddSingleton<IFocusTargetProvider, ForegroundFocusTargetProvider>();
        services.AddSingleton<ITextInjector, ClipboardTextInjector>();
        services.AddSingleton<ITranslationService>(sp =>
            new OpenAiTranslationService(() => sp.GetRequiredService<ISecretStore>().GetApiKey()));
        return services.BuildServiceProvider();
    }
}
```

- [ ] **Step 4: NonActivatingWindow base** (applies `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`)

```csharp
// NonActivatingWindow.cs
using System.Windows;
using System.Windows.Interop;

namespace AiTranslator.App.Windows;

/// <summary>A Topmost window that never steals foreground focus, but is still clickable/typeable.</summary>
public class NonActivatingWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    public NonActivatingWindow()
    {
        ShowActivated = false;
        Topmost = true;
        ShowInTaskbar = false;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, ex | WsExNoActivate | WsExToolWindow);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(nint hWnd, int nIndex);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);
}
```

- [ ] **Step 5: App.xaml.cs** — start to tray, create a hidden message window, register the hotkey

```csharp
// App.xaml.cs (key parts)
using System.Windows;
using System.Windows.Interop;
using AiTranslator.App.Composition;
using AiTranslator.App.Windows;
using AiTranslator.Core.Abstractions;
using AiTranslator.Infrastructure.Input;
using Microsoft.Extensions.DependencyInjection;

namespace AiTranslator.App;

public partial class App : Application
{
    private ServiceProvider _services = null!;
    private HwndSource _msgSource = null!;
    private HotkeyService _hotkey = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;     // tray app: don't exit when windows close
        _services = ServiceConfiguration.Build();

        // Hidden message-only window to receive WM_HOTKEY.
        var parameters = new HwndSourceParameters("AiTranslatorMsgWindow") { WindowStyle = 0 };
        _msgSource = new HwndSource(parameters);
        _hotkey = new HotkeyService(_msgSource.Handle);
        _msgSource.AddHook((nint hwnd, int msg, nint w, nint l, ref bool handled) =>
        {
            if (_hotkey.OnMessage((uint)msg, w)) handled = true;
            return nint.Zero;
        });
        _hotkey.HotkeyPressed += (_, _) => ShowOverlay();

        var settings = _services.GetRequiredService<ISettingsStore>().Load();
        if (!_hotkey.Register(settings.Hotkey))
        {
            // Surface a tray balloon: "hotkey taken, pick another in Settings" (real state, not a stub).
        }
        // TrayIcon is created from App.xaml resources (Task 12 Step 6).
    }

    private void ShowOverlay() { /* implemented in Task 13 */ }
}
```

- [ ] **Step 6: TrayIcon** (H.NotifyIcon `TaskbarIcon` with a menu: Settings, Exit) in `App.xaml`
  resources, wired to open `SettingsWindow` (Task 14) and `Shutdown()`.

- [ ] **Step 7: Build & run (Windows)**

Run (on Windows): `dotnet run --project src/AiTranslator.App`
Expected: app starts to the tray; pressing `Ctrl+Alt+T` triggers `ShowOverlay` (a breakpoint/log
confirms). Tray menu opens Settings / exits.

- [ ] **Step 8: Commit**

```bash
git add src/AiTranslator.App
git commit -m "feat(app): tray shell, DI, message window, global hotkey wiring"
```

---

### Task 13: OverlayInputWindow — type, debounce, stream, inject

**Files:**
- Create: `src/AiTranslator.App/Windows/OverlayInputWindow.xaml(.cs)`; Modify: `App.xaml.cs` `ShowOverlay`

**Interfaces:**
- Consumes: `IFocusTargetProvider`, `ITranslationService`, `ITextInjector`, `LanguageDirector`, `AppSettings`.
- Produces: the floating input box behavior from spec §2 (debounced live-replace, `Enter`=newline, `Esc`=close).

- [ ] **Step 1: XAML** — a `NonActivatingWindow` (root) hosting a multiline, RTL-capable `TextBox`
  (WPF-UI styled, `AcceptsReturn=True`, `FlowDirection=RightToLeft`, Vazirmatn font). No buttons.

- [ ] **Step 2: Code-behind** — capture target on show; debounce; stream; inject

```csharp
// OverlayInputWindow.xaml.cs (core logic)
using System.Windows;
using System.Windows.Threading;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Models;
using AiTranslator.Core.Translation;

namespace AiTranslator.App.Windows;

public partial class OverlayInputWindow : NonActivatingWindow
{
    private readonly IFocusTargetProvider _focus;
    private readonly ITranslationService _translator;
    private readonly ITextInjector _injector;
    private readonly AppSettings _settings;

    private readonly DispatcherTimer _debounce;
    private FocusTarget _target;
    private CancellationTokenSource? _inflight;

    public OverlayInputWindow(IFocusTargetProvider focus, ITranslationService translator,
        ITextInjector injector, AppSettings settings)
    {
        InitializeComponent();
        _focus = focus; _translator = translator; _injector = injector; _settings = settings;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(settings.DebounceMs) };
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await TranslateAndInjectAsync(); };
        Input.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };  // Input = the TextBox
        // Enter => newline is the TextBox default with AcceptsReturn=True (do not intercept it).
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
    }

    /// <summary>Capture the target BEFORE the overlay takes focus, then show.</summary>
    public void ShowFor()
    {
        _target = _focus.CaptureCurrent();
        Input.Clear();
        Show();
        Input.Focus();
    }

    private async Task TranslateAndInjectAsync()
    {
        _inflight?.Cancel();
        _inflight?.Dispose();
        _inflight = new CancellationTokenSource();
        var ct = _inflight.Token;

        var text = Input.Text;
        if (string.IsNullOrWhiteSpace(text)) return;

        var dir = LanguageDirector.Resolve(text, _settings.LanguagePair, _settings.AutoDirection);
        var sb = new System.Text.StringBuilder();
        try
        {
            await foreach (var chunk in _translator.TranslateStreamAsync(text, dir, _settings.Model, ct))
                sb.Append(chunk);
            await _injector.ReplaceTextAsync(_target, sb.ToString(), ct);
        }
        catch (OperationCanceledException) { /* superseded by newer input — ignore */ }
    }
}
```

- [ ] **Step 3: Wire `ShowOverlay` in App.xaml.cs** to construct (once) and `ShowFor()` the overlay,
  passing the loaded `AppSettings` and resolved services.

- [ ] **Step 4: Build & run end-to-end (Windows) — M1 acceptance**

Run (on Windows): `dotnet run --project src/AiTranslator.App`. Then: open Notepad (Win32, the simplest
target), click in it, press `Ctrl+Alt+T`, type `سلام دنیا`, pause 0.5 s.
Expected: Notepad's content is replaced with the streamed English translation (e.g. `Hello world`);
the overlay never stole focus; `Enter` in the overlay added a newline; `Esc` closed it; the clipboard
is restored. Repeat against WhatsApp/Telegram and note any per-app quirks (those are tuned in M2/M3).

- [ ] **Step 5: Commit**

```bash
git add src/AiTranslator.App/Windows/OverlayInputWindow.xaml src/AiTranslator.App/Windows/OverlayInputWindow.xaml.cs src/AiTranslator.App/App.xaml.cs
git commit -m "feat(app): overlay input with debounced streaming translate + inject"
```

---

### Task 14: SettingsWindow — key entry, language pair, model, hotkey

**Files:**
- Create: `src/AiTranslator.App/Windows/SettingsWindow.xaml(.cs)`, `Resources/Strings.en.resx`

**Interfaces:**
- Consumes: `ISettingsStore`, `ISecretStore`, `AppSettings`.
- Produces: a normal (activating) window to edit settings + enter the API key (PasswordBox → `SetApiKey`).

- [ ] **Step 1: XAML** — WPF-UI `FluentWindow` with: API key `PasswordBox` + Save; primary/secondary
  language text boxes; model text box (default `gpt-5.1`); hotkey text box; auto-direction + run-at-startup
  toggles. All labels from `Strings.en.resx` (English source).

- [ ] **Step 2: Code-behind** — load current settings/key on open; on Save, persist via `ISettingsStore`
  and `ISecretStore.SetApiKey`, re-register the hotkey, and report a taken-combo failure inline.

- [ ] **Step 3: First-run** — in `App.OnStartup`, if `ISecretStore.GetApiKey()` is null, open
  `SettingsWindow` so the user can enter the key before first use.

- [ ] **Step 4: Build & run (Windows)**

Run (on Windows): open Settings from the tray; enter a key; change the language pair; Save; confirm
`%APPDATA%\AI-Translator\settings.json` updates and the key appears in Credential Manager; translation
then works without the dev `.secrets` key.
Expected: all persist; hotkey rebind takes effect.

- [ ] **Step 5: Commit**

```bash
git add src/AiTranslator.App/Windows/SettingsWindow.xaml src/AiTranslator.App/Windows/SettingsWindow.xaml.cs src/AiTranslator.App/Resources/Strings.en.resx
git commit -m "feat(app): settings window with key entry and persistence"
```

---

### Task 15: Docs sync + M1 close-out

**Files:**
- Modify: `docs/reference/source-layout.md` (JsonSettingsStore lives in `Core`; reflect actual tree),
  `docs/architecture/overview.md` §9 (tick M1), `docs/reference/openai-models.md` (note default `gpt-5.1`
  matches the SDK example; mini/nano are opt-in).

- [ ] **Step 1: Update the three docs** to match what was built (same-change docs rule). Verify no
  broken links (`grep`-check relative links as in the scaffold review).

- [ ] **Step 2: Run the full cross-platform test suite**

Run: `dotnet test src/AiTranslator.Tests`
Expected: PASS (all Core tests).

- [ ] **Step 3: Commit**

```bash
git add docs/
git commit -m "docs: sync source layout + roadmap after M1"
```

- [ ] **Step 4: Request review/merge** — do NOT merge to `develop` without explicit permission. When
  granted, squash this branch into one clean commit and `--no-ff` merge into `develop` per the git
  standard.

---

## Self-Review

**Spec coverage (overview §2 behavior + §4 components):**
- Hotkey-opened overlay → Tasks 10, 12, 13. ✓
- Type → debounce 500 ms → Task 13. ✓
- Stream translation (Responses API) → Tasks 5, 7. ✓
- Replace messenger field content via clipboard-paste → Tasks 11, 13. ✓
- `Enter`=newline, `Esc`=close, no auto-send → Task 13. ✓
- Multi-language + auto-direction → Tasks 2, 3, 4. ✓
- Settings JSON + key in Credential Manager → Tasks 6, 8, 14. ✓
- Tray + DI + first-run key capture → Tasks 12, 14. ✓
- Non-activating overlay (`WS_EX_NOACTIVATE`) → Task 12 (`NonActivatingWindow`). ✓
- **Deferred (not M1):** badge + auto field-detection (M2), Chromium/Qt coverage (M3), code-signing
  + Velopack (M4). Explicitly out of scope per overview §9 — not gaps.

**Placeholder scan:** Core tasks have complete test+impl code. Interop/UI tasks have concrete code +
explicit Windows verification (the honest analog to unit tests for Win32/WPF). The only "implemented
later" pointer (`ShowOverlay` in Task 12 → Task 13) is a deliberate forward reference, resolved in
the next task. No vague "add error handling"/"TODO" steps.

**Type consistency:** `ITranslationService.TranslateStreamAsync(text, TranslationDirection, model, ct)`,
`ITextInjector.ReplaceTextAsync(FocusTarget, text, ct)`, `IFocusTargetProvider.CaptureCurrent() →
FocusTarget`, `IHotkeyService.Register(string)/HotkeyPressed`, `ISettingsStore.Load()/Save()`,
`ISecretStore.GetApiKey()/SetApiKey()/DeleteApiKey()` are used identically across producer and consumer
tasks. `AppSettings` property names match `reference/configuration.md`.

**Known verification debts (flagged in-task, not placeholders):** exact OpenAI SDK member names
(Task 7), Meziantou API names (Task 8), and CsWin32 generated shapes (Tasks 10, 11) must be confirmed
against the installed packages in-IDE — each task carries that verification step. These are isolated
behind interfaces so any drift is single-file.
