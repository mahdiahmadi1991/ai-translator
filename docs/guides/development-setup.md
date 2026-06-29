# Development setup

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-29

How to set up to build, run, and test on Windows. The `src/AiTranslator.slnx` solution already exists.

## Prerequisites (Windows)

- **Windows 10 (1809+) or Windows 11.** Required to build/run — WPF is Windows-only
  ([ADR-0001](../architecture/decision-records/0001-platform-dotnet10-wpf.md)).
- **.NET 10 SDK** (`dotnet --version` ≥ 10.0). Includes the Windows Desktop workload for WPF.
- **WebView2 Runtime** (ships with Windows 11; otherwise install the Evergreen runtime) — needed to
  exercise WhatsApp/Chromium targets.
- An IDE: Visual Studio 2026 (WPF designer) or VS Code with the C# Dev Kit.
- Useful for debugging accessibility: **Accessibility Insights for Windows** / `Inspect.exe` to see
  how target apps expose their text fields.

## Offline packages

The host network can't reach `nuget.org`. Packages are served from a git-ignored `.nuget-packages/`
fallback folder on disk (wired via `src/Directory.Build.props`), so restore/build works without
network. Do not delete that folder; if a package is missing, repopulate it from a networked machine —
see [offline-build.md](offline-build.md).

## Dev OpenAI key

The released app reads the key from Windows Credential Manager. For local DEBUG runs you can instead
use a dev key file:

```bash
cp .secrets/providers/openai.secrets.example.toml .secrets/providers/openai.secrets.toml
# edit it and paste your real sk-... key; the real file is git-ignored
```

See [.secrets/README.md](../../.secrets/README.md) and
[ADR-0005](../architecture/decision-records/0005-secret-storage-credential-manager.md).

## Next

- Build/run/publish commands: [build-and-run.md](build-and-run.md).
- Design & decisions: [../architecture/overview.md](../architecture/overview.md).
