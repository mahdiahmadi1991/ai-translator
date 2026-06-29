using System.Drawing;
using System.Runtime.InteropServices;

namespace AiTranslator.App.Windows;

/// <summary>
/// Physical-pixel window placement helpers (M2). Positioning in physical pixels via SetWindowPos
/// avoids WPF laying an off-screen window out at the wrong monitor's DPI on PerMonitorV2 / mixed-DPI
/// setups; <see cref="ScaleForPoint"/> reads the DPI of the monitor a field is actually on so DIP
/// offsets convert to the correct number of physical pixels.
/// </summary>
internal static partial class ScreenPlacement
{
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly nint HWND_TOPMOST = -1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    /// <summary>Move the window's top-left to a physical-pixel point (keeps WPF-computed size).</summary>
    public static void MoveTopLeft(nint hwnd, int xPx, int yPx, bool topmost, bool activate)
    {
        uint flags = SWP_NOSIZE | SWP_SHOWWINDOW;
        if (!activate)
        {
            flags |= SWP_NOACTIVATE;
        }

        if (!topmost)
        {
            flags |= SWP_NOZORDER;   // keep current Z order when not forcing topmost
        }

        SetWindowPos(hwnd, topmost ? HWND_TOPMOST : 0, xPx, yPx, 0, 0, flags);
    }

    /// <summary>DPI scale (1.0 == 96 DPI) of the monitor containing the physical-pixel point; 1.0 on failure.</summary>
    public static double ScaleForPoint(int xPx, int yPx)
    {
        nint monitor = MonitorFromPoint(new POINT { X = xPx, Y = yPx }, MONITOR_DEFAULTTONEAREST);
        if (monitor != 0 && GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX != 0)
        {
            return dpiX / 96.0;
        }

        return 1.0;
    }

    /// <summary>
    /// A physical-pixel top-left for a <paramref name="winWpx"/>×<paramref name="winHpx"/> window placed
    /// just below <paramref name="fieldPx"/>, flipped above the field if it would overflow the bottom,
    /// and clamped to the field monitor's work area so it is never off-screen.
    /// </summary>
    public static (int X, int Y) PlaceNearField(Rectangle fieldPx, int winWpx, int winHpx, int gapPx)
    {
        var work = WorkAreaForPoint(fieldPx.Left, fieldPx.Bottom);

        int x = Clamp(fieldPx.Left, work.Left, Math.Max(work.Left, work.Right - winWpx));
        int y = fieldPx.Bottom + gapPx;
        if (y + winHpx > work.Bottom)
        {
            y = fieldPx.Top - gapPx - winHpx;   // not enough room below — place above the field
        }

        y = Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - winHpx));
        return (x, y);
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

    private static Rectangle WorkAreaForPoint(int xPx, int yPx)
    {
        nint monitor = MonitorFromPoint(new POINT { X = xPx, Y = yPx }, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (monitor != 0 && GetMonitorInfo(monitor, ref mi))
        {
            return Rectangle.FromLTRB(mi.rcWork.left, mi.rcWork.top, mi.rcWork.right, mi.rcWork.bottom);
        }

        // Fallback: a generous virtual-desktop-ish box so clamping never hides the window.
        return Rectangle.FromLTRB(0, 0, 10000, 10000);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(POINT pt, uint dwFlags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}
