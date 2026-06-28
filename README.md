# AI Translator

A Windows desktop utility that brings **Grammarly-style inline translation** to any
messaging app. When you focus a text box in an allowlisted messenger (WhatsApp, Telegram,
…), a small badge appears beside it. Click the badge, type in your own language, and the
translation streams **live into the messenger's input box** — ready for you to send.

- **Platform:** .NET 10 + WPF (Windows only)
- **Translation:** OpenAI Responses API (streaming text)
- **Status:** Design phase — see the design before any code is written.

## How it works (at a glance)

1. A system-wide focus watcher detects when you focus an editable text field in an
   allowlisted app (the same accessibility technique Grammarly uses).
2. A floating **badge** anchors next to that field.
3. Clicking the badge opens a simple floating **input box**.
4. As you type (debounced ~500 ms), the text is translated and the result **replaces the
   content of the messenger's real input box**. `Enter` in the box inserts a newline.
5. You review and press the messenger's own Send.

## Documentation

Everything lives under [`docs/`](docs/README.md) — start at the docs front door.

- **Design / architecture:** [docs/architecture/overview.md](docs/architecture/overview.md)
- **Key decisions (ADRs):** [docs/architecture/decision-records/](docs/architecture/decision-records/README.md)
- **Configuration:** [docs/reference/configuration.md](docs/reference/configuration.md)
- **Developer setup:** [docs/guides/development-setup.md](docs/guides/development-setup.md)

## Repository conventions

- Files & directories: `lowercase-kebab-case`. C# identifiers: language-idiomatic PascalCase.
- Secrets live **only** under the git-ignored [`.secrets/`](.secrets/README.md) folder.
- Branch model: `main` / `test` / `develop`; all work on child branches off `develop`.
- See [CLAUDE.md](CLAUDE.md) for agent/contributor working rules.
