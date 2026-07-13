using AiTranslator.Core.Models;

namespace AiTranslator.Core.Awareness;

/// <summary>
/// Resolves the rewrite style to use for a given app: the style last used in that app if one was
/// remembered, otherwise the global default (<see cref="AppSettings.RewriteStyle"/>). Each app keeps
/// its own independent memory, so Teams can stay Formal while the browser stays Friendly (ADR-0008).
/// Pure lookup, so it is unit-testable without a live desktop; matching uses the same
/// <see cref="ExeName"/> rules as the block list, so a key works with or without ".exe" and against
/// a full path.
/// </summary>
public static class AppStyles
{
    public static TranslationStyle For(string? exe, AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(exe))
        {
            foreach (var (key, style) in settings.AppStyles)
            {
                if (ExeName.Matches(key, exe))
                {
                    return style;
                }
            }
        }

        return settings.RewriteStyle;
    }

    /// <summary>
    /// Remember <paramref name="style"/> for <paramref name="exe"/>. Returns updated settings: the
    /// per-app entry when the app is known (keyed by its normalized file name), otherwise the global
    /// default, so a choice is never silently lost.
    /// </summary>
    public static AppSettings Remember(string? exe, TranslationStyle style, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(exe))
        {
            return settings with { RewriteStyle = style };
        }

        var key = ExeName.Normalize(exe);
        var updated = new Dictionary<string, TranslationStyle>(settings.AppStyles, StringComparer.OrdinalIgnoreCase);

        // Drop any existing entry that already matches this exe (e.g. a hand-written moniker) so the
        // app ends up with exactly one remembered style instead of a shadowed duplicate.
        foreach (var existing in updated.Keys.Where(k => ExeName.Matches(k, exe)).ToList())
        {
            updated.Remove(existing);
        }

        updated[key] = style;
        return settings with { AppStyles = updated };
    }
}
