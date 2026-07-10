using System.Drawing;
using System.Windows.Interop;

namespace AiTranslator.App.Windows;

/// <summary>
/// The read-mode icon: a small, non-activating button anchored just below a text selection. Left-click
/// raises <see cref="Clicked"/> (open the translation pop-up). Positioned in physical pixels via
/// <see cref="ScreenPlacement"/> so it lands correctly on multi-monitor / mixed-DPI setups.
/// </summary>
public partial class SelectionBadgeWindow : NonActivatingWindow
{
    public SelectionBadgeWindow()
    {
        InitializeComponent();
        Root.MouseLeftButtonUp += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the user clicks the icon.</summary>
    public event EventHandler? Clicked;

    /// <summary>Show (or move) the icon just below the selection's bottom edge (physical pixels).</summary>
    public void ShowAt(Rectangle selectionPx)
    {
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();

        double scale = ScreenPlacement.ScaleForPoint(selectionPx.Left, selectionPx.Bottom);
        int badgeW = (int)Math.Round((ActualWidth > 0 ? ActualWidth : 40) * scale);
        int badgeH = (int)Math.Round((ActualHeight > 0 ? ActualHeight : 40) * scale);
        int gap = (int)Math.Round(2 * scale);

        // nestle it just below the selection's bottom-right corner
        int x = selectionPx.Right - badgeW;
        int y = selectionPx.Bottom + gap;

        ScreenPlacement.MoveTopLeft(new WindowInteropHelper(this).Handle, x, y, topmost: true, activate: false);
    }
}
