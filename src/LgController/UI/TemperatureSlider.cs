using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LgController.UI;

/// <summary>Bar that fills from cold to warm up to Value; dragging or scrolling changes Value in Step increments.</summary>
public sealed class TemperatureSlider : FrameworkElement
{
    private const double Radius = 12;
    /// <summary>Width of the fill at Minimum, so the thumb always has room.</summary>
    private const double MinFill = 28;
    private const double ThumbInset = 12;

    private static readonly Brush FillBrush = CreateFill();
    private static readonly Brush ThumbBrush = Brushes.White;

    public static readonly DependencyProperty MinimumProperty = Register(nameof(Minimum), 0d);
    public static readonly DependencyProperty MaximumProperty = Register(nameof(Maximum), 1d);
    public static readonly DependencyProperty StepProperty = Register(nameof(Step), 1d);
    public static readonly DependencyProperty TrackBrushProperty = Register<Brush?>(nameof(TrackBrush), null);

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(TemperatureSlider),
        new FrameworkPropertyMetadata(
            0d,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, e) => ((TemperatureSlider)d).OnValueChanged((double)e.NewValue)));

    /// <summary>The value currently drawn; trails Value while animating.</summary>
    private static readonly DependencyProperty ShownValueProperty = Register(nameof(ShownValue), 0d);

    private bool _dragging;

    public TemperatureSlider()
    {
        Cursor = Cursors.Hand;
    }

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    private double ShownValue => (double)GetValue(ShownValueProperty);

    private static DependencyProperty Register<T>(string name, T defaultValue) =>
        DependencyProperty.Register(
            name,
            typeof(T),
            typeof(TemperatureSlider),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    private static Brush CreateFill()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x2F, 0x7B, 0xF0), 0),
                new GradientStop(Color.FromRgb(0x6E, 0xA0, 0xE6), 0.42),
                new GradientStop(Color.FromRgb(0xE9, 0xA8, 0x7C), 0.72),
                new GradientStop(Color.FromRgb(0xF5, 0x7C, 0x22), 1),
            },
        };
        brush.Freeze();
        return brush;
    }

    private void OnValueChanged(double value)
    {
        // Follows the pointer directly while dragging; glides otherwise.
        if (_dragging || !IsVisible)
        {
            BeginAnimation(ShownValueProperty, null);
            SetValue(ShownValueProperty, value);
            return;
        }

        BeginAnimation(
            ShownValueProperty,
            new DoubleAnimation(value, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= MinFill || bounds.Height <= 0) return;

        drawingContext.DrawRoundedRectangle(TrackBrush ?? Brushes.Transparent, null, bounds, Radius, Radius);

        var fraction = Maximum > Minimum ? Math.Clamp((ShownValue - Minimum) / (Maximum - Minimum), 0, 1) : 0;
        var fillWidth = MinFill + fraction * (bounds.Width - MinFill);

        // The gradient spans the whole track, so a longer fill reveals warmer colors.
        drawingContext.PushClip(new RectangleGeometry(new Rect(0, 0, fillWidth, bounds.Height), Radius, Radius));
        drawingContext.DrawRectangle(FillBrush, null, bounds);
        drawingContext.Pop();

        var thumb = new Rect(fillWidth - ThumbInset - 2, 9, 4, Math.Max(0, bounds.Height - 18));
        drawingContext.DrawRoundedRectangle(ThumbBrush, null, thumb, 2, 2);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragging = CaptureMouse();
        SetFromPointer(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) SetFromPointer(e.GetPosition(this).X);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragging) ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) => _dragging = false;

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        SetSnapped(Value + Math.Sign(e.Delta) * Step);
        e.Handled = true;
    }

    private void SetFromPointer(double x)
    {
        if (ActualWidth <= MinFill || Maximum <= Minimum) return;
        var fraction = (x + ThumbInset - MinFill) / (ActualWidth - MinFill);
        SetSnapped(Minimum + fraction * (Maximum - Minimum));
    }

    private void SetSnapped(double value)
    {
        var step = Step > 0 ? Step : 1;
        var snapped = Minimum + Math.Round((value - Minimum) / step, MidpointRounding.AwayFromZero) * step;
        snapped = Math.Clamp(snapped, Minimum, Maximum);
        if (snapped != Value) SetCurrentValue(ValueProperty, snapped);
    }
}
