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
    private static readonly int[] SleepMinutes = [60, 120, 240, 480];

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
    [ObservableProperty] private string _temperatureUnitText = "°C";
    [ObservableProperty] private string _minTemperatureText = "";
    [ObservableProperty] private string _maxTemperatureText = "";
    [ObservableProperty] private double _targetTemperature;
    [ObservableProperty] private double _minTemperature = 18;
    [ObservableProperty] private double _maxTemperature = 30;
    [ObservableProperty] private double _temperatureStep = 1;
    [ObservableProperty] private bool _showTemperature;
    [ObservableProperty] private bool _temperatureEnabled;
    [ObservableProperty] private bool _showPowerSave;
    [ObservableProperty] private bool _powerSave;
    [ObservableProperty] private bool _airflowActive;
    [ObservableProperty] private double _airflowSpeed = 1;
    [ObservableProperty] private bool _airflowSwing;
    [ObservableProperty] private bool _hasFilterText;
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private Color _accentColor = Color.FromRgb(0x6B, 0x72, 0x80);
    [ObservableProperty] private bool _hasBanner;
    [ObservableProperty] private string _bannerText = "";
    [ObservableProperty] private bool _showSetupButton;
    [ObservableProperty] private bool _hasToast;
    [ObservableProperty] private string _toastText = "";
    [ObservableProperty] private bool _isTrayOpen;
    /// <summary>The tile whose options the tray shows; kept after closing so the tray can animate out.</summary>
    [ObservableProperty] private TileItem? _trayTile;

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

    /// <summary>The tiles the device supports, in display order.</summary>
    public ObservableCollection<TileItem> Tiles { get; } = [];

    public TileItem ModeTile { get; } = new("Mode");
    public TileItem WindTile { get; } = new("Fan speed", icon: TileIcons.Fan);
    public TileItem SwingVerticalTile { get; } = new("Swing", "", hasOptions: false);
    public TileItem SwingHorizontalTile { get; } = new("Swing sideways", "", hasOptions: false);
    public TileItem SleepTile { get; } = new("Sleep", "");
    public TileItem StopTimerTile { get; } = new("Turn off in", "");
    public TileItem StartTimerTile { get; } = new("Turn on in", "");

    partial void OnIsOnChanged(bool value)
    {
        if (!_syncing) _controller.SetPower(value);
    }

    partial void OnPowerSaveChanged(bool value)
    {
        if (!_syncing) _controller.SetPowerSave(value);
    }

    partial void OnTargetTemperatureChanged(double value)
    {
        if (!_syncing) _controller.SetTargetTemperature(value);
    }

    [RelayCommand]
    private void IncreaseTemperature() => StepTemperature(+1);

    [RelayCommand]
    private void DecreaseTemperature() => StepTemperature(-1);

    [RelayCommand]
    private void ActivateTile(TileItem tile)
    {
        if (!tile.IsEnabled || _controller.State is not { } state) return;

        if (tile == SwingVerticalTile) _controller.SetSwingVertical(!state.SwingVertical);
        else if (tile == SwingHorizontalTile) _controller.SetSwingHorizontal(!state.SwingHorizontal);
        else if (IsTrayOpen && TrayTile == tile) IsTrayOpen = false;
        else
        {
            TrayTile = tile;
            IsTrayOpen = true;
        }
    }

    [RelayCommand]
    private void SelectOption(OptionItem option)
    {
        var tile = TrayTile;
        IsTrayOpen = false;

        if (tile == ModeTile) _controller.SetMode(option.Value);
        else if (tile == WindTile) _controller.SetWindStrength(option.Value);
        else if (tile == SleepTile) _controller.SetSleepTimer(int.Parse(option.Value));
        else if (tile == StopTimerTile) _controller.SetStopTimer(int.Parse(option.Value));
        else if (tile == StartTimerTile) _controller.SetStartTimer(int.Parse(option.Value));
    }

    [RelayCommand]
    private void CloseTray() => IsTrayOpen = false;

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
                AirflowActive = false;
                foreach (var tile in Tiles) tile.IsEnabled = false;
                IsTrayOpen = false;
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
            TemperatureStep = caps.TemperatureStep;
            TemperatureUnitText = $"°{caps.TemperatureUnit}";
            MinTemperatureText = $"{caps.MinTemperature.ToString("0.#", Culture)}°";
            MaxTemperatureText = $"{caps.MaxTemperature.ToString("0.#", Culture)}°";
            TargetTemperature = state.TargetTemperature ?? caps.MinTemperature;
            TargetTemperatureText = state.TargetTemperature is { } target ? target.ToString("0.#", Culture) : "--";
            RoomTemperatureText = state.CurrentTemperature is { } room
                ? $"{room.ToString("0.#", Culture)}{TemperatureUnitText}"
                : "";

            ShowPowerSave = caps.CanPowerSave;
            PowerSave = state.PowerSave;

            SyncTiles(caps, state, reachable);
            if (TrayTile is { IsEnabled: false }) IsTrayOpen = false;

            AirflowActive = ControlsEnabled;
            AirflowSpeed = SpeedFor(state.WindStrength);
            AirflowSwing = state.SwingVertical || state.SwingHorizontal;

            HasFilterText = state.FilterUsedHours is not null;
            FilterText = DescribeFilter(state.FilterUsedHours, state.FilterLifetimeHours);

            var (r, g, b) = ModePalette.For(state.Mode, state.IsOn);
            AccentColor = Color.FromRgb(r, g, b);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void SyncTiles(AcCapabilities caps, AcState state, bool reachable)
    {
        var winds = caps.AllWindStrengths;
        var supported = new List<TileItem>();
        if (caps.Modes.Count > 0) supported.Add(ModeTile);
        if (winds.Count > 0) supported.Add(WindTile);
        if (caps.CanSwingVertical) supported.Add(SwingVerticalTile);
        if (caps.CanSwingHorizontal) supported.Add(SwingHorizontalTile);
        if (caps.CanSleepTimer) supported.Add(SleepTile);
        if (caps.CanStopTimer) supported.Add(StopTimerTile);
        if (caps.CanStartTimer) supported.Add(StartTimerTile);
        if (!Tiles.SequenceEqual(supported))
        {
            Tiles.Clear();
            foreach (var tile in supported) Tiles.Add(tile);
        }

        foreach (var tile in Tiles) tile.IsEnabled = ControlsEnabled;
        // A turn-on timer only makes sense while the unit is off.
        StartTimerTile.IsEnabled = reachable && !state.IsOn;

        var mode = state.Mode is { } current ? OptionCatalog.Mode(current) : null;
        SyncOptions(ModeTile.Options, caps.Modes.Select(OptionCatalog.Mode), state.Mode);
        ModeTile.Value = mode?.Label ?? "";
        ModeTile.Glyph = mode?.Glyph ?? "";
        ModeTile.GlyphFont = mode?.GlyphFont ?? OptionCatalog.IconFont;
        ModeTile.IsActive = true;

        SyncOptions(WindTile.Options, winds.Select(OptionCatalog.Wind), state.WindStrength);
        WindTile.Value = state.WindStrength is { } wind ? OptionCatalog.Wind(wind).Label : "";

        SyncSwitch(SwingVerticalTile, state.SwingVertical);
        SyncSwitch(SwingHorizontalTile, state.SwingHorizontal);
        SyncTimer(SleepTile, SleepMinutes, caps.CanCancelSleepTimer, state.SleepTimerMinutes);
        SyncTimer(StopTimerTile, TimerMinutes, caps.CanCancelStopTimer, state.StopTimerMinutes);
        SyncTimer(StartTimerTile, TimerMinutes, caps.CanCancelStartTimer, state.StartTimerMinutes);
    }

    private static void SyncSwitch(TileItem tile, bool on)
    {
        tile.Value = on ? "On" : "Off";
        tile.IsActive = on;
    }

    private static void SyncTimer(TileItem tile, int[] choices, bool canCancel, int? minutes)
    {
        var options = canCancel ? choices.Prepend(0) : choices;
        SyncOptions(tile.Options, options.Select(OptionCatalog.Timer), (minutes ?? 0).ToString());
        tile.Value = DescribeTimer(minutes);
        tile.IsActive = minutes > 0;
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
        if (minutes is not { } total || total <= 0) return "Off";
        var hours = total / 60;
        var rest = total % 60;
        return hours > 0 && rest > 0 ? $"{hours} h {rest} min" : hours > 0 ? $"{hours} h" : $"{rest} min";
    }

    private static string DescribeFilter(double? usedHours, double? lifetimeHours)
    {
        if (usedHours is not { } used) return "";
        var usedText = used.ToString("0", Culture);
        return lifetimeHours is { } lifetime && lifetime > 0
            ? $"Filter used {usedText} of {lifetime.ToString("0", Culture)} h"
            : $"Filter in use for {usedText} h";
    }

    /// <summary>Relative speed of the airflow animation.</summary>
    private static double SpeedFor(string? windStrength) => windStrength switch
    {
        "SLOW" => 0.4,
        "LOW" => 0.6,
        "LOW_MID" => 0.8,
        "MID_HIGH" => 1.3,
        "HIGH" => 1.6,
        "POWER" => 2.1,
        _ => 1,
    };

    private void ShowToast(ThinQException error)
    {
        ToastText = ErrorMessages.For(error);
        HasToast = true;
        _toastTimer?.Dispose();
        _toastTimer = _time.CreateTimer(
            _ => _dispatch(() => HasToast = false), null, ToastDuration, Timeout.InfiniteTimeSpan);
    }
}
