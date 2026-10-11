using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace LgController.UI;

public static class TrayIconRenderer
{
    private const string PowerGlyph = "";

    /// <summary>Quadrado arredondado na cor dada, com a temperatura ou, se <paramref name="text"/> for nulo, o símbolo de energia.</summary>
    public static Icon Render(string? text, (byte R, byte G, byte B) color, bool alert)
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            using var background = RoundedRectangle(new RectangleF(1, 1, 30, 30), 8);
            using var fill = new SolidBrush(Color.FromArgb(color.R, color.G, color.B));
            g.FillPath(fill, background);

            using var font = text is null
                ? new Font("Segoe MDL2 Assets", 17, FontStyle.Regular, GraphicsUnit.Pixel)
                : new Font("Segoe UI", text.Length > 2 ? 13 : 19, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text ?? PowerGlyph, font, Brushes.White, new RectangleF(0, 1, 32, 32), format);

            if (alert)
            {
                g.FillEllipse(Brushes.Black, 19, 19, 13, 13);
                g.FillEllipse(Brushes.Gold, 21, 21, 9, 9);
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
