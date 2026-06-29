namespace AiTranslator.Core.Awareness;

/// <summary>
/// Shared executable-name matching: the single source of truth for how a user-written list entry
/// (allowlist, blocklist, per-app offsets) matches a live foreground exe. Case-insensitive, matches
/// on the filename component (so full paths work), and tolerates entries written without ".exe".
/// </summary>
public static class ExeName
{
    /// <summary>Filename component, lower-cased. Handles full paths of either slash flavor.</summary>
    public static string Normalize(string value)
    {
        var trimmed = value.Trim();
        var sep = trimmed.LastIndexOfAny(['\\', '/']);
        var fileName = sep >= 0 ? trimmed[(sep + 1)..] : trimmed;
        return fileName.ToLowerInvariant();
    }

    /// <summary>True when a list entry refers to the same executable as <paramref name="candidate"/>.</summary>
    public static bool Matches(string listEntry, string candidate)
    {
        var entry = Normalize(listEntry);
        var name = Normalize(candidate);
        return entry == name || StripExe(entry) == StripExe(name);
    }

    private static string StripExe(string lowerName)
        => lowerName.EndsWith(".exe", StringComparison.Ordinal) ? lowerName[..^4] : lowerName;
}
