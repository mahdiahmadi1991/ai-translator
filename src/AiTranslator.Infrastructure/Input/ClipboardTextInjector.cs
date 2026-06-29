using System.Windows;                 // WPF Clipboard (STA) — Infrastructure sets UseWPF=true.
using AiTranslator.Core.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace AiTranslator.Infrastructure.Input;

/// <summary>
/// Replaces a target field's content via clipboard paste (ADR-0004): save clipboard → set text →
/// focus target → Ctrl+A → Ctrl+V → restore clipboard. M1 snapshots only text; full-format
/// snapshot/restore is M3. Call on the STA UI thread (clipboard requirement).
/// Verify CsWin32 <c>INPUT</c>/<c>KEYBDINPUT</c> field names when building on Windows.
/// </summary>
public sealed class ClipboardTextInjector : ITextInjector
{
    private const int SettleAfterActivationMs = 40;
    private const int ClipboardReadGraceMs = 250;
    private const int ClipboardRetryCount = 8;
    private const int ClipboardRetryDelayMs = 30;

    public async Task ReplaceTextAsync(FocusTarget target, string text, CancellationToken ct)
    {
        string? previous = SafeGetClipboardText();

        SetClipboardTextWithRetry(text);

        FocusTargetWindow((HWND)target.WindowHandle);
        await Task.Delay(SettleAfterActivationMs, ct);

        SendCtrl(VIRTUAL_KEY.VK_A);   // select all
        SendCtrl(VIRTUAL_KEY.VK_V);   // paste (replace)

        await Task.Delay(ClipboardReadGraceMs, ct);
        if (previous is not null)
        {
            SetClipboardTextWithRetry(previous);
        }
    }

    // unsafe: GetWindowThreadProcessId's lpdwProcessId parameter is a uint* (passed null via default).
    private static unsafe void FocusTargetWindow(HWND target)
    {
        HWND fg = PInvoke.GetForegroundWindow();
        uint fgThread = PInvoke.GetWindowThreadProcessId(fg, default);
        uint tgtThread = PInvoke.GetWindowThreadProcessId(target, default);
        bool attached = fgThread != tgtThread && PInvoke.AttachThreadInput(fgThread, tgtThread, true);
        try
        {
            PInvoke.SetForegroundWindow(target);
        }
        finally
        {
            if (attached)
            {
                PInvoke.AttachThreadInput(fgThread, tgtThread, false);
            }
        }
    }

    private static unsafe void SendCtrl(VIRTUAL_KEY key)
    {
        Span<INPUT> inputs =
        [
            Key(VIRTUAL_KEY.VK_CONTROL, down: true),
            Key(key, down: true),
            Key(key, down: false),
            Key(VIRTUAL_KEY.VK_CONTROL, down: false),
        ];
        PInvoke.SendInput(inputs, sizeof(INPUT));
    }

    private static INPUT Key(VIRTUAL_KEY vk, bool down) => new()
    {
        type = INPUT_TYPE.INPUT_KEYBOARD,
        Anonymous = new INPUT._Anonymous_e__Union
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                dwFlags = down ? 0 : KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP,
            },
        },
    };

    private static string? SafeGetClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch
        {
            return null;   // clipboard momentarily locked — treat as "nothing to restore"
        }
    }

    private static void SetClipboardTextWithRetry(string text)
    {
        for (int i = 0; i < ClipboardRetryCount; i++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch
            {
                Thread.Sleep(ClipboardRetryDelayMs);   // CLIPBRD_E_CANT_OPEN — another app holds it
            }
        }
    }
}
