using AiTranslator.Core.Abstractions;
using Windows.Win32;

namespace AiTranslator.Infrastructure.Input;

/// <summary>Captures the current foreground window as the injection target (M1 — pre-badge).</summary>
public sealed class ForegroundFocusTargetProvider : IFocusTargetProvider
{
    // unsafe: HWND.Value is a void* — the (nint) cast needs an unsafe context.
    public unsafe FocusTarget CaptureCurrent() => new((nint)PInvoke.GetForegroundWindow().Value);
}
