namespace AiTranslator.Core.Models;

/// <summary>Where the recognizer is in its lifecycle; drives the mic button's appearance.</summary>
public enum SpeechState
{
    /// <summary>Not listening.</summary>
    Idle = 0,

    /// <summary>Opening the session (the mic is not capturing yet).</summary>
    Connecting,

    /// <summary>Capturing and streaming audio; text is arriving.</summary>
    Listening,

    /// <summary>Flushing the final transcript and closing the session.</summary>
    Stopping,
}

/// <summary>
/// One dictation session's settings. <paramref name="LanguageCode"/> is sent explicitly because naming
/// the language measurably improves accuracy (ADR-0009); it is the configured primary language.
/// </summary>
public sealed record SpeechOptions(string LanguageCode, string Model);
