using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using AiTranslator.App.Resources;
using AiTranslator.Core.Awareness;

namespace AiTranslator.App.Windows;

/// <summary>
/// The Grammarly-style badge (M2 Task 4): a small always-on-top, non-activating button that anchors
/// beside the focused field. Left-click raises <see cref="Clicked"/> (open the overlay); right-click
/// opens a context menu (ignore this app / settings / quit). Positioned in physical pixels via
/// <see cref="ScreenPlacement"/> so multi-monitor / mixed-DPI placement does not depend on WPF's DIP
/// layout of an off-screen window.
/// </summary>
public partial class BadgeWindow : NonActivatingWindow
{
    public BadgeWindow()
    {
        InitializeComponent();
        Root.MouseLeftButtonUp += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
        IgnoreItem.Header = UiStrings.BadgeIgnoreGeneric;
    }

    /// <summary>Raised when the user clicks the badge.</summary>
    public event EventHandler? Clicked;

    /// <summary>Right-click → "Don't show in this app" (the App adds it to the blocklist).</summary>
    public event EventHandler? IgnoreAppRequested;

    /// <summary>Right-click → "Settings…".</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Right-click → "Quit".</summary>
    public event EventHandler? QuitRequested;

    /// <summary>Label the "don't show" item with the current app, e.g. "Don't show the badge in Telegram".</summary>
    public void SetAppName(string? exeName)
    {
        if (string.IsNullOrWhiteSpace(exeName))
        {
            IgnoreItem.Header = UiStrings.BadgeIgnoreGeneric;
            return;
        }

        var display = exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exeName[..^4] : exeName;
        IgnoreItem.Header = string.Format(UiStrings.BadgeIgnoreFormat, display);
    }

    private void OnIgnoreClick(object sender, RoutedEventArgs e) => IgnoreAppRequested?.Invoke(this, EventArgs.Empty);

    private void OnSettingsMenuClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnQuitClick(object sender, RoutedEventArgs e) => QuitRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Show (or move) the badge anchored to <paramref name="fieldRect"/> (physical pixels).</summary>
    public void ShowAt(Rectangle fieldRect, AppOffset offset)
    {
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();   // so ActualWidth/Height are valid for the inside-the-corner math

        // Grammarly-style: nestle the badge into the field's BOTTOM-RIGHT corner, just inside the edge
        // (the badge XAML already carries a margin for its shadow). dx/dy (DIPs) are per-app nudges.
        double scale = ScreenPlacement.ScaleForPoint(fieldRect.Left, fieldRect.Top);
        int badgeW = (int)Math.Round((ActualWidth > 0 ? ActualWidth : 42) * scale);
        int badgeH = (int)Math.Round((ActualHeight > 0 ? ActualHeight : 42) * scale);

        int x = fieldRect.Right - badgeW + (int)Math.Round(offset.Dx * scale);
        int y = fieldRect.Bottom - badgeH + (int)Math.Round(offset.Dy * scale);

        ScreenPlacement.MoveTopLeft(new WindowInteropHelper(this).Handle, x, y, topmost: true, activate: false);
    }
}
