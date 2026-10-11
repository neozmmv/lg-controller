using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LgController.UI;

public partial class FlyoutWindow : Window
{
    private readonly FlyoutViewModel _viewModel;
    private readonly FlyoutToggleGuard _guard = new(TimeProvider.System);
    private readonly SolidColorBrush _accent;

    public FlyoutWindow(FlyoutViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // Um único pincel compartilhado: animar a cor dele muda todos os controles de destaque.
        _accent = new SolidColorBrush(viewModel.AccentColor);
        Resources["AccentBrush"] = _accent;
        viewModel.PropertyChanged += OnViewModelChanged;

        Deactivated += (_, _) => HideFlyout();
        SizeChanged += (_, _) => Reposition();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) HideFlyout();
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

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        RootShift.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        OpenChanged?.Invoke(true);
    }

    private void HideFlyout()
    {
        if (!IsVisible) return;
        _guard.MarkClosed();
        Hide();
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
        if (e.PropertyName != nameof(FlyoutViewModel.AccentColor)) return;
        _accent.BeginAnimation(
            SolidColorBrush.ColorProperty,
            new ColorAnimation(_viewModel.AccentColor, TimeSpan.FromMilliseconds(250)));
    }
}
