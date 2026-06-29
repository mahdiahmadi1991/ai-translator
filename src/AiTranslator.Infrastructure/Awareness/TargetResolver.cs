using System.Drawing;
using System.Windows.Automation;
using AiTranslator.Core.Abstractions;

namespace AiTranslator.Infrastructure.Awareness;

/// <summary>
/// Classifies and locates the focused element of a window via UI Automation (M2 Task 3, ADR-0003).
/// Uses the managed <see cref="AutomationElement"/> client, which ships in the WindowsDesktop
/// framework (no extra package — important for the offline build) and is understood by Win32, WPF,
/// and modern Chromium/WebView2/Electron (which expose a UIA provider once a UIA client is active).
/// </summary>
/// <remarks>
/// Scope note: this resolves the focused element's <see cref="AutomationElement.Current"/> bounding
/// rectangle (physical pixels). Caret-precise placement (TextPattern2 / GetGUIThreadInfo) and the
/// explicit IAccessible2 "wake the Chromium renderer" dance from the plan are deferred until manual
/// per-app testing (Task 5) shows they are actually needed — modern UIA covers the common cases.
/// </remarks>
public sealed class TargetResolver : ITargetResolver
{
    public FieldLocation? Resolve(nint windowHandle)
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null)
            {
                return null;
            }

            return new FieldLocation(IsEditable(focused), ReadRect(focused));
        }
        catch (Exception ex) when (ex is ElementNotAvailableException
                                      or ElementNotEnabledException
                                      or System.Runtime.InteropServices.COMException
                                      or InvalidOperationException
                                      or TimeoutException)
        {
            return null;   // element vanished / provider not ready — caller treats as "unresolved"
        }
    }

    /// <summary>An editable text target: an enabled, keyboard-focusable, non-password Edit/Document
    /// that exposes a writable ValuePattern or a TextPattern.</summary>
    private static bool IsEditable(AutomationElement element)
    {
        var info = element.Current;
        if (info.ControlType != ControlType.Edit && info.ControlType != ControlType.Document)
        {
            return false;
        }

        if (info.IsPassword || !info.IsEnabled || !info.IsKeyboardFocusable)
        {
            return false;
        }

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value))
        {
            return !((ValuePattern)value).Current.IsReadOnly;
        }

        // No ValuePattern: a TextPattern still means a typeable surface (e.g. rich editors).
        return element.TryGetCurrentPattern(TextPattern.Pattern, out _);
    }

    private static Rectangle? ReadRect(AutomationElement element)
    {
        var r = element.Current.BoundingRectangle;   // System.Windows.Rect, physical screen pixels
        if (r.IsEmpty || double.IsInfinity(r.Width) || double.IsInfinity(r.Height) || r.Width <= 0 || r.Height <= 0)
        {
            return null;
        }

        return new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
    }
}
