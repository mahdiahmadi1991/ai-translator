# ADR-0001: Platform — .NET 10 + WPF (Windows)

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-28

## Context

The app must: run as a background Windows utility; place an always-on-top, non-activating overlay
beside arbitrary apps; read system-wide focus via accessibility APIs; and inject keystrokes. These
are deep Windows-desktop integrations. The user selected .NET 10 as the platform.

## Decision

Build on **.NET 10** targeting **`net10.0-windows`**, UI in **WPF (C#)**. Local `dotnet` SDK is
10.0.109. The same stack Grammarly itself uses (it is a .NET WPF app — confirmed by local
inspection), which validates the fit for this exact class of tool.

## Alternatives considered

- **WinUI 3** — modern Fluent, same UIA access, but heavier and less battle-tested for always-on-top
  non-activating tool windows. Rejected for added friction with no benefit here.
- **Electron / Tauri (web)** — familiar UI but weak/awkward native focus detection and keystroke
  injection; needs native add-ons for exactly the hardest parts. Rejected.
- **Win32/C++** — maximum control, much slower to build the UI and app logic. Rejected.

## Consequences

- WPF gives first-class access to Win32 interop (`SetWinEventHook`, `SendInput`, window styles),
  COM UI Automation, and the clipboard.
- **WPF is Windows-only:** the project can be edited from WSL/Linux but must be **built and run on
  Windows**. A Windows-only build cannot be verified from a Linux shell — do not claim otherwise.
- Per-monitor DPI awareness (`PerMonitorV2`) must be declared in the app manifest for correct
  overlay placement on multi-DPI setups.

## Sources

- Local inspection: Grammarly is `.NET Framework 4.7.2` WPF — `Grammarly.Desktop.exe.config`
  (`/mnt/c/Users/TZAB2/AppData/Local/Grammarly/DesktopIntegrations/`).
- Local `dotnet --version` → `10.0.109`.
- WPF on .NET — https://learn.microsoft.com/dotnet/desktop/wpf/
