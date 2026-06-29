# Offline build (network-restricted Windows)

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-29

The Windows host building this solution **cannot reliably reach `nuget.org`** (connections are reset
— typical of a filtered network). So every package is pre-downloaded once (from any networked machine,
e.g. WSL) into a folder beside the repo, and the build restores from it with **no network**.

## How it works

- **`.nuget-packages/`** (repo root, git-ignored) holds every restored package in NuGet's
  global-packages layout, on disk next to the solution.
- **`Directory.Build.props`** wires it up for every project:
  - `RestoreFallbackFolders` → `.nuget-packages` — NuGet serves packages from here and skips the
    network for anything found (which is everything).
  - `EnableWindowsTargeting=true` — lets `net10.0-windows` projects restore/evaluate from Linux too.
  - `NoWarn` includes `NU1507` — Visual Studio reports a machine-global `github` source it won't drop
    via `RestoreSources`; suppressed because all packages come from the offline folder anyway.
- On Windows, the WPF/WinForms **targeting pack comes from the installed .NET 10 SDK**, not the
  folder; only the regular NuGet packages are served offline.

## Building

1. Make sure **`.nuget-packages/` exists** next to the repo. Do **not** delete it.
2. Restore + build normally (`dotnet build src/AiTranslator.slnx`, or Rebuild in the IDE). If using an
   IDE, reopen the solution after pulling so it re-reads `Directory.Build.props`.

## Moving the repo to a native Windows path

`.nuget-packages/` is **git-ignored**, so `git clone`/copy will **not** bring it. If you relocate the
repo (e.g. from the WSL share to `C:\…`), **copy the `.nuget-packages/` folder manually** alongside it
(e.g. `robocopy`), or repopulate it (below). Without it, an offline host cannot restore.

## Repopulating (after adding or bumping a package, or on a fresh checkout)

Adding a package changes the version set, so refresh the folder **from a machine with network**
(e.g. WSL/Linux on the same box, which can reach nuget.org):

```bash
# from the repo root, on a networked machine
dotnet restore src/AiTranslator.slnx --packages .nuget-packages
```

If a brand-new transitive package is reported missing during a Windows build, this command fetches it.
Then copy/keep `.nuget-packages/` next to the repo the Windows host builds.

## Alternative

If the Windows host gets reliable `nuget.org` access (VPN/proxy, or VS → Tools → Options → NuGet →
configure an HTTP proxy), the offline folder is not needed and a normal restore works. The offline
setup is harmless in that case (the fallback folder is just unused).

Related: [windows-build-checklist.md](windows-build-checklist.md) · [build-and-run.md](build-and-run.md)
