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
    private readonly Func<AppSettings> _settingsProvider;
    private readonly DispatcherTimer _visibilityWatch;

    private FocusTarget _target;
    private CancellationTokenSource? _inflight;
    private bool _busy;
    private bool _loadingStyle;   // suppress StyleChanged while we set the combo programmatically

    public OverlayInputWindow(
        IFocusTargetProvider focus, ITranslationService translator, ITextInjector injector,
        Func<AppSettings> settingsProvider)
    {
        InitializeComponent();
        _focus = focus;
        _translator = translator;
        _injector = injector;
        _settingsProvider = settingsProvider;

        StyleCombo.ItemsSource = RewriteStyleCatalog.All;
        StyleCombo.SelectionChanged += OnStyleChanged;

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
            if (node is Button)
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

        // Reflect the style remembered for THIS app (falling back to the global default), without
        // raising StyleChanged for the programmatic set.
        _loadingStyle = true;
        StyleCombo.SelectedItem = RewriteStyleCatalog.Get(AppStyles.For(_target.ExeName, _settingsProvider()));
        _loadingStyle = false;

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
        if (_busy)
        {
            return;
        }

        var text = Input.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // Enter the busy state and paint it BEFORE the network call (which runs synchronously up to its
        // first await) so the button disables and the spinner appears immediately, without a lag.
        _busy = true;
        TranslateButton.IsEnabled = false;
        TranslateButton.Content = UiStrings.OverlayTranslating;
        Busy.Visibility = Visibility.Visible;
        ClearStatus();
        await Dispatcher.Yield(DispatcherPriority.Background);

        _inflight?.Cancel();
        _inflight?.Dispose();
        _inflight = new CancellationTokenSource();
        var ct = _inflight.Token;

        var settings = _settingsProvider();
        var direction = LanguageDirector.Resolve(text, settings.LanguagePair, settings.AutoDirection);
        var style = StyleCombo.SelectedItem is RewriteStyleOption o
            ? o.Style
            : AppStyles.For(_target.ExeName, settings);
        var request = new TranslationRequest(text, direction, settings.Model, style, settings.HumanizeTranslations);
        var sb = new StringBuilder();
        try
        {
            await foreach (var chunk in _translator.TranslateStreamAsync(request, ct))
            {
                sb.Append(chunk);
            }

            var translation = sb.ToString();

            // Inject via clipboard paste (Ctrl+End then Ctrl+V): it goes through the app's own editor,
            // so the text is styled correctly (UIA SetValue can land text that renders invisibly in
            // Chromium/contenteditable fields), appends after existing content, and leaves the caret at
            // the end. Then clear the draft and dismiss (focus returns to the app, ready to send).
            await _injector.AppendTextAsync(_target, translation, ct);
            Input.Clear();
            ClearStatus();
            Hide();
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
            TranslateButton.IsEnabled = true;
            TranslateButton.Content = UiStrings.OverlayTranslate;
            Busy.Visibility = Visibility.Collapsed;
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
        _visibilityWatch.Stop();
        _inflight?.Cancel();
        _inflight?.Dispose();
        base.OnClosed(e);
    }
}
