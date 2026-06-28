# ADR-0005: API-key storage in Windows Credential Manager

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-28

## Context

This is a client desktop app, so the OpenAI key lives on the user's machine. It must be stored
securely per-user, be revocable, and never appear in the repo, source, or committed config. The
user's secrets standard requires repo secrets to live only under git-ignored `.secrets/`.

## Decision

- **Runtime (end user):** store the OpenAI key in **Windows Credential Manager** as a Generic
  credential (target `AI-Translator:OpenAI`) via the `Meziantou.Framework.Win32.CredentialManager`
  package. It is DPAPI-backed (CurrentUser), needs no ciphertext management, and is user-inspectable
  and revocable in the Credential Manager control panel. The user enters the key in a first-run flow.
- **Development:** a developer's own key may live in `.secrets/providers/openai.secrets.toml`
  (git-ignored), loaded only in `DEBUG`. The released build reads the key **exclusively** from
  Credential Manager. See [.secrets/README.md](../../../.secrets/README.md).
- **Never** bundle or hard-code a key; never log it (redact `***`).

## Alternatives considered

- **Raw DPAPI (`ProtectedData`, CurrentUser + entropy)** — equivalent security, zero extra
  dependency, but we own the ciphertext blob and storage. Acceptable fallback behind the same
  `SecretStore` abstraction.
- **`Windows.Security.Credentials.PasswordVault`** — aimed at packaged/UWP apps; needs WinRT interop
  from plain WPF. Rejected unless we later ship MSIX.

## Consequences

- The stored key is bound to the Windows user/machine (DPAPI CurrentUser) — it does **not** roam;
  copying `settings.json` to another PC won't carry the key. Provide a re-enter-key flow.
- Credential Manager/DPAPI protect against other users and at-rest theft, **not** same-user malware.
- `SecretStore` is an abstraction so the DPAPI fallback or a future backend-proxy can swap in.

## Sources

- Meziantou — store a password on Windows (Credential Manager recommended) —
  https://www.meziantou.net/how-to-store-a-password-on-windows.htm
- DPAPI `ProtectedData` — https://learn.microsoft.com/dotnet/api/system.security.cryptography.protecteddata
- User secrets are dev-only/unencrypted — https://learn.microsoft.com/aspnet/core/security/app-secrets
- Secrets standard — `~/.claude/standards/secrets-management.md`
