using System.Windows;
using System.Windows.Media;

namespace LgController.UI;

/// <summary>270° arc whose filled part is proportional to Value between Minimum and Maximum.</summary>
public sealed class ArcGauge : FrameworkElement
{
    private const double StartAngle = 135;
    private const double SweepAngle = 270;

    public static readonly DependencyProperty MinimumProperty = Register(nameof(Minimum), 0d);
    public static readonly DependencyProperty MaximumProperty = Register(nameof(Maximum), 1d);
    public static readonly DependencyProperty ValueProperty = Register(nameof(Value), 0d);
    public static readonly DependencyProperty ThicknessProperty = Register(nameof(Thickness), 10d);
    public static readonly DependencyProperty AccentBrushProperty = Register<Brush?>(nameof(AccentBrush), null);
    public static readonly DependencyProperty TrackBrushProperty = Register<Brush?>(nameof(TrackBrush), null);

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public Brush? AccentBrush { get => (Brush?)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    private static DependencyProperty Register<T>(string name, T defaultValue) =>
        DependencyProperty.Register(
            name,
            typeof(T),
            typeof(ArcGauge),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        var radius = (size - Thickness) / 2;
        if (radius <= 0) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        if (TrackBrush is not null) DrawArc(drawingContext, TrackBrush, center, radius, 1);

        var fraction = Maximum > Minimum ? Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1) : 0;
        if (AccentBrush is not null && fraction > 0.001) DrawArc(drawingContext, AccentBrush, center, radius, fraction);
    }

    private void DrawArc(DrawingContext drawingContext, Brush brush, Point center, double radius, double fraction)
    {
        var sweep = SweepAngle * fraction;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(PointAt(center, radius, StartAngle), isFilled: false, isClosed: false);
            context.ArcTo(
                PointAt(center, radius, StartAngle + sweep),
                new Size(radius, radius),
                rotationAngle: 0,
                isLargeArc: sweep > 180,
                SweepDirection.Clockwise,
                isStroked: true,
                isSmoothJoin: false);
        }

        geometry.Freeze();
        var pen = new Pen(brush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private static Point PointAt(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }
}
