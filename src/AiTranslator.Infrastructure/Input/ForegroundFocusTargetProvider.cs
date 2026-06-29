using AiTranslator.Core.Abstractions;
using Windows.Win32;

namespace AiTranslator.Infrastructure.Input;

/// <summary>Captures the current foreground window as the injection target (M1 — pre-badge).</summary>
public sealed class ForegroundFocusTargetProvider : IFocusTargetProvider
{
    public FocusTarget CaptureCurrent() => new((nint)PInvoke.GetForegroundWindow().Value);
}
