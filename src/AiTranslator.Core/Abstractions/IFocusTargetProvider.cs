namespace AiTranslator.Core.Abstractions;

/// <summary>
/// The field that was focused before our overlay opened. <paramref name="ExeName"/> identifies the
/// app it belongs to, so per-app preferences (e.g. the remembered rewrite style) can be resolved for
/// it; it is null when the app could not be identified.
/// </summary>
public readonly record struct FocusTarget(nint WindowHandle, string? ExeName = null);

/// <summary>Captures the foreign window that should receive injected text.</summary>
public interface IFocusTargetProvider
{
    /// <summary>Capture the currently-focused foreground window as the injection target.</summary>
    FocusTarget CaptureCurrent();
}
