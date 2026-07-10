using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AiTranslator.App.Windows;

/// <summary>
/// A WPF window that never takes foreground activation (M2 Task 4): on <c>SourceInitialized</c> it ORs
/// <c>WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW</c> into its extended style and answers
/// <c>WM_MOUSEACTIVATE</c> with <c>MA_NOACTIVATE</c>, so clicking it never steals foreground from the
/// messenger. It stays clickable (no <c>WS_EX_TRANSPARENT</c>). Base class for the badge today and the
/// non-activating overlay in M3. Derived windows set <c>WindowStyle</c>/<c>AllowsTransparency</c>/
/// <c>Background</c> in their XAML, as the overlay does.
/// </summary>
public abstract partial class NonActivatingWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const nint MA_NOACTIVATE = 3;

    protected NonActivatingWindow()
    {
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        nint hwnd = new WindowInteropHelper(this).Handle;
        nint exStyle = GetWindowLongPtrCompat(hwnd, GWL_EXSTYLE);
        SetWindowLongPtrCompat(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private static nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return MA_NOACTIVATE;   // process the click but do not activate
        }

        return nint.Zero;
    }

    // 64/32-bit safe GetWindowLong/SetWindowLong. The *Ptr* entry points exist only on 64-bit;
    // the 32-bit branch is never resolved on a 64-bit process (P/Invoke binds lazily per call).
    private static nint GetWindowLongPtrCompat(nint hWnd, int index)
        => nint.Size == 8 ? GetWindowLongPtr(hWnd, index) : GetWindowLong(hWnd, index);

    private static void SetWindowLongPtrCompat(nint hWnd, int index, nint value)
    {
        if (nint.Size == 8)
        {
            SetWindowLongPtr(hWnd, index, value);
        }
        else
        {
            SetWindowLong(hWnd, index, (int)value);
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static partial int GetWindowLong(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static partial int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);
}
