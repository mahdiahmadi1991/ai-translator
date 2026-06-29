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

        var name = NormalizeFileName(foregroundExe);

        if (blocklist.Any(b => Matches(b, name)))
        {
            return false;
        }

        // Empty allowlist means "auto-appear nowhere" — the manual hotkey still works everywhere.
        return allowlist.Any(a => Matches(a, name));
    }

    private static bool Matches(string listEntry, string normalizedName)
    {
        var entry = NormalizeFileName(listEntry);
        if (entry == normalizedName)
        {
            return true;
        }

        // Allow entries written without the ".exe" extension.
        return StripExe(entry) == StripExe(normalizedName);
    }

    private static string NormalizeFileName(string value)
    {
        // Take the filename component (handles full paths) and lower-case it. Use the last path
        // separator of either flavor so Windows paths normalize even on a non-Windows host.
        var trimmed = value.Trim();
        var sep = trimmed.LastIndexOfAny(['\\', '/']);
        var fileName = sep >= 0 ? trimmed[(sep + 1)..] : trimmed;
        return fileName.ToLowerInvariant();
    }

    private static string StripExe(string lowerName)
        => lowerName.EndsWith(".exe", StringComparison.Ordinal) ? lowerName[..^4] : lowerName;
}
