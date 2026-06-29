using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Models;
using AiTranslator.Core.Translation;

namespace AiTranslator.App.Windows;

/// <summary>
/// The floating input box. The user types source text; after a debounce the translation streams and
/// replaces the captured target field's content (spec §2). <c>Enter</c> inserts a newline; <c>Esc</c> closes.
/// </summary>
/// <remarks>
/// M1 captures the foreground window before showing and re-focuses itself after each injection. The
/// brief focus flicker (overlay → target → overlay) is an M1 limitation; M3 replaces it with a
/// non-activating overlay + background injection so typing is never interrupted.
/// </remarks>
public partial class OverlayInputWindow : Window
{
    private readonly IFocusTargetProvider _focus;
    private readonly ITranslationService _translator;
    private readonly ITextInjector _injector;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _debounce;

    private FocusTarget _target;
    private CancellationTokenSource? _inflight;

    public OverlayInputWindow(
        IFocusTargetProvider focus, ITranslationService translator, ITextInjector injector, AppSettings settings)
    {
        InitializeComponent();
        _focus = focus;
        _translator = translator;
        _injector = injector;
        _settings = settings;

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(settings.DebounceMs) };
        _debounce.Tick += OnDebounceTick;
        Input.TextChanged += (_, _) =>
        {
            _debounce.Stop();
            _debounce.Start();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
            // Enter is intentionally NOT handled: AcceptsReturn makes it insert a newline.
        };
    }

    /// <summary>Hotkey path (M1): capture the foreground window as the target and show bottom-centre.</summary>
    public void ShowFor() => ShowForCore(_focus.CaptureCurrent(), anchor: null);

    /// <summary>Badge path (M2): target the field the watcher resolved, anchored near its rectangle.</summary>
    public void ShowFor(FocusTarget target, System.Drawing.Rectangle? anchor) => ShowForCore(target, anchor);

    private void ShowForCore(FocusTarget target, System.Drawing.Rectangle? anchor)
    {
        _target = target;
        Input.Clear();
        Show();
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
    }

    private void PositionNearBottomCenter()
    {
        var area = SystemParameters.WorkArea;     // DIPs, primary monitor working area
        Left = area.Left + ((area.Width - Width) / 2);
        Top = area.Top + (area.Height * 0.70);
    }

    /// <summary>Place the box just below the field, in physical pixels via <see cref="ScreenPlacement"/>
    /// (so it lands on the field's own monitor at the right DPI — not the overlay's current monitor).
    /// <paramref name="anchorPx"/> is physical pixels.</summary>
    private void PositionNear(System.Drawing.Rectangle anchorPx)
    {
        double scale = ScreenPlacement.ScaleForPoint(anchorPx.Left, anchorPx.Bottom);
        int gap = (int)Math.Round(4 * scale);
        ScreenPlacement.MoveTopLeft(
            new WindowInteropHelper(this).Handle, anchorPx.Left, anchorPx.Bottom + gap, topmost: false, activate: true);
    }

    private async void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounce.Stop();
        await TranslateAndInjectAsync();
    }

    private async Task TranslateAndInjectAsync()
    {
        _inflight?.Cancel();
        _inflight?.Dispose();
        _inflight = new CancellationTokenSource();
        var ct = _inflight.Token;

        var text = Input.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var direction = LanguageDirector.Resolve(text, _settings.LanguagePair, _settings.AutoDirection);
        var sb = new StringBuilder();
        try
        {
            await foreach (var chunk in _translator.TranslateStreamAsync(text, direction, _settings.Model, ct))
            {
                sb.Append(chunk);
            }

            await _injector.ReplaceTextAsync(_target, sb.ToString(), ct);

            // Return focus so the user can keep typing (see remarks — flicker is an M1 limitation).
            Activate();
            Input.Focus();
        }
        catch (OperationCanceledException)
        {
            // Superseded by newer input — ignore.
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _debounce.Stop();
        _inflight?.Cancel();
        _inflight?.Dispose();
        base.OnClosed(e);
    }
}
