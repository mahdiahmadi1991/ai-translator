namespace AiTranslator.Core.Abstractions;

/// <summary>An opaque handle to the field that was focused before our overlay opened.</summary>
public readonly record struct FocusTarget(nint WindowHandle);

/// <summary>Captures the foreign window that should receive injected text.</summary>
public interface IFocusTargetProvider
{
    /// <summary>Capture the currently-focused foreground window as the injection target.</summary>
    FocusTarget CaptureCurrent();
}
