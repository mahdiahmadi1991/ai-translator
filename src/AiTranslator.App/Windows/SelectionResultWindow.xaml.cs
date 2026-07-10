using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using AiTranslator.App.Resources;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Models;
using AiTranslator.Core.Translation;
using Wpf.Ui.Controls;

namespace AiTranslator.App.Windows;

/// <summary>
/// The read-mode pop-up: shows the translation of selected text (streaming, read-only). The source
/// language is detected and the direction shown; the user can copy the result or re-translate to a
/// different target language. A reusable singleton — it hides when focus leaves (via a foreground poll,
/// which — unlike <see cref="Window.Deactivated"/> — never dismisses the box while its own dropdown is
/// open), preserving its place, and cancels any in-flight request so a hidden box never burns tokens.
/// </summary>
public partial class SelectionResultWindow : Window
{
    // Reserve the fully-grown height when placing: the window has SizeToContent=Height and grows DOWN
    // from a fixed Top as text streams in, so placement must budget for the tallest it can become
    // (12 margin + ~40 header + 240 body cap + ~52 footer + 12 margin) or a long result clips off-screen.
    private const double MaxWindowHeightDip = 360;

    private readonly ITranslationService _translator;
    private readonly Func<AppSettings> _settingsProvider;
    private readonly DispatcherTimer _visibilityWatch;
    private readonly DispatcherTimer _copyFeedbackTimer;

    private string _source = string.Empty;
    private string _sourceLang = string.Empty;
    private string _currentTarget = string.Empty;   // the target the visible result was translated to
    private nint _sourceWindow;                      // the app the selection came from (keeps us visible)
    private CancellationTokenSource? _inflight;
    private bool _loading;
    private bool _busy;
    private bool _hasResult;                         // a clean, non-empty translation is on screen

    public SelectionResultWindow(ITranslationService translator, Func<AppSettings> settingsProvider)
    {
        InitializeComponent();
        _translator = translator;
        _settingsProvider = settingsProvider;

        TargetCombo.ItemsSource = LanguageCatalog.All;

        // Re-translate when the user commits a new target from the dropdown (not on every navigation
        // keystroke). Dedupe against the target already on screen so re-opening the picker is free.
        TargetCombo.DropDownClosed += (_, _) =>
        {
            if (!_loading && TargetCombo.SelectedItem is LanguageOption o
                && !string.Equals(o.Code, _currentTarget, StringComparison.OrdinalIgnoreCase))
            {
                _ = TranslateAsync();
            }
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Hide();
            }
        };

        // Ctrl+C copies the whole result even when the caret isn't inside the text box.
        InputBindings.Add(new KeyBinding(new RelayCommand(CopyResult), Key.C, ModifierKeys.Control));

        // Foreground poll: hide when focus is neither this window nor the source app (mirrors the write
        // box). More reliable than Deactivated, which fires spuriously when the target-language dropdown
        // (a transparent Popup) opens and would otherwise dismiss the box mid-interaction.
        _visibilityWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _visibilityWatch.Tick += OnVisibilityWatchTick;

        _copyFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
        _copyFeedbackTimer.Tick += (_, _) => ResetCopyButton();

        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue)
            {
                _visibilityWatch.Start();
            }
            else
            {
                _visibilityWatch.Stop();
                _inflight?.Cancel();   // a hidden box must not keep streaming (Esc / close / click-away)
                _busy = false;
                BusyOverlay.Visibility = Visibility.Collapsed;
            }
        };
    }

    private void OnVisibilityWatchTick(object? sender, EventArgs e)
    {
        nint foreground = ScreenPlacement.ForegroundWindow();
        nint self = new WindowInteropHelper(this).Handle;
        if (foreground != self && foreground != _sourceWindow)
        {
            Hide();
        }
    }

    /// <summary>Show the pop-up for a selection, anchored below its bounds (or bottom-centre).</summary>
    public void ShowFor(SelectedText selection, System.Drawing.Rectangle? anchorPx)
    {
        var settings = _settingsProvider();
        var direction = LanguageDirector.Resolve(selection.Text, settings.LanguagePair, settings.AutoDirection);

        _source = selection.Text;
        _sourceLang = direction.SourceLang;
        _sourceWindow = selection.WindowHandle;   // stay visible while the user flips back to this app

        _loading = true;
        TargetCombo.SelectedItem = LanguageCatalog.Get(direction.TargetLang);
        _loading = false;

        ResetCopyButton();
        _hasResult = false;
        ClearStatus();
        Output.Clear();
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        if (anchorPx is { } a)
        {
            PositionNear(a);
        }
        else
        {
            PositionNearBottomCenter();
        }

        Activate();
        Output.Focus();   // keep initial focus off the Dismiss/Combo so a stray key can't close/re-translate
        _ = TranslateAsync();
    }

    private void UpdateDirectionLabel(string targetLang)
    {
        // Native names (matching the picker), each wrapped in a Unicode isolate (U+2068…U+2069) so the
        // neutral arrow can't be reordered by the bidi algorithm around an RTL name.
        string src = LanguageCatalog.NativeName(_sourceLang);
        string dst = LanguageCatalog.NativeName(targetLang);
        DirectionLabel.Text = $"⁨{src}⁩  →  ⁨{dst}⁩";
    }

    private async Task TranslateAsync()
    {
        if (string.IsNullOrWhiteSpace(_source))
        {
            return;   // guard FIRST — never mutate _busy/_inflight for an empty source (would freeze it)
        }

        string target = TargetCombo.SelectedItem is LanguageOption option ? option.Code : _sourceLang;
        _currentTarget = target;
        UpdateDirectionLabel(target);

        // Reading direction is set from the KNOWN target code up front, so streamed chunks land correctly
        // from the first token. Full ternary — this singleton must flip back to LTR for a non-RTL target.
        Output.FlowDirection = LanguageDirector.IsRightToLeftLanguage(target)
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        _busy = true;
        _hasResult = false;
        SetCopyEnabled(false);
        ClearStatus();
        Output.Clear();
        BusyOverlay.Visibility = Visibility.Visible;
        await Dispatcher.Yield(DispatcherPriority.Background);

        _inflight?.Cancel();
        _inflight?.Dispose();
        _inflight = new CancellationTokenSource();
        var myCts = _inflight;
        var ct = myCts.Token;

        var settings = _settingsProvider();
        var direction = new TranslationDirection(_sourceLang, target);
        try
        {
            await foreach (var chunk in _translator.TranslateStreamAsync(_source, direction, settings.Model, ct))
            {
                if (BusyOverlay.Visibility == Visibility.Visible)
                {
                    BusyOverlay.Visibility = Visibility.Collapsed;   // first token arrived
                }

                Output.AppendText(chunk);
                Output.ScrollToEnd();
            }

            if (!ct.IsCancellationRequested)
            {
                if (string.IsNullOrWhiteSpace(Output.Text))
                {
                    ShowStatus(UiStrings.SelectionEmpty, isError: false);
                }
                else
                {
                    _hasResult = true;
                    SetCopyEnabled(true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // superseded / hidden — ignore
        }
        catch (InvalidOperationException)
        {
            ShowStatus(UiStrings.OverlayNoApiKey, isError: true);
        }
        catch (Exception ex)
        {
            ShowStatus($"{UiStrings.OverlayError} {ex.Message}", isError: true);
        }
        finally
        {
            // Only the OWNING request resets shared UI state — a superseded request must not clobber the
            // spinner/flag of the one that replaced it (nor leave _busy stuck true on the whitespace path).
            if (ReferenceEquals(_inflight, myCts))
            {
                _busy = false;
                BusyOverlay.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e) => CopyResult();

    private void CopyResult()
    {
        if (_busy || !_hasResult || string.IsNullOrWhiteSpace(Output.Text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(Output.Text);
            CopyButton.Content = UiStrings.SelectionCopied;
            CopyIcon.Symbol = SymbolRegular.Checkmark24;
            _copyFeedbackTimer.Stop();
            _copyFeedbackTimer.Start();   // revert after a moment, even if the pop-up stays open
        }
        catch { /* clipboard momentarily locked — ignore */ }
    }

    private void ResetCopyButton()
    {
        _copyFeedbackTimer.Stop();
        CopyButton.Content = UiStrings.SelectionCopy;
        CopyIcon.Symbol = SymbolRegular.Copy24;
    }

    private void SetCopyEnabled(bool enabled) => CopyButton.IsEnabled = enabled;

    private void ShowStatus(string message, bool isError)
    {
        StatusText.Text = message;
        if (TryFindResource(isError ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush")
            is System.Windows.Media.Brush brush)
        {
            StatusText.Foreground = brush;
        }

        StatusText.Visibility = Visibility.Visible;
    }

    private void ClearStatus()
    {
        StatusText.Text = string.Empty;
        StatusText.Visibility = Visibility.Collapsed;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsWithinButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        try { DragMove(); } catch { /* button released already — ignore */ }
    }

    private static bool IsWithinButton(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is System.Windows.Controls.Primitives.ButtonBase)
            {
                return true;
            }

            node = node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(node)
                : null;
        }

        return false;
    }

    protected override void OnClosed(EventArgs e)
    {
        _visibilityWatch.Stop();
        _copyFeedbackTimer.Stop();
        _inflight?.Cancel();
        _inflight?.Dispose();
        base.OnClosed(e);
    }

    private void PositionNearBottomCenter()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + ((area.Width - Width) / 2);
        Top = area.Top + (area.Height * 0.62);
    }

    private void PositionNear(System.Drawing.Rectangle anchorPx)
    {
        double scale = ScreenPlacement.ScaleForPoint(anchorPx.Left, anchorPx.Bottom);
        double wDip = ActualWidth > 10 ? ActualWidth : Width;
        double hDip = Math.Max(ActualHeight, MaxWindowHeightDip);   // budget for the grown height
        int winW = (int)Math.Round(wDip * scale);
        int winH = (int)Math.Round(hDip * scale);
        int gap = (int)Math.Round(8 * scale);

        var (x, y) = ScreenPlacement.PlaceNearField(anchorPx, winW, winH, gap);
        ScreenPlacement.MoveTopLeft(new WindowInteropHelper(this).Handle, x, y, topmost: true, activate: true);
    }

    // Minimal ICommand for the Ctrl+C key binding (no MVVM framework in play).
    private sealed class RelayCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();
    }
}
