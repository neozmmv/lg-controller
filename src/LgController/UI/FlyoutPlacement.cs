using System.Windows;

namespace LgController.UI;

public static class FlyoutPlacement
{
    /// <summary>
    /// Work-area corner closest to the tray. The work area starts after the taskbar
    /// when it sits at the top or left, and that is how its position is known.
    /// </summary>
    public static Point Compute(Rect workArea, Size window, double margin = 12)
    {
        var x = workArea.Left > 0 ? workArea.Left + margin : workArea.Right - window.Width - margin;
        var y = workArea.Top > 0 ? workArea.Top + margin : workArea.Bottom - window.Height - margin;
        return new Point(Math.Max(x, workArea.Left + margin), Math.Max(y, workArea.Top + margin));
    }
}
