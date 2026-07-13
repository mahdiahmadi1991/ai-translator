using System.Windows;                 // WPF Clipboard (STA) — Infrastructure sets UseWPF=true.
using AiTranslator.Core.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace AiTranslator.Infrastructure.Input;

/// <summary>
/// Appends text to a target field via clipboard paste (ADR-0004): save clipboard, set our text, focus
/// the target, Ctrl+End, Ctrl+V, then put the user's clipboard back. Call on the STA UI thread
/// (clipboard requirement).
/// <para>
/// <b>Two races make the naive version paste the wrong thing, and both are handled here.</b> First, the
/// keystrokes must not be sent until the target is really in the foreground, or they land in whatever
/// window still has focus. Second, and the one that actually bit us: the target reads the clipboard
/// when <i>it</i> gets round to processing the paste, which can be far later than <c>SendInput</c>
/// returns. Restoring the previous clipboard on a short fixed timer therefore raced the target, and it
/// pasted the user's OLD clipboard content instead of the translation. The restore now waits generously,
/// runs off the caller's path so the box still dismisses at once, and is skipped if anything else has
/// taken the clipboard in the meantime.
/// </para>
/// </summary>
public sealed class ClipboardTextInjector : ITextInjector
{
    private const int ForegroundPollMs = 20;
    private const int ForegroundTimeoutMs = 1500;
    private const int SettleAfterActivationMs = 60;

    /// <summary>How long the target may take to actually process the paste and read the clipboard.
    /// Generous on purpose: a chat app under load is far slower than a text editor, and restoring too
    /// early is what made it paste the wrong text.</summary>
    private const int ClipboardRestoreDelayMs = 2500;

    private const int ClipboardRetryCount = 20;
    private const int ClipboardRetryDelayMs = 50;

    public async Task AppendTextAsync(FocusTarget target, string text, CancellationToken ct)
    {
        string? previous = SafeGetClipboardText();

        // NEVER paste on trust. Another app can hold the clipboard open (a clipboard manager, the
        // target itself), in which case the set silently fails and the OLD content is still there.
        // Sending Ctrl+V then injects that old content, which is exactly how a user's own source text
        // ended up in the chat instead of the translation. Verify, or refuse to paste.
        if (!TrySetClipboardText(text))
        {
            throw new TextInjectionException("The clipboard is held by another app.");
        }

        // Send the keystrokes only once the target really has the foreground, so they cannot land in
        // the window we are leaving.
        await FocusTargetWindowAsync((HWND)target.WindowHandle, ct);
        await Task.Delay(SettleAfterActivationMs, ct);

        SendCtrl(VIRTUAL_KEY.VK_END);   // caret to end — append, do NOT select-all/replace
        SendCtrl(VIRTUAL_KEY.VK_V);     // paste

        if (previous is not null)
        {
            // Deliberately not awaited: the caller can dismiss the box immediately, and the clipboard
            // goes back once the target has certainly consumed it.
            _ = RestorePreviousClipboardAsync(previous, ours: text);
        }
    }

    /// <summary>Put the user's clipboard back, but only once the target has had time to paste, and only
    /// if our text is still on the clipboard (otherwise something else owns it and we must not clobber it).</summary>
    private static async Task RestorePreviousClipboardAsync(string previous, string ours)
    {
        await Task.Delay(ClipboardRestoreDelayMs);

        if (SafeGetClipboardText() == ours)
        {
            SetClipboardTextWithRetry(previous);
        }
    }

    /// <summary>
    /// Bring the target to the foreground and wait until Windows agrees that it is there. If it never
    /// gets there we must NOT paste: the keystrokes would land in whatever window still has focus
    /// (including our own box), and the translation would vanish.
    /// </summary>
    private static async Task FocusTargetWindowAsync(HWND target, CancellationToken ct)
    {
        FocusTargetWindow(target);

        for (int waited = 0; waited < ForegroundTimeoutMs; waited += ForegroundPollMs)
        {
            if (PInvoke.GetForegroundWindow() == target)
            {
                return;
            }

            await Task.Delay(ForegroundPollMs, ct);
            FocusTargetWindow(target);   // keep asking; the foreground can be handed over late
        }

        throw new TextInjectionException("The target window would not come to the foreground.");
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

    /// <summary>
    /// Put <paramref name="text"/> on the clipboard and confirm it actually landed. Returns false when
    /// the clipboard could not be taken: a swallowed failure here is what makes a paste insert stale
    /// content, so the caller must be able to see it.
    /// </summary>
    private static bool TrySetClipboardText(string text)
    {
        for (int attempt = 0; attempt < ClipboardRetryCount; attempt++)
        {
            try
            {
                Clipboard.SetText(text);

                // Read it back: SetText can report success while another owner still holds the board.
                if (SafeGetClipboardText() == text)
                {
                    return true;
                }
            }
            catch
            {
                // CLIPBRD_E_CANT_OPEN — another app holds it; back off and try again.
            }

            Thread.Sleep(ClipboardRetryDelayMs);
        }

        return false;
    }

    private static void SetClipboardTextWithRetry(string text) => TrySetClipboardText(text);
}
