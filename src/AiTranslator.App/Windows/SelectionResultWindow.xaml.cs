using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using AiTranslator.App.Resources;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Models;
using AiTranslator.Core.Translation;

namespace AiTranslator.App.Windows;

/// <summary>
/// The read-mode pop-up: shows the translation of selected text (streaming, read-only). The source
/// language is detected and the direction shown; the user can copy the result or re-translate to a
/// different target language. A reusable singleton — it hides when it loses focus, keeping its place.
/// </summary>
public partial class SelectionResultWindow : Window
{
    private readonly ITranslationService _translator;
    private readonly Func<AppSettings> _settingsProvider;

    private string _source = string.Empty;
    private string _sourceLang = string.Empty;
    private CancellationTokenSource? _inflight;
    private bool _loading;
    private bool _busy;

    public SelectionResultWindow(ITranslationService translator, Func<AppSettings> settingsProvider)
    {
        InitializeComponent();
        _translator = translator;
        _settingsProvider = settingsProvider;

        TargetCombo.ItemsSource = LanguageCatalog.All;
        TargetCombo.SelectionChanged += (_, _) =>
        {
            if (!_loading)
            {
                _ = TranslateAsync();   // re-translate to the newly picked target
            }
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Hide();
            }
        };

        Deactivated += (_, _) => Hide();   // click elsewhere → get out of the way
    }

    /// <summary>Show the pop-up for a selection, anchored below its bounds (or bottom-centre).</summary>
    public void ShowFor(SelectedText selection, System.Drawing.Rectangle? anchorPx)
    {
        var settings = _settingsProvider();
        var direction = LanguageDirector.Resolve(selection.Text, settings.LanguagePair, settings.AutoDirection);

        _source = selection.Text;
        _sourceLang = direction.SourceLang;

        _loading = true;
        TargetCombo.SelectedItem = LanguageCatalog.Get(direction.TargetLang);
        _loading = false;

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
        _ = TranslateAsync();
    }

    private void UpdateDirectionLabel(string targetLang)
        => DirectionLabel.Text = $"{LanguageCatalog.DisplayName(_sourceLang)}  →  {LanguageCatalog.DisplayName(targetLang)}";

    private async Task TranslateAsync()
    {
        if (_busy)
        {
            _inflight?.Cancel();   // target changed mid-flight — supersede
        }

        if (string.IsNullOrWhiteSpace(_source))
        {
            return;
        }

        string target = TargetCombo.SelectedItem is LanguageOption option ? option.Code : _sourceLang;
        UpdateDirectionLabel(target);

        _busy = true;
        Busy.Visibility = Visibility.Visible;
        Output.Clear();
        await Dispatcher.Yield(DispatcherPriority.Background);

        _inflight?.Cancel();
        _inflight?.Dispose();
        _inflight = new CancellationTokenSource();
        var ct = _inflight.Token;

        var settings = _settingsProvider();
        var direction = new TranslationDirection(_sourceLang, target);
        try
        {
            await foreach (var chunk in _translator.TranslateStreamAsync(_source, direction, settings.Model, ct))
            {
                Output.AppendText(chunk);
                Output.ScrollToEnd();
            }
        }
        catch (OperationCanceledException)
        {
            // superseded — ignore
        }
        catch (InvalidOperationException)
        {
            Output.Text = UiStrings.OverlayNoApiKey;
        }
        catch (Exception ex)
        {
            Output.Text = $"{UiStrings.OverlayError} {ex.Message}";
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                _busy = false;
                Busy.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrEmpty(Output.Text))
            {
                Clipboard.SetText(Output.Text);
                CopyButton.Content = UiStrings.SelectionCopied;
            }
        }
        catch { /* clipboard momentarily locked — ignore */ }
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
        _inflight?.Cancel();
        _inflight?.Dispose();
        base.OnClosed(e);
    }

    // Reset the "Copied" label back to "Copy" whenever the pop-up is shown again.
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        CopyButton.Content = UiStrings.SelectionCopy;
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
        double hDip = ActualHeight > 10 ? ActualHeight : 160;
        int winW = (int)Math.Round(wDip * scale);
        int winH = (int)Math.Round(hDip * scale);
        int gap = (int)Math.Round(8 * scale);

        var (x, y) = ScreenPlacement.PlaceNearField(anchorPx, winW, winH, gap);
        ScreenPlacement.MoveTopLeft(new WindowInteropHelper(this).Handle, x, y, topmost: true, activate: true);
    }
}
