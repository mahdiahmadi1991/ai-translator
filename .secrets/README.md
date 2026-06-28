# Secrets inventory

Documents **what** each secret is and **where** it is used. **Never** put actual values here, in
code, in committed config, or in logs. This whole folder is git-ignored except this README and any
`*.example.*` placeholder siblings.

> Two distinct stores — do not confuse them:
> - **Runtime (end user):** the OpenAI API key is stored in **Windows Credential Manager**, entered
>   by the user on first run. It never lives in this repo. See
>   [docs/architecture/decision-records/0005-secret-storage-credential-manager.md](../docs/architecture/decision-records/0005-secret-storage-credential-manager.md).
> - **Development:** a developer's own OpenAI key for running the app in `DEBUG` may live here, in
>   `providers/openai.secrets.toml` (git-ignored). Copy the `.example` file and fill it locally.

| Key | File | Purpose | Used by | Owner |
| --- | --- | --- | --- | --- |
| `OPENAI_API_KEY` | `providers/openai.secrets.toml` | Dev-time OpenAI API key for running the app locally in DEBUG without the first-run setup flow | `TranslationService` (dev only) | Mehdi |

## How to set up a dev key

```bash
cp .secrets/providers/openai.secrets.example.toml .secrets/providers/openai.secrets.toml
# then edit openai.secrets.toml and paste your real key (the real file stays git-ignored)
chmod 600 .secrets/providers/openai.secrets.toml
```

The released application does **not** read this file — it reads the user's key from Credential
Manager only.
