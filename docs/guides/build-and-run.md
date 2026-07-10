# Build & run

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-29

> Commands run **on Windows** (PowerShell or a dev shell). Restore is a standard online restore from
> `nuget.org` (pinned as the single source in `src/nuget.config`).

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

> **Smart App Control / WDAC:** a freshly-built, unsigned `AiTranslator.App.exe` (apphost) is blocked
> by Windows ("Part of this app has been blocked … can't confirm who published…"). So **Debug builds
> emit no `.exe`** (`UseAppHost=false`); run through the trusted **dotnet host**, which is not blocked:
> `dotnet run --project src/AiTranslator.App`, or the fast path on the last build
> `dotnet "src/AiTranslator.App/bin/Debug/net10.0-windows/AiTranslator.App.dll"`. The release apphost
> is Authenticode-signed (see Publish) so end users don't hit this.

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
