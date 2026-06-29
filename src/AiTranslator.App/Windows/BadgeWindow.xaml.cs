using System.Drawing;
using System.Windows.Interop;
using AiTranslator.Core.Awareness;

namespace AiTranslator.App.Windows;

/// <summary>
/// The Grammarly-style badge (M2 Task 4): a small always-on-top, non-activating button that anchors
/// beside the focused field. Clicking it raises <see cref="Clicked"/> (the App opens the overlay
/// targeting that field). Positioned in physical pixels via <see cref="ScreenPlacement"/> so
/// multi-monitor / mixed-DPI placement does not depend on WPF's DIP layout of an off-screen window.
/// </summary>
public partial class BadgeWindow : NonActivatingWindow
{
    public BadgeWindow()
    {
        InitializeComponent();
        Root.MouseLeftButtonUp += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the user clicks the badge.</summary>
    public event EventHandler? Clicked;

    /// <summary>Show (or move) the badge anchored to <paramref name="fieldRect"/> (physical pixels).</summary>
    public void ShowAt(Rectangle fieldRect, AppOffset offset)
    {
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();   // so ActualWidth/Height are valid for the inside-the-edge math

        // Grammarly-style: nestle the badge just INSIDE the field's right edge, vertically centred —
        // not hanging off a corner. dx/dy (DIPs) are optional per-app nudges from that anchor.
        double scale = ScreenPlacement.ScaleForPoint(fieldRect.Left, fieldRect.Top);
        int badgeW = (int)Math.Round((ActualWidth > 0 ? ActualWidth : 26) * scale);
        int badgeH = (int)Math.Round((ActualHeight > 0 ? ActualHeight : 26) * scale);
        int margin = (int)Math.Round(6 * scale);

        int x = fieldRect.Right - badgeW - margin + (int)Math.Round(offset.Dx * scale);
        int y = fieldRect.Top + ((fieldRect.Height - badgeH) / 2) + (int)Math.Round(offset.Dy * scale);

        ScreenPlacement.MoveTopLeft(new WindowInteropHelper(this).Handle, x, y, topmost: true, activate: false);
    }
}
