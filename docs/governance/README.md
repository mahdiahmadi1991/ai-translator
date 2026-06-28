# Governance

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-28

This project follows the global standards in `~/.claude/standards/` (documentation structure, git
workflow, secrets management, code quality). They apply as written; this page records only
**project-specific** points and overrides.

## Project-specific points

- **Spec location:** the brainstorming default (`docs/superpowers/specs/`) is **overridden** — the
  design lives in [../architecture/overview.md](../architecture/overview.md) with decisions split into
  [ADRs](../architecture/decision-records/README.md), matching the documentation-structure standard.
- **Decisions need ADRs:** any load-bearing technical choice (or new dependency beyond the approved
  set in [source-layout.md](../reference/source-layout.md)) is recorded as an ADR before adoption.
- **Build verification:** Windows-only — a WPF build/run cannot be verified from Linux/WSL; never
  claim it passes there ([ADR-0001](../architecture/decision-records/0001-platform-dotnet10-wpf.md)).
- **Security posture:** the app reads global focus and injects keystrokes; treat code signing,
  the app allowlist, and secret redaction as release requirements
  ([overview §8](../architecture/overview.md#8-cross-cutting-concerns)).

## Working agreement (from the user's rules)

- Reply to the user in Persian; author all repo artifacts in English.
- Never commit/push/merge without explicit permission in the conversation.
- Secrets only under git-ignored `.secrets/`; never in code/config/logs.
