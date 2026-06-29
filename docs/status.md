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
| `AiTranslator.Infrastructure` + `AiTranslator.App` (`net10.0-windows`) | **Code written, not yet compiled** | first Windows build is the active task |
| Docs, ADRs, M1 & M2 plans | Current | this dashboard |

- **Milestone:** M1 (walking skeleton) code is complete; the **M2 foundation** (pure Core logic:
  `AppActivationPolicy`, `HotkeyCombination`) is done and tested. Roadmap:
  [overview §9](architecture/overview.md#9-roadmap).
- **Branch:** everything is merged into **`develop`**. New work goes on `feature/*` / `fix/*`
  branches off `develop`. Commit/merge is **pre-authorized for this project** (see [CLAUDE.md](../CLAUDE.md) § Git).

## Your immediate task: get a green Windows build

The Infrastructure/App C# was authored on Linux and never compiled. On Windows you can finally build,
verify, and fix it. Do this in order:

1. **Build.** [guides/build-and-run.md](guides/build-and-run.md). Restore is **offline** (the host
   network can't reach nuget.org) — see [guides/offline-build.md](guides/offline-build.md).
2. **Fix the flagged SDK/interop usages** until the build is green — each is isolated to one file:
   [guides/windows-build-checklist.md](guides/windows-build-checklist.md) §2–§5 (OpenAI SDK,
   Credential Manager, CsWin32 `INPUT`/`SendInput`, H.NotifyIcon/`System.Drawing`).
3. **Run the M1 acceptance test** (hotkey → type Persian → translation replaces the focused field):
   [windows-build-checklist.md §6](guides/windows-build-checklist.md#6-acceptance-test-manual).
4. **Commit each fix** (small, coherent) and keep `dotnet test` green.

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
