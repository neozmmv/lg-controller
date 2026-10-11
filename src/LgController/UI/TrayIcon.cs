using System.Globalization;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using LgController.Core;

namespace LgController.UI;

public sealed class TrayIcon : IDisposable
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private readonly TaskbarIcon _icon = new();
    private readonly MenuItem _powerItem;
    private System.Drawing.Icon? _current;
    private string? _renderKey;

    public TrayIcon(Action toggleFlyout, Action togglePower, Action openSettings, Action exit)
    {
        _powerItem = new MenuItem { Header = "Turn on" };
        _powerItem.Click += (_, _) => togglePower();

        var autoStart = new MenuItem { Header = "Start with Windows", IsCheckable = true, IsChecked = AutoStart.IsEnabled };
        autoStart.Click += (_, _) => AutoStart.Set(autoStart.IsChecked);

        var settings = new MenuItem { Header = "Settings" };
        settings.Click += (_, _) => openSettings();

        var quit = new MenuItem { Header = "Exit" };
        quit.Click += (_, _) => exit();

        _icon.ContextMenu = new ContextMenu { Items = { _powerItem, autoStart, settings, new Separator(), quit } };
        _icon.LeftClickCommand = new RelayCommand(toggleFlyout);
        _icon.NoLeftClickDelay = true;
        Update(null, hasError: false, "LG Controller");
        _icon.ForceCreate(false);
    }

    public void Update(AcState? state, bool hasError, string alias)
    {
        var isOn = state?.IsOn ?? false;
        var text = isOn && state!.TargetTemperature is { } target
            ? Math.Round(target).ToString("0", Culture)
            : null;
        var color = ModePalette.For(state?.Mode, isOn);
        var key = $"{text}|{color}|{hasError}";
        if (key != _renderKey)
        {
            _renderKey = key;
            var previous = _current;
            _current = TrayIconRenderer.Render(text, color, hasError);
            _icon.Icon = _current;
            previous?.Dispose();
        }

        _powerItem.Header = isOn ? "Turn off" : "Turn on";
        _powerItem.IsEnabled = state is not null;
        _icon.ToolTipText = Describe(state, hasError, alias);
    }

    public void Dispose()
    {
        _icon.Dispose();
        _current?.Dispose();
    }

    private static string Describe(AcState? state, bool hasError, string alias)
    {
        if (state is null) return hasError ? $"{alias}: no connection" : $"{alias}: connecting…";
        var status = state.IsOn ? "On" : "Off";
        var target = state.IsOn && state.TargetTemperature is { } t ? $", {t.ToString("0.#", Culture)}°" : "";
        var room = state.CurrentTemperature is { } c ? $" (room {c.ToString("0.#", Culture)}°)" : "";
        var warning = hasError ? " · no connection" : "";
        return $"{alias}: {status}{target}{room}{warning}";
    }
}
