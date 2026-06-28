# Decision records (ADRs)

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-28

Load-bearing technical decisions and their rationale. An ADR is **immutable once `Accepted`**: to
change a decision, add a new ADR that supersedes the old one (mark the old `Superseded` with a link).
Start new ones from [../../templates/adr-template.md](../../templates/adr-template.md).

| # | Decision | Status |
| --- | --- | --- |
| [0001](0001-platform-dotnet10-wpf.md) | Platform: .NET 10 + WPF (Windows) | Accepted |
| [0002](0002-translation-openai-responses-streaming.md) | Translation via OpenAI Responses API streaming (not Realtime) | Accepted |
| [0003](0003-focus-detection-winevent-uia-ia2.md) | Field detection via SetWinEventHook + UI Automation + IAccessible2 (Grammarly model) | Accepted |
| [0004](0004-text-injection-clipboard-paste.md) | Text injection via clipboard-paste primary, with fallbacks | Accepted |
| [0005](0005-secret-storage-credential-manager.md) | API-key storage in Windows Credential Manager | Accepted |
| [0006](0006-distribution-velopack.md) | Distribution & auto-update via Velopack | Accepted |

These are grounded in the research summarized in [../overview.md](../overview.md). Where a fact may
drift over time (model names, SDK versions), the ADR says so and points to the reference docs.
