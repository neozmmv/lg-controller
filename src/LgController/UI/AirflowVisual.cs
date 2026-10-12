using System.Windows;
using System.Windows.Media;

namespace LgController.UI;

/// <summary>Streaks of air fanning out below the unit; animated only while active and visible.</summary>
public sealed class AirflowVisual : FrameworkElement
{
    private const int StreakCount = 9;
    private const int DashesPerStreak = 2;
    private const double OriginSpacing = 13;
    private const double SpreadDegrees = 11;

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(AirflowVisual),
        new PropertyMetadata(false, (d, _) => ((AirflowVisual)d).UpdateTicking()));

    public static readonly DependencyProperty SpeedProperty = DependencyProperty.Register(
        nameof(Speed), typeof(double), typeof(AirflowVisual), new PropertyMetadata(1d));

    public static readonly DependencyProperty SwingProperty = DependencyProperty.Register(
        nameof(Swing), typeof(bool), typeof(AirflowVisual), new PropertyMetadata(false));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(AirflowVisual),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private TimeSpan _lastTick;
    private bool _ticking;
    private double _phase;
    private double _swingPhase;
    private double _intensity;
    private double _speed = 1;
    private double _swing;

    public AirflowVisual()
    {
        IsHitTestVisible = false;
        IsVisibleChanged += (_, _) => UpdateTicking();
    }

    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public double Speed { get => (double)GetValue(SpeedProperty); set => SetValue(SpeedProperty, value); }
    public bool Swing { get => (bool)GetValue(SwingProperty); set => SetValue(SwingProperty, value); }
    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    private void UpdateTicking()
    {
        // Keeps running after deactivation until the streaks have faded out.
        var shouldTick = IsVisible && (IsActive || _intensity > 0);
        if (shouldTick == _ticking) return;
        _ticking = shouldTick;
        _lastTick = TimeSpan.Zero;
        if (shouldTick) CompositionTarget.Rendering += OnRendering;
        else CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastTick) return;
        var elapsed = _lastTick == TimeSpan.Zero ? 0 : Math.Min((now - _lastTick).TotalSeconds, 0.1);
        _lastTick = now;

        // Eases toward the targets so speed and power changes never jump.
        var blend = Math.Min(1, elapsed * 3.5);
        _intensity += ((IsActive ? 1 : 0) - _intensity) * blend;
        _speed += (Speed - _speed) * blend;
        _swing += ((Swing ? 1 : 0) - _swing) * blend;
        _phase += elapsed * _speed * 0.5;
        _swingPhase += elapsed * 1.3;

        if (!IsActive && _intensity < 0.01)
        {
            _intensity = 0;
            UpdateTicking();
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Stroke is not { } stroke || _intensity <= 0) return;

        var pen = new Pen(stroke, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var reach = ActualHeight - 8;
        var spread = 1 + _swing * 0.45 * Math.Sin(_swingPhase);
        var middle = (StreakCount - 1) / 2.0;

        for (var streak = 0; streak < StreakCount; streak++)
        {
            var offset = streak - middle;
            var radians = offset * SpreadDegrees * spread * Math.PI / 180;
            var direction = new Vector(Math.Sin(radians), Math.Cos(radians));
            var origin = new Point(ActualWidth / 2 + offset * OriginSpacing, 0);

            for (var dash = 0; dash < DashesPerStreak; dash++)
            {
                var progress = _phase + streak * 0.37 + (double)dash / DashesPerStreak;
                progress -= Math.Floor(progress);
                var length = 9 + 9 * progress;
                var start = origin + direction * (4 + progress * (reach - length - 4));

                drawingContext.PushOpacity(Math.Sin(Math.PI * progress) * _intensity);
                drawingContext.DrawLine(pen, start, start + direction * length);
                drawingContext.Pop();
            }
        }
    }
}
