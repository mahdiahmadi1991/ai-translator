# Build & run

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-29

> Commands run **on Windows** (PowerShell or a dev shell). Restore is **offline** — packages come
> from the local `.nuget-packages/` fallback folder ([offline-build.md](offline-build.md)); no
> `nuget.org` access is needed.

## Restore & build

```powershell
dotnet restore src/AiTranslator.slnx
dotnet build   src/AiTranslator.slnx -c Debug
```

## Run

```powershell
dotnet run --project src/AiTranslator.App
```

The app starts to the tray. Open settings from the tray icon to enter the OpenAI key (or rely on the
DEBUG `.secrets` dev key), choose the language pair, and set the hotkey.

## Test

```powershell
dotnet test src/AiTranslator.Tests
```

Unit tests cover the platform-agnostic `Core` logic (language direction, prompt building, settings)
with the Windows interfaces mocked — they run without a live desktop.

## Publish (release)

```powershell
dotnet publish src/AiTranslator.App -c Release -r win-x64 ^
  -p:PublishSingleFile=true --self-contained false
```

Then package + sign with Velopack for the installer and auto-update
([ADR-0006](../architecture/decision-records/0006-distribution-velopack.md)). Authenticode-sign the
output to avoid AV/SmartScreen friction.

## Verification expectations

Per the project rules, never claim a build/test "passes" without showing the command output. Windows
GUI behavior (badge appearance, injection into WhatsApp/Telegram) is verified by manual run on
Windows, app by app — the focus/injection paths are inherently per-app
([ADR-0003](../architecture/decision-records/0003-focus-detection-winevent-uia-ia2.md)).
