# Offline build (network-restricted Windows)

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-29

The Windows host building this solution **cannot reliably reach `nuget.org`** (connections are reset
— typical of a filtered network). WSL/Linux on the same machine **can**. So packages are downloaded
once from WSL and the Windows build (Visual Studio over `\\wsl.localhost`) restores from them with
**no network**.

## How it works

- **`.nuget-packages/`** (repo root, git-ignored) holds every restored package in NuGet's
  global-packages layout. It lives on the shared WSL filesystem, so Visual Studio reads it over
  `\\wsl.localhost`.
- **`Directory.Build.props`** wires it up for every project:
  - `RestoreFallbackFolders` → `.nuget-packages` — NuGet serves packages from here and skips the
    network for anything found (which is everything).
  - `EnableWindowsTargeting=true` — lets `net10.0-windows` projects restore/evaluate from Linux too.
  - `NoWarn` includes `NU1507` — Visual Studio reports a machine-global `github` source it won't drop
    via `RestoreSources`; suppressed because all packages come from the offline folder anyway.
- On Windows, the WPF/WinForms **targeting pack comes from the installed .NET 10 SDK**, not the
  folder; only the regular NuGet packages are served offline.

## First build on Windows

1. The `.nuget-packages/` folder already exists on disk (populated from WSL). Do **not** delete it.
2. In Visual Studio, **close and reopen the solution** so it re-reads `Directory.Build.props`.
3. **Build → Rebuild Solution.** Restore runs offline from `.nuget-packages`.

## Repopulating (after adding or bumping a package)

Adding a package changes the version set, so the folder must be refreshed **from WSL/Linux** (which
has network):

```bash
# from the repo root, in WSL
dotnet restore src/AiTranslator.slnx --packages .nuget-packages
```

Then rebuild in Visual Studio. If a brand-new transitive package is missing, this command fetches it.

## Alternative

If the Windows host gets reliable `nuget.org` access (VPN/proxy, or VS → Tools → Options → NuGet →
configure an HTTP proxy), the offline folder is not needed and a normal restore works. The offline
setup is harmless in that case (the fallback folder is just unused).

Related: [windows-build-checklist.md](windows-build-checklist.md) · [build-and-run.md](build-and-run.md)
