namespace AiTranslator.Core.Speech;

/// <summary>
/// Turns a stream of recognizer events into the text the compose box should show (ADR-0009).
/// <para>
/// It keeps three parts apart: the <b>prefix</b> the user had already typed before dictating, the
/// <b>committed</b> utterances finalized during this session, and the single <b>pending</b> partial
/// still being recognized. A partial always replaces the previous partial, so a re-rendered or revised
/// delta can never duplicate text or disturb what the user typed. Pure logic: no audio, no network.
/// </para>
/// </summary>
public sealed class DictationBuffer
{
    private string _prefix = string.Empty;
    private string _committed = string.Empty;
    private string _pending = string.Empty;

    /// <summary>Start a session, anchoring dictation after <paramref name="existingText"/>.</summary>
    public void Begin(string? existingText)
    {
        _prefix = existingText ?? string.Empty;
        _committed = string.Empty;
        _pending = string.Empty;
    }

    /// <summary>The partial transcript recognized so far; replaces any previous partial.</summary>
    public string ApplyPartial(string? partial)
    {
        _pending = Clean(partial);
        return Text;
    }

    /// <summary>A finished utterance. It supersedes the pending partial that led to it.</summary>
    public string ApplyFinal(string? final)
    {
        Commit(Clean(final));
        _pending = string.Empty;
        return Text;
    }

    /// <summary>Stop: keep whatever partial never got a final (e.g. the session ended early).</summary>
    public string Flush()
    {
        if (_pending.Length > 0)
        {
            Commit(_pending);
            _pending = string.Empty;
        }

        return Text;
    }

    /// <summary>The full text the box should display right now.</summary>
    public string Text
    {
        get
        {
            var body = Join(_committed, _pending);
            if (body.Length == 0)
            {
                return _prefix;
            }

            if (_prefix.Length == 0)
            {
                return body;
            }

            // Only add a separator when the user's own text does not already end with whitespace.
            return char.IsWhiteSpace(_prefix[^1]) ? _prefix + body : _prefix + " " + body;
        }
    }

    private void Commit(string text) => _committed = Join(_committed, text);

    private static string Join(string left, string right)
    {
        if (right.Length == 0)
        {
            return left;
        }

        return left.Length == 0 ? right : left + " " + right;
    }

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;
}
