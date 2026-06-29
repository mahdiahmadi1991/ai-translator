using AiTranslator.Core.Abstractions;
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
    private const int HotkeyId = 0xA11;        // app-unique id (valid range 0x0000–0xBFFF)
    private const uint WmHotkey = 0x0312;
    private readonly HWND _hwnd;
    private bool _registered;

    public HotkeyService(nint messageWindowHandle) => _hwnd = (HWND)messageWindowHandle;

    public event EventHandler? HotkeyPressed;

    public bool Register(string hotkey)
    {
        Unregister();
        var (mods, vk) = Parse(hotkey);
        _registered = PInvoke.RegisterHotKey(_hwnd, HotkeyId, mods | HOT_KEY_MODIFIERS.MOD_NOREPEAT, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            PInvoke.UnregisterHotKey(_hwnd, HotkeyId);
            _registered = false;
        }
    }

    /// <summary>Call from the host window's WndProc. Returns true if it handled WM_HOTKEY.</summary>
    public bool OnMessage(uint msg, nint wParam)
    {
        if (msg == WmHotkey && (int)wParam == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return false;
    }

    private static (HOT_KEY_MODIFIERS Mods, uint Vk) Parse(string hotkey)
    {
        HOT_KEY_MODIFIERS mods = 0;
        uint vk = 0;
        foreach (var raw in hotkey.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": mods |= HOT_KEY_MODIFIERS.MOD_CONTROL; break;
                case "ALT": mods |= HOT_KEY_MODIFIERS.MOD_ALT; break;
                case "SHIFT": mods |= HOT_KEY_MODIFIERS.MOD_SHIFT; break;
                case "WIN": mods |= HOT_KEY_MODIFIERS.MOD_WIN; break;
                default:
                    // Single letters/digits map to their ASCII virtual-key code (A-Z, 0-9).
                    if (raw.Length == 1)
                    {
                        vk = char.ToUpperInvariant(raw[0]);
                    }

                    break;
            }
        }

        return (mods, vk);
    }

    public void Dispose() => Unregister();
}
