namespace AiTranslator.App.Windows;

/// <summary>
/// Opt-in diagnostics for the compose flow (auto-correct, translate, inject). Off unless
/// <c>AITR_COMPOSE_LOG</c> is set, so the user's text is never written to disk in normal use:
/// set it to "1" for the default temp path, or to a full path.
/// </summary>
internal static class ComposeLog
{
    private static readonly string? Path = Resolve();

    private static string? Resolve()
    {
        var value = Environment.GetEnvironmentVariable("AITR_COMPOSE_LOG");
        if (string.IsNullOrWhiteSpace(value) || value == "0")
        {
            return null;
        }

        return value == "1"
            ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ai-translator-compose.log")
            : value;
    }

    /// <summary>A short, single-line excerpt of user text, safe to put in a diagnostic line.</summary>
    public static string Peek(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var flat = text.ReplaceLineEndings(" ");
        return flat.Length <= 60 ? flat : flat[..60] + "…";
    }

    public static void Write(string message)
    {
        if (Path is null)
        {
            return;
        }

        try
        {
            System.IO.File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // diagnostics must never break the feature they observe
        }
    }
}
