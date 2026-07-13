using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AiTranslator.App.Resources;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Awareness;
using AiTranslator.Core.Models;
using AiTranslator.Core.Speech;
using AiTranslator.Core.Translation;
using Wpf.Ui.Controls;

namespace AiTranslator.App.Windows;

/// <summary>
/// The floating input box. The user types source text and presses <b>Translate</b> (or Ctrl+Enter);
/// the translation replaces the target field's content (spec §2). Translation is explicit — never on a
/// debounce — so typing is never interrupted. The box is a reusable singleton: it <b>hides</b> (not
/// closes) when focus leaves, preserving the draft, and reappears with the same text. <c>Esc</c>
/// clears the draft and hides.
/// </summary>
public partial class OverlayInputWindow : Window
{
    private readonly IFocusTargetProvider _focus;
    private readonly ITranslationService _translator;
    private readonly ITextInjector _injector;
    private readonly ISpeechRecognizer _speech;
    private readonly ITextCorrector _corrector;
    private readonly Func<AppSettings> _settingsProvider;
    private readonly DispatcherTimer _visibilityWatch;
    private readonly DictationBuffer _dictation = new();

    private FocusTarget _target;
    private CancellationTokenSource? _inflight;
    private bool _busy;
    private bool _loadingStyle;   // suppress StyleChanged while we set the combo programmatically
    private SpeechState _speechState = SpeechState.Idle;
    private string _lastCorrected = string.Empty;   // skip re-correcting text we already proof-read
    private Task? _correcting;                      // the proof-read in flight, so we never run two
    private bool _stopping;                         // a stop is already unwinding — absorb further ones
    private bool _statusIsError;                    // an error on show must not be wiped by a progress note

    public OverlayInputWindow(
        IFocusTargetProvider focus, ITranslationService translator, ITextInjector injector,
        ISpeechRecognizer speech, ITextCorrector corrector, Func<AppSettings> settingsProvider)
    {
        InitializeComponent();
        _focus = focus;
        _translator = translator;
        _injector = injector;
        _speech = speech;
        _corrector = corrector;
        _settingsProvider = settingsProvider;

        StyleCombo.ItemsSource = RewriteStyleCatalog.All;
        StyleCombo.SelectionChanged += OnStyleChanged;

        // The recognizer raises on background threads — marshal everything to the UI thread.
        _speech.PartialTranscript += (_, text) => Dispatcher.InvokeAsync(() => ShowDictated(_dictation.ApplyPartial(text)));
        _speech.FinalTranscript += (_, text) => Dispatcher.InvokeAsync(() => ShowDictated(_dictation.ApplyFinal(text)));
        _speech.StateChanged += (_, state) => Dispatcher.InvokeAsync(() => ApplySpeechState(state));
        _speech.Failed += (_, ex) => Dispatcher.InvokeAsync(() => ShowStatus($"{UiStrings.DictationFailed} {ex.Message}"));

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (_speechState != SpeechState.Idle)
                {
                    e.Handled = true;
                    _ = StopDictationAsync();   // Esc while listening stops dictation, it does not discard
                    return;
                }

                Input.Clear();   // explicit discard
                Hide();
            }
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                e.Handled = true;
                _ = TranslateAsync();
            }
            // plain Enter is intentionally NOT handled: AcceptsReturn makes it insert a newline.
        };

        Input.TextChanged += (_, _) => UpdateHeader();   // auto direction + source→target label

        // Auto-hide: while the box is visible, poll the foreground window; if it is neither this box
        // nor the target field, hide (preserving the draft). Polling is more reliable than
        // Window.Deactivated, which never fires if the box never actually took activation.
        _visibilityWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _visibilityWatch.Tick += OnVisibilityWatchTick;
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue)
            {
                _visibilityWatch.Start();
            }
            else
            {
                _visibilityWatch.Stop();
                if (_speechState != SpeechState.Idle)
                {
                    _ = StopDictationAsync();   // a hidden box must never keep the microphone open
                }
            }
        };
    }

    private void OnVisibilityWatchTick(object? sender, EventArgs e)
    {
        nint foreground = ScreenPlacement.ForegroundWindow();
        nint self = new WindowInteropHelper(this).Handle;
        if (foreground != self && foreground != _target.WindowHandle)
        {
            Hide();   // focus is neither the box nor the target → get out of the way (draft preserved)
        }
    }

    /// <summary>Raised when the user clicks the header settings gear.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Raised when the user picks a different rewrite style, with the exe of the app being
    /// written into, so the host can remember the choice for that app alone (ADR-0008).</summary>
    public event Action<string?, TranslationStyle>? StyleChanged;

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingStyle && StyleCombo.SelectedItem is RewriteStyleOption option)
        {
            StyleChanged?.Invoke(_target.ExeName, option.Style);   // remembered per app by the host
        }
    }

    // ---- Dictation (ADR-0009) -----------------------------------------------------------------

    private void OnMicClick(object sender, RoutedEventArgs e)
    {
        if (_speechState == SpeechState.Idle)
        {
            _ = StartDictationAsync();
        }
        else
        {
            _ = StopDictationAsync();
        }
    }

    /// <summary>
    /// Stopping is not instant: the recognizer drains the audio it has and then waits for the final
    /// transcript, which can take seconds. Re-entering during that window would flush the buffer early
    /// and proof-read twice, so a stop in progress simply absorbs further stop requests (the mic button
    /// is also disabled, and Esc and the auto-hide path come through here too).
    /// </summary>
    private async Task StopDictationAsync()
    {
        if (_stopping)
        {
            return;
        }

        _stopping = true;
        try
        {
            await StopDictationCoreAsync();
        }
        finally
        {
            _stopping = false;
        }
    }

    private async Task StartDictationAsync()
    {
        if (_busy)
        {
            return;   // a translation is in flight; don't fight over the box
        }

        var settings = _settingsProvider();
        ClearStatus();

        // Anchor dictation after whatever the user already typed, so speech is appended, not replacing.
        _dictation.Begin(Input.Text);

        try
        {
            await _speech.StartAsync(new SpeechOptions(settings.LanguagePair.Primary, settings.SpeechModel));
        }
        catch (InvalidOperationException ex)
        {
            // No key, or no usable microphone — both are things the user can act on.
            ShowStatus(ex.Message.Contains("microphone", StringComparison.OrdinalIgnoreCase)
                ? UiStrings.DictationNoMicrophone
                : UiStrings.OverlayNoApiKey);
        }
        catch (Exception ex)
        {
            ShowStatus($"{UiStrings.DictationFailed} {ex.Message}");
        }
    }

    private async Task StopDictationCoreAsync()
    {
        try
        {
            await _speech.StopAsync();
        }
        catch (Exception ex)
        {
            ShowStatus($"{UiStrings.DictationFailed} {ex.Message}");
        }
        finally
        {
            ShowDictated(_dictation.Flush());   // keep a partial that never got a final
        }

        // Speech-to-text mishears words and writes English terms in the local script, so proof-read
        // what it produced (ADR-0010). Nothing follows this to report a failure, so surface one here.
        _correcting = AutoCorrectAsync(surfaceErrors: true);
        await _correcting;
    }

    // ---- Auto-correct (ADR-0010) --------------------------------------------------------------

    /// <summary>
    /// Proof-read the box: fix typos, repair words dictation misheard, and restore transliterated
    /// English terms. Best-effort by design: if it fails, the user's text is kept exactly as it was.
    /// <para>
    /// The box stays <b>editable</b> throughout. A model call takes seconds, and the first thing anyone
    /// does after dictating is reach for the word the recognizer got wrong — so freezing the box until
    /// the proof-read returns reads as "the app is stuck". If the text changes while we are away, the
    /// user has taken over: their version wins and the correction is dropped rather than overwriting it.
    /// </para>
    /// </summary>
    private async Task AutoCorrectAsync(bool surfaceErrors)
    {
        var settings = _settingsProvider();
        var text = Input.Text;

        if (!settings.AutoCorrect || string.IsNullOrWhiteSpace(text) || text == _lastCorrected)
        {
            return;
        }

        ShowProgress(UiStrings.Correcting);
        try
        {
            var corrected = await _corrector.CorrectAsync(text, settings.Model);
            ComposeLog.Write($"autocorrect: in='{ComposeLog.Peek(text)}' out='{ComposeLog.Peek(corrected)}'");

            if (Input.Text != text)
            {
                ComposeLog.Write("autocorrect: DISCARDED — the user edited the text while we proof-read");
                ClearStatus();
                return;
            }

            if (!string.IsNullOrWhiteSpace(corrected) && corrected != text)
            {
                Input.Text = corrected;
                Input.CaretIndex = Input.Text.Length;
                Input.ScrollToEnd();
            }

            _lastCorrected = Input.Text;
            ClearStatus();
        }
        catch (Exception ex)
        {
            // A correction failure must never cost the user their text, and must never block the
            // translation that may follow. On the translate path the translation call reports the same
            // fault (no key, no network) a moment later, so it is not duplicated here.
            ClearStatus();
            if (surfaceErrors)
            {
                ShowStatus($"{UiStrings.OverlayError} {ex.Message}");
            }
        }
    }

    /// <summary>Render the live transcript, keeping the caret at the end so the box scrolls with speech.</summary>
    private void ShowDictated(string text)
    {
        Input.Text = text;
        Input.CaretIndex = Input.Text.Length;
        Input.ScrollToEnd();
    }

    private void ApplySpeechState(SpeechState state)
    {
        _speechState = state;
        bool active = state is SpeechState.Connecting or SpeechState.Listening or SpeechState.Stopping;

        // While the recognizer is re-rendering the text, typing would be clobbered — so hold the box.
        Input.IsReadOnly = active;
        TranslateButton.IsEnabled = !active && !_busy;
        StyleCombo.IsEnabled = !active;

        // Stopping can take seconds (drain the audio, wait for the final transcript). The mic must go
        // dead for that window, or a second click re-enters the stop and proof-reads twice.
        MicButton.IsEnabled = state is not SpeechState.Stopping;
        MicIcon.Symbol = active ? SymbolRegular.RecordStop24 : SymbolRegular.Mic24;
        MicButton.Appearance = active ? ControlAppearance.Danger : ControlAppearance.Secondary;
        MicButton.ToolTip = active ? UiStrings.DictationStop : UiStrings.DictationStart;

        switch (state)
        {
            case SpeechState.Connecting:
                ShowProgress(UiStrings.DictationConnecting);
                break;
            case SpeechState.Listening:
                ShowProgress(UiStrings.DictationListening);
                break;
            case SpeechState.Stopping:
                // Without this the box sat locked with the status still reading "Listening…", so the
                // user had no sign their stop had registered.
                ShowProgress(UiStrings.DictationFinishing);
                break;
            case SpeechState.Idle:
                if (!_statusIsError)
                {
                    ClearStatus();   // never wipe an error the user has not had a chance to read
                }

                Input.Focus();
                Input.CaretIndex = Input.Text.Length;
                break;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();   // close = hide; the draft is kept

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Drag the box by its header — but not when the press lands on a header button (gear/close).
        if (e.ChangedButton != MouseButton.Left || IsWithinButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        try { DragMove(); } catch { /* DragMove throws if the button was already released — ignore */ }
    }

    private static bool IsWithinButton(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is System.Windows.Controls.Primitives.ButtonBase)
            {
                return true;
            }

            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : null;
        }

        return false;
    }

    /// <summary>Hotkey path: capture the foreground window as the target and show bottom-centre.</summary>
    public void ShowFor() => ShowForCore(_focus.CaptureCurrent(), anchor: null);

    /// <summary>Badge path: target the field the watcher resolved, anchored near its rectangle.</summary>
    public void ShowFor(FocusTarget target, System.Drawing.Rectangle? anchor) => ShowForCore(target, anchor);

    private void ShowForCore(FocusTarget target, System.Drawing.Rectangle? anchor)
    {
        _target = target;

        var settings = _settingsProvider();

        // Reflect the style remembered for THIS app (falling back to the global default), without
        // raising StyleChanged for the programmatic set.
        _loadingStyle = true;
        StyleCombo.SelectedItem = RewriteStyleCatalog.Get(AppStyles.For(_target.ExeName, settings));
        _loadingStyle = false;

        MicButton.Visibility = settings.Dictation ? Visibility.Visible : Visibility.Collapsed;

        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();   // force measure/arrange so ActualWidth/Height are valid before positioning
        if (anchor is { } a)
        {
            PositionNear(a);
        }
        else
        {
            PositionNearBottomCenter();
        }

        Activate();
        Input.Focus();
        Input.CaretIndex = Input.Text.Length;   // continue the preserved draft
        UpdateHeader();
    }

    /// <summary>Set the input's reading direction from its content and show the source→target label.</summary>
    private void UpdateHeader()
    {
        var settings = _settingsProvider();
        var text = Input.Text;

        bool rtl = string.IsNullOrWhiteSpace(text)
            ? LanguageDirector.IsRightToLeftLanguage(settings.LanguagePair.Primary)
            : LanguageDirector.IsRightToLeft(text);
        Input.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        // Placeholder in the language/direction the user is expected to type in, so it never reads as
        // English text stranded on the right of an RTL box.
        Input.PlaceholderText = rtl ? UiStrings.OverlayPlaceholderRtl : UiStrings.OverlayPlaceholder;

        var dir = LanguageDirector.Resolve(text, settings.LanguagePair, settings.AutoDirection);
        DirectionLabel.Text = $"{LanguageCatalog.DisplayName(dir.SourceLang)}  →  {LanguageCatalog.DisplayName(dir.TargetLang)}";
    }

    private void PositionNearBottomCenter()
    {
        var area = SystemParameters.WorkArea;     // DIPs, primary monitor working area
        Left = area.Left + ((area.Width - Width) / 2);
        Top = area.Top + (area.Height * 0.70);
    }

    /// <summary>Place the box just below the field, in physical pixels via <see cref="ScreenPlacement"/>
    /// (so it lands on the field's own monitor at the right DPI). <paramref name="anchorPx"/> is pixels.</summary>
    private void PositionNear(System.Drawing.Rectangle anchorPx)
    {
        double scale = ScreenPlacement.ScaleForPoint(anchorPx.Left, anchorPx.Bottom);

        double wDip = ActualWidth > 10 ? ActualWidth : Width;
        double hDip = ActualHeight > 10 ? ActualHeight : 160;
        int winW = (int)Math.Round(wDip * scale);
        int winH = (int)Math.Round(hDip * scale);
        int gap = (int)Math.Round(6 * scale);

        var (x, y) = ScreenPlacement.PlaceNearField(anchorPx, winW, winH, gap);
        ScreenPlacement.MoveTopLeft(new WindowInteropHelper(this).Handle, x, y, topmost: false, activate: true);
    }

    private async void OnTranslateClick(object sender, RoutedEventArgs e) => await TranslateAsync();

    private async Task TranslateAsync()
    {
        ComposeLog.Write($"translate: busy={_busy} speech={_speechState} readOnly={Input.IsReadOnly} " +
                         $"boxLen={Input.Text.Length} box='{ComposeLog.Peek(Input.Text)}'");

        if (_busy || _speechState != SpeechState.Idle)
        {
            ComposeLog.Write("translate: SKIPPED (busy or still dictating)");
            return;   // finish dictating first; the text is still being written
        }

        if (string.IsNullOrWhiteSpace(Input.Text))
        {
            ComposeLog.Write("translate: SKIPPED (box empty)");
            return;
        }

        // Enter the busy state and paint it BEFORE the network call (which runs synchronously up to its
        // first await) so the button disables and the spinner appears immediately, without a lag.
        _busy = true;
        TranslateButton.IsEnabled = false;
        TranslateButton.Content = UiStrings.OverlayTranslating;
        MicButton.IsEnabled = false;
        Busy.Visibility = Visibility.Visible;
        ClearStatus();
        await Dispatcher.Yield(DispatcherPriority.Background);

        // If the post-dictation proof-read is still running, join it — the user is about to send text
        // they can see, so it must be the corrected version. We never START a proof-read here: typos are
        // handled inside the translation call itself (below), which costs one round-trip instead of two.
        if (_correcting is { IsCompleted: false } running)
        {
            await running;
        }

        var text = Input.Text;   // captured AFTER any correction, so we translate what the user can see

        _inflight?.Cancel();
        _inflight?.Dispose();
        _inflight = new CancellationTokenSource();
        var ct = _inflight.Token;

        var settings = _settingsProvider();
        var direction = LanguageDirector.Resolve(text, settings.LanguagePair, settings.AutoDirection);
        var style = StyleCombo.SelectedItem is RewriteStyleOption o
            ? o.Style
            : AppStyles.For(_target.ExeName, settings);

        // Auto-correct rides along with the translation rather than running as its own pass (ADR-0010):
        // the source box is cleared on success and never read again, so proof-reading it separately only
        // bought the user a second wait. The translator reads through the typos instead.
        var request = new TranslationRequest(
            text, direction, settings.Model, style, settings.HumanizeTranslations,
            CorrectSource: settings.AutoCorrect);
        var sb = new StringBuilder();
        try
        {
            await foreach (var chunk in _translator.TranslateStreamAsync(request, ct))
            {
                sb.Append(chunk);
            }

            var translation = sb.ToString();
            ComposeLog.Write($"translated: dir={direction.SourceLang}->{direction.TargetLang} style={style} " +
                             $"in='{ComposeLog.Peek(text)}' out='{ComposeLog.Peek(translation)}'");

            // Inject via clipboard paste (Ctrl+End then Ctrl+V): it goes through the app's own editor,
            // so the text is styled correctly (UIA SetValue can land text that renders invisibly in
            // Chromium/contenteditable fields), appends after existing content, and leaves the caret at
            // the end.
            //
            // Hide FIRST: while our box holds the foreground, Windows can refuse to hand it to the
            // target, and the keystrokes would then land in the box itself and the translation would
            // vanish. We already have the text, so the box has nothing left to show.
            Hide();
            await _injector.AppendTextAsync(_target, translation, ct);
            Input.Clear();
            ClearStatus();
        }
        catch (OperationCanceledException)
        {
            // Superseded — ignore.
        }
        catch (TextInjectionException ex)
        {
            // The translation exists but could not be inserted (the clipboard was held, or the target
            // never took the foreground). Bring the box back with the draft intact so the user can just
            // press Translate again, rather than silently pasting the wrong thing or losing the text.
            ComposeLog.Write($"inject FAILED: {ex.Message} — draft kept");
            Show();
            Activate();
            ShowStatus(UiStrings.OverlayInjectFailed);
        }
        catch (InvalidOperationException)
        {
            ShowStatus(UiStrings.OverlayNoApiKey);   // usually: no API key configured
        }
        catch (Exception ex)
        {
            ShowStatus($"{UiStrings.OverlayError} {ex.Message}");
        }
        finally
        {
            _busy = false;
            TranslateButton.IsEnabled = true;
            TranslateButton.Content = UiStrings.OverlayTranslate;
            MicButton.IsEnabled = true;
            Busy.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Show a one-line status. Errors are critical-coloured; progress notes (e.g. "Listening…")
    /// are muted, so a normal dictation never looks like a failure.</summary>
    private void ShowStatus(string message, bool isError = true)
    {
        Status.Text = message;
        if (TryFindResource(isError ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush")
            is System.Windows.Media.Brush brush)
        {
            Status.Foreground = brush;
        }

        Status.Visibility = Visibility.Visible;
        _statusIsError = isError;
    }

    /// <summary>
    /// A muted progress note, which <b>never</b> overwrites an error.
    /// <para>
    /// The recognizer's state changes are marshalled onto the dispatcher, so they land <i>after</i> the
    /// synchronous catch that reported the failure. Letting a queued "Connecting…" (or the Idle handler's
    /// clear) run last is how "Add your OpenAI key in Settings first" was painted and instantly wiped,
    /// leaving the user staring at a mic button that silently did nothing. The error wins; it is cleared
    /// when the user next asks for something.
    /// </para>
    /// </summary>
    private void ShowProgress(string message)
    {
        if (!_statusIsError)
        {
            ShowStatus(message, isError: false);
        }
    }

    private void ClearStatus()
    {
        Status.Text = string.Empty;
        Status.Visibility = Visibility.Collapsed;
        _statusIsError = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        _visibilityWatch.Stop();
        _inflight?.Cancel();
        _inflight?.Dispose();
        base.OnClosed(e);
    }
}
