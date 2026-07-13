using AiTranslator.Core.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace AiTranslator.Infrastructure.Input;

/// <summary>
/// Captures the current foreground window as the injection target (the hotkey path). It also resolves
/// the owning executable, so per-app preferences (the remembered rewrite style, ADR-0008) apply on
/// this path too and not only on the badge path.
/// </summary>
public sealed class ForegroundFocusTargetProvider : IFocusTargetProvider
{
    // unsafe: HWND.Value is a void* — the (nint) cast needs an unsafe context.
    public unsafe FocusTarget CaptureCurrent()
    {
        var hwnd = PInvoke.GetForegroundWindow();
        return new FocusTarget((nint)hwnd.Value, ResolveExe(hwnd));
    }

    private static string? ResolveExe(HWND hwnd)
    {
        try
        {
            if (hwnd.IsNull)
            {
                return null;
            }

            _ = PInvoke.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0)
            {
                return null;
            }

            using var handle = PInvoke.OpenProcess_SafeHandle(
                PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, bInheritHandle: false, pid);
            if (handle.IsInvalid)
            {
                return null;
            }

            Span<char> buffer = new char[1024];
            uint size = (uint)buffer.Length;
            return PInvoke.QueryFullProcessImageName(handle, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, ref size)
                ? new string(buffer[..(int)size])
                : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;   // an unidentifiable app just falls back to the global default style
        }
    }
}
