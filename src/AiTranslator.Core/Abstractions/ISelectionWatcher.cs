using System.Drawing;

namespace AiTranslator.Core.Abstractions;

/// <summary>
/// A run of text the user has selected somewhere on screen. <see cref="Text"/> is the selected text;
/// <see cref="Bounds"/> is its on-screen rectangle in physical pixels (for anchoring the translate
/// icon just below it), or null when only the text could be read (the icon falls back to the cursor);
/// <see cref="WindowHandle"/> is the foreground window; <see cref="ExeName"/> is its executable (per-app
/// tuning + block list); <see cref="IsEditable"/> is true when the selection lives in an editable field.
/// </summary>
public sealed record SelectedText(
    string Text, Rectangle? Bounds, nint WindowHandle, string ExeName, bool IsEditable);

/// <summary>
/// Watches for text selections anywhere on screen (the "read" mode: translate what you selected).
/// Implemented on Windows via a low-level mouse hook plus UI Automation's TextPattern; the selection
/// hotkey remains the guaranteed path (with a clipboard fallback), so the auto icon is best-effort.
/// </summary>
public interface ISelectionWatcher : IDisposable
{
    /// <summary>Begin watching. Idempotent.</summary>
    void Start();

    /// <summary>Stop watching and release the hook.</summary>
    void Stop();

    /// <summary>Read the current selection on demand (the hotkey path); may use a clipboard copy as a
    /// fallback when accessibility exposes no selection. Returns null when nothing usable is selected.</summary>
    SelectedText? CaptureCurrentSelection();

    /// <summary>Raised shortly after the user selects text in a non-blocked app.</summary>
    event EventHandler<SelectedText>? SelectionChanged;

    /// <summary>Raised when the selection is cleared (the translate icon should hide).</summary>
    event EventHandler? SelectionCleared;
}
