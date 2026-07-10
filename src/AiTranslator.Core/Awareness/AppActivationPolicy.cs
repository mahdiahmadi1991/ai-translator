namespace AiTranslator.Core.Awareness;

/// <summary>
/// Decides whether the badge/overlay may auto-appear for a given foreground app (M2). The model is
/// <b>opt-out</b>: the badge appears anywhere an editable field is focused, EXCEPT apps the user has
/// blocklisted. (Whether the focused element is actually an editable field is decided separately by
/// the target resolver.) Pure logic so it is testable without a live desktop.
/// </summary>
public static class AppActivationPolicy
{
    /// <param name="foregroundExe">Process executable name or full path of the foreground window.</param>
    /// <param name="blocklist">Exe "monikers" (regex, see <see cref="ExeName"/>) that suppress the badge.</param>
    public static bool ShouldActivate(string? foregroundExe, IReadOnlyList<string> blocklist)
    {
        if (string.IsNullOrWhiteSpace(foregroundExe))
        {
            return false;
        }

        return !blocklist.Any(b => ExeName.Matches(b, foregroundExe));
    }
}
