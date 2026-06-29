using System.Drawing;

namespace AiTranslator.Core.Abstractions;

/// <summary>
/// The result of classifying + locating the focused element of a window: whether it is an editable
/// text field the user can type into, and its on-screen rectangle in pixels (null when the bounds
/// could not be read).
/// </summary>
public sealed record FieldLocation(bool IsEditable, Rectangle? Rect);

/// <summary>
/// Classifies the focused UI element of a window and locates it on screen (M2, ADR-0003). Implemented
/// on Windows via UI Automation (waking Chromium/WebView2 renderers through IAccessible2 when the UIA
/// tree is empty). Returns null when nothing could be resolved.
/// </summary>
public interface ITargetResolver
{
    /// <summary>Resolve the focused field of the given top-level window, or null if none/unknown.</summary>
    FieldLocation? Resolve(nint windowHandle);

    /// <summary>
    /// Try to set the previously-resolved field's text directly, <b>without moving focus</b> (UIA
    /// ValuePattern). Returns false when the field cannot be set this way (caller then falls back to
    /// clipboard paste). This is what keeps the floating box focused while the messenger updates.
    /// </summary>
    bool TrySetText(nint windowHandle, string text);
}
