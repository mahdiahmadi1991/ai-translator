using System.Drawing;

namespace AiTranslator.Core.Abstractions;

/// <summary>The outcome of classifying + locating the focused element of a window.</summary>
public enum FieldStatus
{
    /// <summary>The focused element is not an editable text field.</summary>
    NotEditable,

    /// <summary>An editable text field (its on-screen <see cref="FieldResolution.Rect"/> may still be
    /// null when the bounds could not be read).</summary>
    Editable,

    /// <summary>A Chromium/WebView2 renderer was just woken and its accessibility tree is still being
    /// built; the field cannot be read yet. The caller should retry shortly without tearing the badge
    /// down — see <see cref="ITargetResolver"/>.</summary>
    Pending,

    /// <summary>Focus could not be determined (no focused element, a transient UIA failure, or our own
    /// window). The caller should leave the current state untouched.</summary>
    Unknown,
}

/// <summary>
/// The result of classifying + locating the focused element of a window: a <see cref="FieldStatus"/>
/// and, when editable, its on-screen rectangle in physical pixels (null when bounds were unreadable).
/// </summary>
public sealed record FieldResolution(FieldStatus Status, Rectangle? Rect)
{
    public static readonly FieldResolution NotEditable = new(FieldStatus.NotEditable, null);
    public static readonly FieldResolution Pending = new(FieldStatus.Pending, null);
    public static readonly FieldResolution Unknown = new(FieldStatus.Unknown, null);

    public static FieldResolution Editable(Rectangle? rect) => new(FieldStatus.Editable, rect);
}

/// <summary>
/// Classifies the focused UI element of a window and locates it on screen (M2, ADR-0003). Implemented
/// on Windows via UI Automation, waking Chromium/WebView2 renderers through the MSAA accessibility
/// handshake when the UIA focus does not reach into the web content. Because a freshly woken renderer
/// builds its accessibility tree asynchronously, <see cref="Resolve"/> is non-blocking and may report
/// <see cref="FieldStatus.Pending"/>, signalling the caller to retry shortly rather than block.
/// </summary>
public interface ITargetResolver
{
    /// <summary>Resolve the focused field of the given top-level window. Never returns null — an
    /// undeterminable focus is reported as <see cref="FieldStatus.Unknown"/>.</summary>
    FieldResolution Resolve(nint windowHandle);
}
