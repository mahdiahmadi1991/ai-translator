namespace AiTranslator.Core.Input;

/// <summary>
/// A parsed global-hotkey combination. Modifier flags and the virtual-key code match the Win32
/// values so the Windows hotkey adapter can pass them straight to <c>RegisterHotKey</c>. Pure logic,
/// fully unit-tested (the parsing previously lived untested inside the Windows adapter).
/// </summary>
public readonly record struct HotkeyCombination(uint Modifiers, uint VirtualKey)
{
    // Win32 fsModifiers values.
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    /// <summary>Parses e.g. "Ctrl+Alt+T". Returns false for empty, modifier-only, or unknown keys.</summary>
    public static bool TryParse(string? text, out HotkeyCombination combo)
    {
        combo = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        uint modifiers = 0;
        uint? virtualKey = null;

        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.ToUpperInvariant();
            switch (token)
            {
                case "CTRL" or "CONTROL": modifiers |= ModControl; continue;
                case "ALT": modifiers |= ModAlt; continue;
                case "SHIFT": modifiers |= ModShift; continue;
                case "WIN": modifiers |= ModWin; continue;
            }

            // Anything else must be the (single) key.
            if (virtualKey is not null || !TryResolveKey(token, out var vk))
            {
                return false;
            }

            virtualKey = vk;
        }

        if (virtualKey is null)
        {
            return false;
        }

        combo = new HotkeyCombination(modifiers, virtualKey.Value);
        return true;
    }

    private static bool TryResolveKey(string token, out uint vk)
    {
        vk = 0;

        // A–Z and 0–9 map to their ASCII code (which equals the Win32 VK code).
        if (token.Length == 1)
        {
            char c = token[0];
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                vk = c;
                return true;
            }

            return false;
        }

        if (token == "SPACE")
        {
            vk = 0x20;   // VK_SPACE
            return true;
        }

        // Function keys F1–F24 → VK_F1 (0x70) .. VK_F24 (0x87).
        if (token.Length >= 2 && token[0] == 'F' && int.TryParse(token[1..], out int n) && n is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + n - 1);
            return true;
        }

        return false;
    }
}
