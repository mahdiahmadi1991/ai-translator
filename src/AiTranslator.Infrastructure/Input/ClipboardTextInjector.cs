using AiTranslator.Core.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace AiTranslator.Infrastructure.Input;

/// <summary>
/// Appends text to a target field via clipboard paste (ADR-0004): save clipboard, set our text, focus
/// the target, Ctrl+End, Ctrl+V, then put the user's clipboard back.
/// <para>
/// <b>Four hazards, all handled here.</b> The keystrokes must not be sent until the target really has
/// the foreground, or they land in whatever window still has focus. The target reads the clipboard when
/// <i>it</i> processes the paste, which can be far later than <c>SendInput</c> returns, so the restore
/// waits generously and is skipped if anything else has taken the board meanwhile. Once our text is on
/// the board, <b>every</b> exit path must put the user's clipboard back, including the failure paths:
/// otherwise a private translation is left on the global clipboard and whatever the user had copied is
/// destroyed. And every clipboard call goes through <see cref="StaClipboard"/> rather than running
/// inline: done on the WPF UI thread it blocks that thread for as long as the board is contended, which
/// froze the compose box for seconds.
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

    public async Task AppendTextAsync(FocusTarget target, string text, CancellationToken ct)
    {
        string? previous = await StaClipboard.GetTextAsync().WaitAsync(ct).ConfigureAwait(true);

        // NEVER paste on trust. Another app can hold the clipboard open (a clipboard manager, the
        // target itself), in which case the set silently fails and the OLD content is still there.
        // Sending Ctrl+V then injects that old content, which is exactly how a user's own source text
        // ended up in the chat instead of the translation. Verify, or refuse to paste. Nothing has been
        // written when this throws, so there is nothing to undo.
        if (!await StaClipboard.TrySetTextAsync(text).WaitAsync(ct).ConfigureAwait(true))
        {
            throw new TextInjectionException("The clipboard is held by another app.");
        }

        // From here the board holds the user's translation, so EVERY exit has to put it back. Failing to
        // do that would leave a private message on the global clipboard for any process to read, and
        // would destroy whatever the user had copied (which may well be a password).
        try
        {
            // Send the keystrokes only once the target really has the foreground, so they cannot land in
            // the window we are leaving.
            await FocusTargetWindowAsync((HWND)target.WindowHandle, ct).ConfigureAwait(true);
            await Task.Delay(SettleAfterActivationMs, ct).ConfigureAwait(true);

            SendCtrl(VIRTUAL_KEY.VK_END);   // caret to end — append, do NOT select-all/replace
            SendCtrl(VIRTUAL_KEY.VK_V);     // paste
        }
        catch
        {
            // Nothing was pasted, so we do not owe the target any settling time: undo our write at once,
            // before letting the failure surface.
            await RestoreClipboardAsync(previous, ours: text).ConfigureAwait(true);
            throw;
        }

        // Deliberately not awaited, and deliberately never resumed on the UI thread: the caller can
        // dismiss the box at once, and the board goes back once the target has certainly consumed it.
        _ = DelayedRestoreAsync(previous, ours: text);
    }

    private static async Task DelayedRestoreAsync(string? previous, string ours)
    {
        await Task.Delay(ClipboardRestoreDelayMs).ConfigureAwait(false);
        await RestoreClipboardAsync(previous, ours).ConfigureAwait(false);
    }

    /// <summary>
    /// Undo our write: put <paramref name="previous"/> back, or empty the board if the user had nothing
    /// on it. Skipped entirely if our text is no longer there, because then something else owns the
    /// clipboard and clobbering it would be the very bug we are avoiding.
    /// </summary>
    private static async Task RestoreClipboardAsync(string? previous, string ours)
    {
        if (await StaClipboard.GetTextAsync().ConfigureAwait(false) != ours)
        {
            return;
        }

        if (previous is not null)
        {
            await StaClipboard.TrySetTextAsync(previous).ConfigureAwait(false);
        }
        else
        {
            await StaClipboard.TryClearAsync().ConfigureAwait(false);
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

            await Task.Delay(ForegroundPollMs, ct).ConfigureAwait(true);
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
}
