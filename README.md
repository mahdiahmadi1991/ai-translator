# AI Translator

Grammarly-style inline translation for **any** text field on Windows. A small badge appears
beside the editable field you're in; click it, type in your own language, and the translation
is written straight into that field, ready to send.

[![ci](https://github.com/mahdiahmadi1991/ai-translator/actions/workflows/ci.yml/badge.svg)](https://github.com/mahdiahmadi1991/ai-translator/actions/workflows/ci.yml)
[![release](https://github.com/mahdiahmadi1991/ai-translator/actions/workflows/release.yml/badge.svg)](https://github.com/mahdiahmadi1991/ai-translator/actions/workflows/release.yml)
[![latest version](https://img.shields.io/github/v/release/mahdiahmadi1991/ai-translator)](https://github.com/mahdiahmadi1991/ai-translator/releases/latest)

- **Platform:** .NET 10 + WPF (Windows 10/11)
- **Translation:** OpenAI (streaming)
- **UI:** Fluent design via WPF-UI

## Download

Grab the latest installer from the
[**Releases**](https://github.com/mahdiahmadi1991/ai-translator/releases/latest) page, or directly:

- [**AiTranslator-win-Setup.exe**](https://github.com/mahdiahmadi1991/ai-translator/releases/latest/download/AiTranslator-win-Setup.exe) (installer, self-contained, auto-updates)
- [AiTranslator-win-Portable.zip](https://github.com/mahdiahmadi1991/ai-translator/releases/latest/download/AiTranslator-win-Portable.zip) (no install)

The build isn't code-signed yet, so on first run Windows SmartScreen may warn you. Choose
**More info → Run anyway**. Installed copies update themselves from GitHub Releases.

First run: open **Settings** from the tray icon and paste your OpenAI API key (it's stored in
Windows Credential Manager, never in a file).

## How it works

1. A system-wide focus watcher notices when you focus an editable text field (the same
   accessibility approach Grammarly uses, including waking WebView2 apps like WhatsApp).
2. A floating **badge** anchors beside that field. It shows in every app by default; add apps
   to the **block list** in Settings to hide it there.
3. Click the badge (or press the global hotkey) to open a floating box.
4. Type your text and press **Translate** (or Ctrl+Enter). The translation is appended into the
   target field with the caret left at the end, so you can review and send.

## Build from source

```powershell
dotnet build src/AiTranslator.slnx -c Debug
dotnet test  src/AiTranslator.slnx -c Debug
dotnet run   --project src/AiTranslator.App
```

Standard online restore from `nuget.org`; needs the .NET 10 SDK on Windows.

## Releases

Releases are automated. Merging into `main` runs the
[release workflow](.github/workflows/release.yml): the version and `CHANGELOG.md` are derived
from [Conventional Commits](https://www.conventionalcommits.org/), then Velopack builds the
installer and publishes a GitHub Release. See [CHANGELOG.md](CHANGELOG.md) for what changed.

## Documentation

Everything lives under [`docs/`](docs/README.md).

- **Design / architecture:** [docs/architecture/overview.md](docs/architecture/overview.md)
- **Decisions (ADRs):** [docs/architecture/decision-records/](docs/architecture/decision-records/README.md)
- **Configuration:** [docs/reference/configuration.md](docs/reference/configuration.md)
- **Developer setup:** [docs/guides/development-setup.md](docs/guides/development-setup.md)

## Conventions

- Files & directories: `lowercase-kebab-case`. C#: language-idiomatic PascalCase.
- Commit messages follow Conventional Commits (they drive versioning + the changelog).
- Secrets live **only** under the git-ignored [`.secrets/`](.secrets/README.md) folder.
- Branch model: work on `feature/…` / `fix/…` off `develop`; merge `develop` into `main` to release.
- See [CLAUDE.md](CLAUDE.md) for the full contributor working rules.
