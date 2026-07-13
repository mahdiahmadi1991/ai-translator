using System.Diagnostics;
using System.Windows;   // WPF Clipboard (STA) — Infrastructure sets UseWPF=true.

namespace AiTranslator.Infrastructure.Input;

/// <summary>
/// Clipboard access that never runs on the caller's thread.
/// <para>
/// The Win32 clipboard is one global lock that any process may hold open, and WPF's <see cref="Clipboard"/>
/// hides that behind an internal retry loop built on <c>Thread.Sleep</c>. Calling it from the WPF UI
/// thread therefore freezes the whole app for as long as the board is contended — measured at over 20
/// seconds on an otherwise idle machine, which is what made the compose box feel like it had hung. Every
/// operation here runs on its own short-lived STA thread (the apartment the clipboard requires) and is
/// awaited, so the UI thread keeps painting and accepting input throughout.
/// </para>
/// </summary>
internal static class StaClipboard
{
    /// <summary>How long to keep trying to take the clipboard before giving up. Generous, because it
    /// costs the UI nothing now: the caller is awaiting, not blocking.</summary>
    private static readonly TimeSpan SetBudget = TimeSpan.FromSeconds(5);

    private const int SetRetryDelayMs = 50;

    public static Task<string?> GetTextAsync() => RunStaAsync(GetText);

    /// <summary>Put <paramref name="text"/> on the clipboard and confirm it landed. False means the
    /// board could not be taken — the caller must not then paste, or it would paste the old content.</summary>
    public static Task<bool> TrySetTextAsync(string text) => RunStaAsync(() => TrySetText(text));

    /// <summary>Empty the clipboard. Used to undo our own write when the user had nothing on it: the
    /// alternative is leaving their private translation on the global board indefinitely.</summary>
    public static Task<bool> TryClearAsync() => RunStaAsync(() =>
    {
        try
        {
            Clipboard.Clear();
            return true;
        }
        catch
        {
            return false;
        }
    });

    /// <summary>Synchronous read, for callers already on an STA thread of their own.</summary>
    public static string? GetText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch
        {
            return null;   // momentarily locked — treat as "nothing there"
        }
    }

    private static bool TrySetText(string text)
    {
        var deadline = Stopwatch.StartNew();
        do
        {
            try
            {
                Clipboard.SetText(text);

                // Read it back: SetText can report success while another owner still holds the board.
                if (GetText() == text)
                {
                    return true;
                }
            }
            catch
            {
                // CLIPBRD_E_CANT_OPEN — another app holds it; back off and try again.
            }

            Thread.Sleep(SetRetryDelayMs);
        }
        while (deadline.Elapsed < SetBudget);

        return false;
    }

    /// <summary>Run <paramref name="work"/> on a dedicated STA thread and await its result.</summary>
    public static Task<T> RunStaAsync<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "sta-clipboard",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
