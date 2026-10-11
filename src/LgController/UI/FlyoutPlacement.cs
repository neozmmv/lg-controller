using System.Windows;

namespace LgController.UI;

public static class FlyoutPlacement
{
    /// <summary>
    /// Canto da área de trabalho mais próximo da bandeja. A área de trabalho começa depois da barra
    /// de tarefas quando ela está em cima ou à esquerda, e é por aí que se sabe onde ela está.
    /// </summary>
    public static Point Compute(Rect workArea, Size window, double margin = 12)
    {
        var x = workArea.Left > 0 ? workArea.Left + margin : workArea.Right - window.Width - margin;
        var y = workArea.Top > 0 ? workArea.Top + margin : workArea.Bottom - window.Height - margin;
        return new Point(Math.Max(x, workArea.Left + margin), Math.Max(y, workArea.Top + margin));
    }
}
