# AI Translator — project agent guidance

Working rules specific to this repository. Global rules in `~/.claude/global-rules.md` and the
standards in `~/.claude/standards/` still apply; this file only adds project-specific context and
overrides where noted.

## Start here

New to this repo? Read **[docs/status.md](docs/status.md)** first — current state, what's verified,
and the immediate next task. Development now happens **natively on Windows**.

## What this project is

A Windows desktop app (.NET 10 + WPF) that shows a Grammarly-style badge beside the focused text
field in allowlisted messaging apps, lets the user type in their own language in a floating box,
and streams an OpenAI translation **into the messenger's real input box**. Full design:
[docs/architecture/overview.md](docs/architecture/overview.md).

## Where things live

- **Docs:** [`docs/`](docs/README.md) — the single source of truth. Update docs in the same change
  as the behavior they describe. Route new docs via [docs/_meta/document-routing.md](docs/_meta/document-routing.md).
- **Decisions:** [`docs/architecture/decision-records/`](docs/architecture/decision-records/README.md).
  Add an ADR before reversing or making a load-bearing technical choice.
- **Source layout:** [docs/reference/source-layout.md](docs/reference/source-layout.md). The `src/`
  solution (`.slnx`) exists: `Core`/`Tests` are built and green; `Infrastructure`/`App` are written
  and await their first Windows build.
- **Secrets:** only under [`.secrets/`](.secrets/README.md) (git-ignored). The user's OpenAI key is
  never stored in the repo, in code, or in committed config.

## Build & run reality

- **Build/run/test natively on Windows** (target `net10.0-windows`). You can — and must — verify
  every change here: run `dotnet build` / `dotnet test` and the manual acceptance test, and report
  the actual output. No "passes" claims without it.
- **Offline NuGet:** the host network can't reach `nuget.org`. Packages are served from a git-ignored
  `.nuget-packages/` fallback folder via `RestoreFallbackFolders` in `src/Directory.Build.props`.
  If a package is missing, repopulate it from a networked machine — see
  [docs/guides/offline-build.md](docs/guides/offline-build.md).
- The app reads global focus and injects keystrokes; it cannot interact with windows running as
  Administrator unless it is elevated too (UIPI). See the security notes in the overview.

## Conventions

- Files/dirs: `lowercase-kebab-case`. C#: PascalCase types/members, `_camelCase` private fields,
  one public type per file, file-scoped namespaces, nullable enabled.
- **Package-first:** prefer a maintained NuGet package over hand-rolling a generic concern; the
  approved dependency set is recorded in the ADRs. Ask before adding anything not listed there.
- Keep modules small and single-responsibility (see the component map in the overview); isolate the
  Win32/UIA/clipboard interop behind explicit abstractions so the core stays testable.
- All user-facing strings go through localization (English is the source language); no hard-coded
  non-English UI text.

## Git

`main` / `test` / `develop` are protected. Work on `feature/…` / `fix/…` / `chore/…` branches off
`develop`; promote `feature → develop` via squash + `--no-ff` (per the git standard). The solution
file is `src/AiTranslator.slnx`.

**Commit & merge are pre-authorized for this project** — commit coherent units and merge into
`develop` without pausing for per-change approval. There is **no git remote**, so do not `push`.
