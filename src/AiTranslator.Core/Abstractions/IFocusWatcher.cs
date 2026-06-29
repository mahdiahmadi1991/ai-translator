using System.Drawing;

namespace AiTranslator.Core.Abstractions;

/// <summary>
/// An editable field that just gained focus in an allowlisted app. <see cref="WindowHandle"/> is the
/// injection target; <see cref="ExeName"/> is the resolved foreground executable (for per-app tuning);
/// <see cref="FieldRect"/> is the field's screen rectangle in pixels for anchoring the badge, or null
/// when the resolver could not locate it (the badge falls back to a window-relative position).
/// <see cref="Rectangle"/> keeps Core free of WPF/UIA types while still carrying pixel bounds.
/// </summary>
public sealed record FocusedField(nint WindowHandle, string ExeName, Rectangle? FieldRect);

/// <summary>
/// Watches system-wide focus/foreground changes and raises an event when an editable field gains
/// focus inside an allowlisted app (M2). Implemented on Windows via <c>SetWinEventHook</c>; the
/// global hotkey remains the guaranteed path, so auto-appearance is best-effort per app (ADR-0003).
/// </summary>
public interface IFocusWatcher : IDisposable
{
    /// <summary>Begin watching. Idempotent; safe to call once at startup.</summary>
    void Start();

    /// <summary>Stop watching and release the hook.</summary>
    void Stop();

    /// <summary>Raised when an editable field in an allowlisted app gains focus.</summary>
    event EventHandler<FocusedField>? FieldFocused;

    /// <summary>Raised when focus leaves a watched field (badge should hide).</summary>
    event EventHandler? FieldUnfocused;
}
