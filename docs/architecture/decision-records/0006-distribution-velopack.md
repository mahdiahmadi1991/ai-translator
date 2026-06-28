# ADR-0006: Distribution & auto-update via Velopack

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-28

## Context

A single-user desktop tool needs a simple installer and frictionless updates, without the overhead
of MSIX packaging/signing pipelines. The app also uses global hooks and `SendInput`, which can trip
AV/SmartScreen heuristics on unsigned binaries.

## Decision

Publish the WPF app (framework-dependent single-file by default; self-contained as an option) and
wrap it with **Velopack** — the maintained Squirrel successor — for the installer plus silent
auto-update. Velopack keeps the executable at a stable path, supports delta updates, and updates
without a UAC prompt. Plan to **Authenticode-sign** the release to reduce AV/SmartScreen friction.

## Alternatives considered

- **MSIX** — Store-ready and clean identity, but heavier packaging/signing; its per-machine secure
  install location matters only if we ever pursue UIAccess. Revisit only for Store distribution.
- **Plain installer (Inno Setup) without auto-update** — simpler but no silent updates.

## Consequences

- Velopack's default per-user install location (`%LOCALAPPDATA%`) is **incompatible with UIAccess**
  (which needs a signed binary in a secure location). So injecting into elevated windows stays
  unsupported unless the whole app is run elevated — documented limitation, acceptable for v1.
- Settings JSON needs a `schemaVersion` so auto-updates don't break existing user config.
- Code-signing certificate is a real cost/requirement before public release.

## Sources

- Velopack — https://github.com/velopack/velopack
- UIAccess requires signed binary in a secure location —
  https://en.wikipedia.org/wiki/User_Interface_Privilege_Isolation
