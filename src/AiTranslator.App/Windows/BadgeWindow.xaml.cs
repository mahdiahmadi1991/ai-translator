using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using AiTranslator.Core.Awareness;

namespace AiTranslator.App.Windows;

/// <summary>
/// The Grammarly-style badge (M2 Task 4): a small always-on-top, non-activating button that anchors
/// beside the focused field. Clicking it raises <see cref="Clicked"/> (the App opens the overlay
/// targeting that field). Positioned in physical pixels via SetWindowPos so multi-monitor / mixed-DPI
/// placement does not depend on WPF's DIP layout of an off-screen window.
/// </summary>
public partial class BadgeWindow : NonActivatingWindow
{
    private static readonly nint HWND_TOPMOST = -1;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    public BadgeWindow()
    {
        InitializeComponent();
        Root.MouseLeftButtonUp += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the user clicks the badge.</summary>
    public event EventHandler? Clicked;

    /// <summary>Show (or move) the badge anchored to <paramref name="fieldRect"/> (physical pixels).</summary>
    public void ShowAt(Rectangle fieldRect, AppOffset offset)
    {
        if (!IsVisible)
        {
            Show();
        }

        var (x, y) = AnchorPixels(fieldRect, offset);
        nint hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private static (int X, int Y) AnchorPixels(Rectangle field, AppOffset offset)
    {
        // Pick the field corner to anchor from (AppOffset.Corner), then nudge by dx/dy. Exact corner
        // semantics and offset units (px vs DIP) are settled by per-app calibration (Task 6) and the
        // manual multi-monitor/DPI pass (Task 4 verify); this is a sensible starting placement.
        int x = offset.Corner is 1 or 2 ? field.Right : field.Left;   // 1=BR, 2=TR are right-anchored
        int y = offset.Corner is 1 or 3 ? field.Bottom : field.Top;   // 1=BR, 3=BL are bottom-anchored
        return (x + offset.Dx, y + offset.Dy);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
