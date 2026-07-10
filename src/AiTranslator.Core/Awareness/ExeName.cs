using System.Text.RegularExpressions;

namespace AiTranslator.Core.Awareness;

/// <summary>
/// Matches a user-written app entry (allowlist, blocklist, per-app offsets key) against a live
/// foreground executable. Following Grammarly's "Moniker" model, each entry is a <b>case-insensitive
/// regular expression</b> tested against the process file name — so a short moniker like
/// <c>whatsapp</c> matches <c>WhatsApp.exe</c> AND the packaged <c>WhatsApp.Root.exe</c>, and
/// <c>telegram</c> matches <c>Telegram.exe</c>. An entry that is not valid regex falls back to a
/// case-insensitive substring test, so plain names like <c>WhatsApp.exe</c> still work.
/// </summary>
public static class ExeName
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    /// <summary>Filename component, lower-cased. Handles full paths of either slash flavor.</summary>
    public static string Normalize(string value)
    {
        var trimmed = value.Trim();
        var sep = trimmed.LastIndexOfAny(['\\', '/']);
        var fileName = sep >= 0 ? trimmed[(sep + 1)..] : trimmed;
        return fileName.ToLowerInvariant();
    }

    /// <summary>True when a list entry (regex moniker) matches the executable of <paramref name="candidate"/>.</summary>
    public static bool Matches(string listEntry, string candidate)
    {
        if (string.IsNullOrWhiteSpace(listEntry) || string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var name = Normalize(candidate);
        var pattern = listEntry.Trim();

        try
        {
            return Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (RegexParseException)
        {
            return name.Contains(pattern.ToLowerInvariant(), StringComparison.Ordinal);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
