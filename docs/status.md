# Project status — start here

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-29

The onboarding dashboard for anyone (human or agent) picking up this project. It links out to the
canonical docs — it does not repeat them.

**What this is:** a Windows desktop translator (.NET 10 + WPF), Grammarly-style. Full design:
[architecture/overview.md](architecture/overview.md). Docs map: [README.md](README.md).

## Where the work stands

| Layer | State | Evidence |
| --- | --- | --- |
| `AiTranslator.Core` + `AiTranslator.Tests` (`net10.0`) | **Built, 31/31 tests green** | `dotnet test` |
| `AiTranslator.Infrastructure` + `AiTranslator.App` (`net10.0-windows`) | **Built green on Windows (0 warn / 0 err); app launches** | `dotnet build AiTranslator.slnx` + smoke run (lands in tray, opens Settings on first run) |
| Docs, ADRs, M1 & M2 plans | Current | this dashboard |

- **Milestone:** M1 (walking skeleton) code is complete; the **M2 foundation** (pure Core logic:
  `AppActivationPolicy`, `HotkeyCombination`) is done and tested. Roadmap:
  [overview §9](architecture/overview.md#9-roadmap).
- **Branch:** everything is merged into **`develop`**. New work goes on `feature/*` / `fix/*`
  branches off `develop`. Commit/merge is **pre-authorized for this project** (see [CLAUDE.md](../CLAUDE.md) § Git).

## Your immediate task: run the M1 acceptance test

The first Windows build is **done and green**. The Infrastructure/App C# (authored on Linux, never
compiled) now builds with 0 warnings / 0 errors, `dotnet test` is 31/31, and the app launches into
the tray without a startup crash. Only two local fixes were needed (commit `fix(build): green first
Windows build`): mark two interop methods `unsafe` (CS0214) and scope-suppress `OPENAI001` for the
[Experimental] OpenAI Responses API. The other checklist items (Credential Manager, CsWin32
`INPUT`/`SendInput`, H.NotifyIcon) compiled and started as written.

What remains to close M1 is the **manual acceptance test**, which needs a human + a real OpenAI key:

1. **Provide a key.** Run the app (`dotnet run --project src/AiTranslator.App`); on first run it opens
   Settings — paste an `sk-...` key (persisted to Windows Credential Manager) and set the language pair.
2. **Acceptance test.** Notepad → focus it → hotkey (`Ctrl+Alt+T`) → type `سلام دنیا` → the field is
   replaced with the English translation: [windows-build-checklist.md §6](guides/windows-build-checklist.md#6-acceptance-test-manual).

> Known gap: [development-setup.md](guides/development-setup.md) describes a DEBUG `.secrets/providers/openai.secrets.toml`
> dev-key fallback, but `CredentialManagerSecretStore` does not yet read it — for now paste the key via Settings.

## After M1 is green on Windows

Execute the next milestone from its plan: **M2 — Grammarly-style awareness** (badge auto-appearance):
[plans/2026-06-29-m2-awareness.md](plans/2026-06-29-m2-awareness.md). Its pure-logic foundation is
already done and tested; the remaining tasks are Windows-only (FocusWatcher, TargetResolver,
BadgeWindow) and now buildable/verifiable on this machine.

## Quick reference

- **Build / test / run:** [guides/build-and-run.md](guides/build-and-run.md)
- **Dev environment + OpenAI dev key:** [guides/development-setup.md](guides/development-setup.md)
- **Offline NuGet:** [guides/offline-build.md](guides/offline-build.md)
- **Source map + approved dependencies:** [reference/source-layout.md](reference/source-layout.md)
- **Config schema:** [reference/configuration.md](reference/configuration.md) ·
  **models/pricing:** [reference/openai-models.md](reference/openai-models.md)
- **Decisions:** [architecture/decision-records/](architecture/decision-records/README.md)
