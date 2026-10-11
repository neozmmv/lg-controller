using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LgController.Core;
using LgController.ThinQ;

namespace LgController.UI;

public sealed partial class FlyoutViewModel : ObservableObject
{
    public static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(4);

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly int[] TimerMinutes = [30, 60, 120, 240];

    private readonly AcController _controller;
    private readonly TimeProvider _time;
    private readonly Action<Action> _dispatch;
    private ITimer? _toastTimer;
    private bool _syncing;

    [ObservableProperty] private string _deviceName;
    [ObservableProperty] private bool _isOn;
    [ObservableProperty] private bool _powerEnabled;
    [ObservableProperty] private bool _controlsEnabled;
    [ObservableProperty] private string _roomTemperatureText = "";
    [ObservableProperty] private string _targetTemperatureText = "--";
    [ObservableProperty] private string _modeText = "";
    [ObservableProperty] private double _targetTemperature;
    [ObservableProperty] private double _minTemperature = 18;
    [ObservableProperty] private double _maxTemperature = 30;
    [ObservableProperty] private bool _showTemperature;
    [ObservableProperty] private bool _temperatureEnabled;
    [ObservableProperty] private bool _showModes;
    [ObservableProperty] private bool _showWind;
    [ObservableProperty] private bool _showSwingVertical;
    [ObservableProperty] private bool _showSwingHorizontal;
    [ObservableProperty] private bool _showPowerSave;
    [ObservableProperty] private bool _showTimer;
    [ObservableProperty] private bool _swingVertical;
    [ObservableProperty] private bool _swingHorizontal;
    [ObservableProperty] private bool _powerSave;
    [ObservableProperty] private string _timerText = "";
    [ObservableProperty] private Color _accentColor = Color.FromRgb(0x6B, 0x72, 0x80);
    [ObservableProperty] private bool _hasBanner;
    [ObservableProperty] private string _bannerText = "";
    [ObservableProperty] private bool _showSetupButton;
    [ObservableProperty] private bool _hasToast;
    [ObservableProperty] private string _toastText = "";

    public FlyoutViewModel(AcController controller, string alias, TimeProvider time, Action<Action>? dispatch = null)
    {
        _controller = controller;
        _time = time;
        _dispatch = dispatch ?? (action => action());
        DeviceName = alias;
        controller.StateChanged += Sync;
        controller.ErrorOccurred += ShowToast;
        Sync();
    }

    public event Action? SettingsRequested;

    public ObservableCollection<OptionItem> Modes { get; } = [];
    public ObservableCollection<OptionItem> WindStrengths { get; } = [];
    public ObservableCollection<OptionItem> TimerOptions { get; } = [];

    partial void OnIsOnChanged(bool value)
    {
        if (!_syncing) _controller.SetPower(value);
    }

    partial void OnSwingVerticalChanged(bool value)
    {
        if (!_syncing) _controller.SetSwingVertical(value);
    }

    partial void OnSwingHorizontalChanged(bool value)
    {
        if (!_syncing) _controller.SetSwingHorizontal(value);
    }

    partial void OnPowerSaveChanged(bool value)
    {
        if (!_syncing) _controller.SetPowerSave(value);
    }

    [RelayCommand]
    private void IncreaseTemperature() => StepTemperature(+1);

    [RelayCommand]
    private void DecreaseTemperature() => StepTemperature(-1);

    [RelayCommand]
    private void SelectMode(OptionItem option) => _controller.SetMode(option.Value);

    [RelayCommand]
    private void SelectWind(OptionItem option) => _controller.SetWindStrength(option.Value);

    [RelayCommand]
    private void SelectTimer(OptionItem option) => _controller.SetStopTimer(int.Parse(option.Value));

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke();

    private void StepTemperature(int direction)
    {
        if (_controller.Capabilities is not { } caps || _controller.State?.TargetTemperature is not { } current) return;
        _controller.SetTargetTemperature(current + direction * caps.TemperatureStep);
    }

    private void Sync()
    {
        _syncing = true;
        try
        {
            var error = _controller.ConnectionError;
            HasBanner = error is not null;
            BannerText = error is null ? "" : ErrorMessages.For(error);
            ShowSetupButton = error?.Kind == ThinQErrorKind.Unauthorized;

            if (_controller.Capabilities is not { } caps || _controller.State is not { } state)
            {
                PowerEnabled = false;
                ControlsEnabled = false;
                TemperatureEnabled = false;
                return;
            }

            var reachable = error?.Kind != ThinQErrorKind.DeviceOffline;
            IsOn = state.IsOn;
            PowerEnabled = caps.CanPower && reachable;
            ControlsEnabled = state.IsOn && reachable;

            ShowTemperature = caps.TemperatureResource != TemperatureResource.None;
            TemperatureEnabled = ControlsEnabled && state.Mode != "FAN";
            MinTemperature = caps.MinTemperature;
            MaxTemperature = caps.MaxTemperature;
            TargetTemperature = state.TargetTemperature ?? caps.MinTemperature;
            TargetTemperatureText = state.TargetTemperature is { } target ? $"{target.ToString("0.#", Culture)}°" : "--";
            RoomTemperatureText = state.CurrentTemperature is { } room ? $"Room {room.ToString("0.#", Culture)}°" : "";
            ModeText = state.Mode is { } mode ? OptionCatalog.Mode(mode).Label : "";

            SyncOptions(Modes, caps.Modes.Select(OptionCatalog.Mode), state.Mode);
            SyncOptions(WindStrengths, caps.WindStrengths.Select(OptionCatalog.Wind), state.WindStrength);
            ShowModes = Modes.Count > 0;
            ShowWind = WindStrengths.Count > 0;

            ShowSwingVertical = caps.CanSwingVertical;
            ShowSwingHorizontal = caps.CanSwingHorizontal;
            ShowPowerSave = caps.CanPowerSave;
            SwingVertical = state.SwingVertical;
            SwingHorizontal = state.SwingHorizontal;
            PowerSave = state.PowerSave;

            ShowTimer = caps.CanStopTimer;
            var minutes = caps.CanCancelStopTimer ? TimerMinutes.Prepend(0) : TimerMinutes;
            SyncOptions(TimerOptions, minutes.Select(OptionCatalog.Timer), (state.StopTimerMinutes ?? 0).ToString());
            TimerText = DescribeTimer(state.StopTimerMinutes);

            var (r, g, b) = ModePalette.For(state.Mode, state.IsOn);
            AccentColor = Color.FromRgb(r, g, b);
        }
        finally
        {
            _syncing = false;
        }
    }

    private static void SyncOptions(ObservableCollection<OptionItem> target, IEnumerable<OptionItem> source, string? selected)
    {
        var items = source.ToList();
        if (!target.Select(o => o.Value).SequenceEqual(items.Select(o => o.Value)))
        {
            target.Clear();
            foreach (var item in items) target.Add(item);
        }

        foreach (var item in target) item.IsSelected = item.Value == selected;
    }

    private static string DescribeTimer(int? minutes)
    {
        if (minutes is not { } total || total <= 0) return "";
        var hours = total / 60;
        var rest = total % 60;
        var text = hours > 0 && rest > 0 ? $"{hours} h {rest} min" : hours > 0 ? $"{hours} h" : $"{rest} min";
        return $"Turns off in {text}";
    }

    private void ShowToast(ThinQException error)
    {
        ToastText = ErrorMessages.For(error);
        HasToast = true;
        _toastTimer?.Dispose();
        _toastTimer = _time.CreateTimer(
            _ => _dispatch(() => HasToast = false), null, ToastDuration, Timeout.InfiniteTimeSpan);
    }
}
