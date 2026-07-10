using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace AiTranslator.Infrastructure.Input;

/// <summary>
/// Registers a system-wide hotkey via <c>RegisterHotKey</c>. The host window forwards WM_HOTKEY to
/// <see cref="OnMessage"/> (ADR-0003). Verify the CsWin32 <c>RegisterHotKey</c> overload + the
/// <c>HOT_KEY_MODIFIERS</c>/<c>VIRTUAL_KEY</c> enum names when building on Windows.
/// </summary>
public sealed class HotkeyService : IHotkeyService
{
    private const uint WmHotkey = 0x0312;
    private readonly HWND _hwnd;
    private readonly int _hotkeyId;            // app-unique id (valid range 0x0000–0xBFFF)
    private bool _registered;

    public HotkeyService(nint messageWindowHandle, int hotkeyId = 0xA11)
    {
        _hwnd = (HWND)messageWindowHandle;
        _hotkeyId = hotkeyId;
    }

    public event EventHandler? HotkeyPressed;

    public bool Register(string hotkey)
    {
        Unregister();
        if (!HotkeyCombination.TryParse(hotkey, out var combo))
        {
            return false;   // invalid combo — caller surfaces "pick another" (no exception)
        }

        // Core MOD_* values equal Win32 HOT_KEY_MODIFIERS; cast straight through.
        var modifiers = (HOT_KEY_MODIFIERS)combo.Modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        _registered = PInvoke.RegisterHotKey(_hwnd, _hotkeyId, modifiers, combo.VirtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            PInvoke.UnregisterHotKey(_hwnd, _hotkeyId);
            _registered = false;
        }
    }

    /// <summary>Call from the host window's WndProc. Returns true if it handled WM_HOTKEY.</summary>
    public bool OnMessage(uint msg, nint wParam)
    {
        if (msg == WmHotkey && (int)wParam == _hotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return false;
    }

    public void Dispose() => Unregister();
}
