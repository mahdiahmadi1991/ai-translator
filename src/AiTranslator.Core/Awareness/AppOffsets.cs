using AiTranslator.Core.Models;

namespace AiTranslator.Core.Awareness;

/// <summary>
/// Resolves the badge offset for a foreground exe: the per-app calibration from settings if one
/// exists, otherwise <see cref="AppOffset.Default"/>. Pure lookup so it is unit-testable without a
/// live desktop; matching uses the same <see cref="ExeName"/> rules as the allowlist, so a key works
/// with or without ".exe" and against a full path.
/// </summary>
public static class AppOffsets
{
    public static AppOffset For(string? exe, AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(exe))
        {
            foreach (var (key, offset) in settings.AppOffsets)
            {
                if (ExeName.Matches(key, exe))
                {
                    return offset;
                }
            }
        }

        return AppOffset.Default;
    }
}
