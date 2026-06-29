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
| `AiTranslator.Core` + `AiTranslator.Tests` (`net10.0`) | **Built, 40/40 tests green** | `dotnet test` |
| `AiTranslator.Infrastructure` + `AiTranslator.App` (`net10.0-windows`) | **Built green on Windows (0/0); app launches; M1 + M2 awareness machinery** | `dotnet build AiTranslator.slnx` + smoke run (tray + Settings; focus watcher runs live) |
| Docs, ADRs, M1 & M2 plans | Current | this dashboard |

- **Milestone:** M1 (walking skeleton) builds green on Windows; **M2 — Grammarly awareness** machinery
  is **built and wired** (FocusWatcher, TargetResolver via managed UIA, non-activating BadgeWindow,
  per-app `appOffsets`, allow/blocklist + live-validated hotkey in Settings). Roadmap:
  [overview §9](architecture/overview.md#9-roadmap).
- **Branch:** everything is merged into **`develop`**. New work goes on `feature/*` / `fix/*`
  branches off `develop`. Commit/merge is **pre-authorized for this project** (see [CLAUDE.md](../CLAUDE.md) § Git).

## Your immediate task: run the M1 acceptance test

The first Windows build is **done and green**. The Infrastructure/App C# (authored on Linux, never
compiled) now builds with 0 warnings / 0 errors, `dotnet test` is green (40/40 after M2), and the app launches into
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

## M2 — Grammarly awareness: built; needs per-app verification

The M2 machinery is implemented and builds 0/0 ([plans/2026-06-29-m2-awareness.md](plans/2026-06-29-m2-awareness.md)):
`FocusWatcher` (system-wide `SetWinEventHook` on an STA pump), `TargetResolver` (managed
`System.Windows.Automation` — no NuGet, offline-friendly), the non-activating `BadgeWindow`, the App
wiring (badge shows on focus in allowlisted apps → click opens the overlay targeting that field; the
hotkey path is unchanged), per-app `appOffsets`, and an allow/blocklist + live-validated-hotkey
Settings editor. Smoke-run confirms the watcher starts and runs without crashing.

**What's left for M2** (needs a human on Windows, with the real apps + a key):

1. Focus an allowlisted app (WhatsApp/Telegram) → confirm the badge appears beside the input field and
   not elsewhere; click it → overlay opens targeting that field → translation replaces it.
2. Tune per-app placement (`appOffsets`) and confirm no focus-steal / multi-monitor-DPI correctness.
3. If WhatsApp(WebView2)/Electron don't expose a field via UIA, add the deferred IAccessible2 wake
   (TargetResolver scope note) — only if testing shows UIA is insufficient.

Deferred to **M3**: non-activating *overlay* + live-inject without focus flicker; deep Qt/Electron tuning.

## Quick reference

- **Build / test / run:** [guides/build-and-run.md](guides/build-and-run.md)
- **Dev environment + OpenAI dev key:** [guides/development-setup.md](guides/development-setup.md)
- **Offline NuGet:** [guides/offline-build.md](guides/offline-build.md)
- **Source map + approved dependencies:** [reference/source-layout.md](reference/source-layout.md)
- **Config schema:** [reference/configuration.md](reference/configuration.md) ·
  **models/pricing:** [reference/openai-models.md](reference/openai-models.md)
- **Decisions:** [architecture/decision-records/](architecture/decision-records/README.md)
