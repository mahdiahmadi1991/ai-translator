using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using AiTranslator.App.Resources;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Models;
using AiTranslator.Core.Translation;

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
    private readonly ITargetResolver _resolver;
    private readonly Func<AppSettings> _settingsProvider;
    private readonly DispatcherTimer _closeWatch;

    private FocusTarget _target;
    private CancellationTokenSource? _inflight;
    private bool _busy;

    public OverlayInputWindow(
        IFocusTargetProvider focus, ITranslationService translator, ITextInjector injector,
        ITargetResolver resolver, Func<AppSettings> settingsProvider)
    {
        InitializeComponent();
        _focus = focus;
        _translator = translator;
        _injector = injector;
        _resolver = resolver;
        _settingsProvider = settingsProvider;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
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

        // Auto-hide: when the box loses activation, wait briefly (the clipboard-paste fallback can
        // momentarily foreground the target) then hide ONLY if focus truly went elsewhere — i.e. the
        // foreground is neither this box nor the target field. Hiding preserves the draft; re-activation
        // cancels the pending hide.
        _closeWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _closeWatch.Tick += OnCloseWatchTick;
        Deactivated += (_, _) =>
        {
            _closeWatch.Stop();
            _closeWatch.Start();
        };
        Activated += (_, _) => _closeWatch.Stop();
    }

    private void OnCloseWatchTick(object? sender, EventArgs e)
    {
        _closeWatch.Stop();
        nint foreground = ScreenPlacement.ForegroundWindow();
        nint self = new WindowInteropHelper(this).Handle;
        if (foreground != self && foreground != _target.WindowHandle)
        {
            Hide();   // preserve the draft; the box reappears (with its text) on the next badge/hotkey
        }
    }

    /// <summary>Hotkey path: capture the foreground window as the target and show bottom-centre.</summary>
    public void ShowFor() => ShowForCore(_focus.CaptureCurrent(), anchor: null);

    /// <summary>Badge path: target the field the watcher resolved, anchored near its rectangle.</summary>
    public void ShowFor(FocusTarget target, System.Drawing.Rectangle? anchor) => ShowForCore(target, anchor);

    private void ShowForCore(FocusTarget target, System.Drawing.Rectangle? anchor)
    {
        _target = target;
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
        if (_busy)
        {
            return;
        }

        var text = Input.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _busy = true;
        TranslateButton.IsEnabled = false;
        var label = TranslateButton.Content;
        TranslateButton.Content = UiStrings.OverlayTranslating;
        ClearStatus();

        _inflight?.Cancel();
        _inflight?.Dispose();
        _inflight = new CancellationTokenSource();
        var ct = _inflight.Token;

        var settings = _settingsProvider();
        var direction = LanguageDirector.Resolve(text, settings.LanguagePair, settings.AutoDirection);
        var sb = new StringBuilder();
        try
        {
            await foreach (var chunk in _translator.TranslateStreamAsync(text, direction, settings.Model, ct))
            {
                sb.Append(chunk);
            }

            var translation = sb.ToString();

            // Preferred: set the field's value directly via UIA — no focus move. Fallback: clipboard
            // paste (briefly foregrounds the target, then we return focus here).
            if (!_resolver.TrySetText(_target.WindowHandle, translation))
            {
                await _injector.ReplaceTextAsync(_target, translation, ct);
                Activate();
                Input.Focus();
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded — ignore.
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
            TranslateButton.Content = label;
            TranslateButton.IsEnabled = true;
        }
    }

    private void ShowStatus(string message)
    {
        Status.Text = message;
        Status.Visibility = Visibility.Visible;
    }

    private void ClearStatus()
    {
        Status.Text = string.Empty;
        Status.Visibility = Visibility.Collapsed;
    }

    protected override void OnClosed(EventArgs e)
    {
        _closeWatch.Stop();
        _inflight?.Cancel();
        _inflight?.Dispose();
        base.OnClosed(e);
    }
}
