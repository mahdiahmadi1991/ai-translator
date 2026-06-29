namespace AiTranslator.Core.Awareness;

/// <summary>
/// Decides whether the badge/overlay should auto-appear for a given foreground app (M2). Pure logic
/// so it is testable without a live desktop; the Windows focus watcher feeds it the foreground exe.
/// </summary>
public static class AppActivationPolicy
{
    /// <param name="foregroundExe">Process executable name or full path of the foreground window.</param>
    /// <param name="allowlist">Exe names where auto-activation is allowed (with or without ".exe").</param>
    /// <param name="blocklist">Exe names that always suppress activation; takes precedence.</param>
    public static bool ShouldActivate(
        string? foregroundExe, IReadOnlyList<string> allowlist, IReadOnlyList<string> blocklist)
    {
        if (string.IsNullOrWhiteSpace(foregroundExe))
        {
            return false;
        }

        if (blocklist.Any(b => ExeName.Matches(b, foregroundExe)))
        {
            return false;
        }

        // Empty allowlist means "auto-appear nowhere" — the manual hotkey still works everywhere.
        return allowlist.Any(a => ExeName.Matches(a, foregroundExe));
    }
}
