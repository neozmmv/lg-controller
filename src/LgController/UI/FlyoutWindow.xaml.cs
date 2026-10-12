using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LgController.UI;

public partial class FlyoutWindow : Window
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    private readonly FlyoutViewModel _viewModel;
    private readonly FlyoutToggleGuard _guard = new(TimeProvider.System);
    private readonly SolidColorBrush _accent;
    private readonly FrameworkElement[] _sections;
    private double _shownTemperature;

    public FlyoutWindow(FlyoutViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _shownTemperature = viewModel.TargetTemperature;

        // A single shared brush: animating its color changes every accent control.
        _accent = new SolidColorBrush(viewModel.AccentColor);
        Resources["AccentBrush"] = _accent;
        viewModel.PropertyChanged += OnViewModelChanged;

        _sections = [Hero, TempRow, SliderRow, TileArea];
        foreach (var section in _sections) section.RenderTransform = new TranslateTransform();

        Deactivated += (_, _) => HideFlyout();
        SizeChanged += (_, _) => Reposition();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (_viewModel.IsTrayOpen) _viewModel.CloseTrayCommand.Execute(null);
            else HideFlyout();
        };
        ThemeManager.Changed += ApplyBackdrop;
    }

    public event Action<bool>? OpenChanged;

    public void Toggle()
    {
        if (IsVisible) HideFlyout();
        else if (_guard.CanOpen()) ShowFlyout();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Backdrop.HideFromAltTab(this);
        ApplyBackdrop();
    }

    protected override void OnClosed(EventArgs e)
    {
        ThemeManager.Changed -= ApplyBackdrop;
        _viewModel.PropertyChanged -= OnViewModelChanged;
        base.OnClosed(e);
    }

    private void ApplyBackdrop()
    {
        var hasBackdrop = Backdrop.Apply(this, ThemeManager.IsDark);
        Root.SetResourceReference(
            System.Windows.Controls.Border.BackgroundProperty, hasBackdrop ? "PanelTintBrush" : "PanelSolidBrush");
    }

    private void ShowFlyout()
    {
        Root.Opacity = 0;
        Show();
        UpdateLayout();
        Reposition();
        Activate();

        Root.BeginAnimation(OpacityProperty, Glide(0, 1, 160));
        RootShift.BeginAnimation(TranslateTransform.YProperty, Glide(22, 0, 280));
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, Glide(0.96, 1, 280));
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, Glide(0.96, 1, 280));

        // The sections settle one after another.
        for (var i = 0; i < _sections.Length; i++)
        {
            var shift = (TranslateTransform)_sections[i].RenderTransform;
            var rise = Glide(18, 0, 380);
            rise.BeginTime = TimeSpan.FromMilliseconds(45 * (i + 1));
            // Shown until the delayed animation starts.
            shift.Y = 18;
            shift.BeginAnimation(TranslateTransform.YProperty, rise);
        }

        OpenChanged?.Invoke(true);
    }

    private void HideFlyout()
    {
        if (!IsVisible) return;
        _guard.MarkClosed();
        Hide();
        _viewModel.CloseTrayCommand.Execute(null);
        OpenChanged?.Invoke(false);
    }

    private void Reposition()
    {
        if (!IsVisible) return;
        var position = FlyoutPlacement.Compute(SystemParameters.WorkArea, new Size(ActualWidth, ActualHeight));
        Left = position.X;
        Top = position.Y;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FlyoutViewModel.AccentColor):
                _accent.BeginAnimation(
                    SolidColorBrush.ColorProperty,
                    new ColorAnimation(_viewModel.AccentColor, TimeSpan.FromMilliseconds(350)));
                break;
            case nameof(FlyoutViewModel.TargetTemperatureText):
                NudgeTemperature();
                break;
            case nameof(FlyoutViewModel.IsTrayOpen):
                SwapTray(_viewModel.IsTrayOpen);
                break;
        }
    }

    /// <summary>The new number slides in from the direction the temperature moved.</summary>
    private void NudgeTemperature()
    {
        var direction = Math.Sign(_viewModel.TargetTemperature - _shownTemperature);
        _shownTemperature = _viewModel.TargetTemperature;
        if (direction == 0 || !IsVisible) return;

        TempShift.BeginAnimation(TranslateTransform.YProperty, Glide(direction * 12, 0, 240));
        TempNumber.BeginAnimation(OpacityProperty, Glide(0.15, 1, 240));
    }

    private void SwapTray(bool open)
    {
        // Snaps while hidden so the panel never reopens mid-transition.
        var milliseconds = IsVisible ? 220 : 0;
        Tray.IsHitTestVisible = open;
        TileGrid.IsHitTestVisible = !open;

        var tileScale = open ? 0.95 : 1;
        TileGrid.BeginAnimation(OpacityProperty, Glide(null, open ? 0 : 1, milliseconds));
        TileGridScale.BeginAnimation(ScaleTransform.ScaleXProperty, Glide(null, tileScale, milliseconds));
        TileGridScale.BeginAnimation(ScaleTransform.ScaleYProperty, Glide(null, tileScale, milliseconds));
        Tray.BeginAnimation(OpacityProperty, Glide(null, open ? 1 : 0, milliseconds));
        TrayShift.BeginAnimation(TranslateTransform.YProperty, Glide(null, open ? 0 : 14, milliseconds));
    }

    private static DoubleAnimation Glide(double? from, double to, int milliseconds) =>
        new() { From = from, To = to, Duration = TimeSpan.FromMilliseconds(milliseconds), EasingFunction = EaseOut };
}
