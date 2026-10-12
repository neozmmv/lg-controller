using LgController.Core;
using LgController.ThinQ;
using LgController.UI;
using Microsoft.Extensions.Time.Testing;

namespace LgController.Tests;

public class FlyoutViewModelTests
{
    private readonly FakeThinQClient _client = new();
    private readonly FakeTimeProvider _time = new();

    private async Task<(FlyoutViewModel Vm, AcController Controller)> CreateAsync()
    {
        var controller = new AcController(_client, "dev-1", _time);
        var vm = new FlyoutViewModel(controller, "Bedroom", _time);
        await controller.RefreshAsync();
        return (vm, controller);
    }

    private static void Pick(FlyoutViewModel vm, TileItem tile, string value)
    {
        vm.ActivateTileCommand.Execute(tile);
        vm.SelectOptionCommand.Execute(tile.Options.Single(o => o.Value == value));
    }

    [Fact]
    public void BeforeFirstState_ControlsAreDisabled()
    {
        var vm = new FlyoutViewModel(new AcController(_client, "dev-1", _time), "Bedroom", _time);

        Assert.Equal("Bedroom", vm.DeviceName);
        Assert.False(vm.ControlsEnabled);
        Assert.False(vm.PowerEnabled);
        Assert.False(vm.AirflowActive);
        Assert.Equal("--", vm.TargetTemperatureText);
        Assert.Empty(vm.Tiles);
    }

    [Fact]
    public async Task State_IsReflected()
    {
        var (vm, _) = await CreateAsync();

        Assert.True(vm.IsOn);
        Assert.True(vm.ControlsEnabled);
        Assert.True(vm.PowerEnabled);
        Assert.Equal("23", vm.TargetTemperatureText);
        Assert.Equal("°C", vm.TemperatureUnitText);
        Assert.Equal("26.5°C", vm.RoomTemperatureText);
        Assert.Equal(23, vm.TargetTemperature);
        Assert.Equal(16, vm.MinTemperature);
        Assert.Equal(30, vm.MaxTemperature);
        Assert.Equal(0.5, vm.TemperatureStep);
        Assert.Equal("16°", vm.MinTemperatureText);
        Assert.Equal("30°", vm.MaxTemperatureText);
        Assert.False(vm.PowerSave);
    }

    [Fact]
    public async Task Tiles_ShowCurrentValues()
    {
        var (vm, _) = await CreateAsync();

        Assert.Equal("Cool", vm.ModeTile.Value);
        Assert.Equal(["Cool", "Dry", "Fan", "Auto"], vm.ModeTile.Options.Select(m => m.Label));
        Assert.Equal("COOL", vm.ModeTile.Options.Single(m => m.IsSelected).Value);
        Assert.Equal("Medium", vm.WindTile.Value);
        Assert.Equal(["Low", "Medium", "High", "Auto"], vm.WindTile.Options.Select(w => w.Label));
        Assert.Equal("MID", vm.WindTile.Options.Single(w => w.IsSelected).Value);
        Assert.Equal("On", vm.SwingVerticalTile.Value);
        Assert.True(vm.SwingVerticalTile.IsActive);
        Assert.Equal("1 h 30 min", vm.StopTimerTile.Value);
        Assert.True(vm.StopTimerTile.IsActive);
        Assert.Equal("Off", vm.StartTimerTile.Value);
        Assert.False(vm.StartTimerTile.IsActive);
        Assert.Equal("2 h", vm.SleepTile.Value);
        Assert.Equal("120", vm.SleepTile.Options.Single(o => o.IsSelected).Value);
    }

    [Fact]
    public async Task Tiles_ListOnlySupportedFeatures()
    {
        var (vm, _) = await CreateAsync();

        Assert.Equal(
            [vm.ModeTile, vm.WindTile, vm.SwingVerticalTile, vm.SleepTile, vm.StopTimerTile, vm.StartTimerTile],
            vm.Tiles);
        Assert.True(vm.ShowTemperature);
        Assert.True(vm.ShowPowerSave);
        Assert.Equal(["Off", "30 min", "1 h", "2 h", "4 h"], vm.StopTimerTile.Options.Select(t => t.Label));
        Assert.Equal(["Off", "30 min", "1 h", "2 h", "4 h"], vm.StartTimerTile.Options.Select(t => t.Label));
        Assert.Equal(["Off", "1 h", "2 h", "4 h", "8 h"], vm.SleepTile.Options.Select(t => t.Label));
    }

    [Fact]
    public async Task ProfileWithoutExtras_HasNoTilesForThem()
    {
        _client.ProfileJson = """
            {"property":{"airConJobMode":{"currentJobMode":{"type":"enum","mode":["r","w"],"value":{"w":["COOL"]}}}}}
            """;

        var (vm, _) = await CreateAsync();

        Assert.Equal([vm.ModeTile], vm.Tiles);
    }

    [Fact]
    public async Task WindTile_OffersDetailOnlySpeedsAfterTheBasicOnes()
    {
        _client.ProfileJson = Samples.Profile.Replace(
            "\"airFlow\": {",
            """
            "airFlow": {
              "windStrengthDetail": { "type": "enum", "mode": ["r", "w"],
                "value": { "r": ["NATURE", "LOW"], "w": ["NATURE", "LOW"] } },
            """);

        var (vm, controller) = await CreateAsync();
        Pick(vm, vm.WindTile, "NATURE");
        _time.Advance(AcController.DebounceDelay);

        Assert.Equal(["Low", "Medium", "High", "Auto", "Natural"], vm.WindTile.Options.Select(w => w.Label));
        Assert.Equal("Natural", vm.WindTile.Value);
        Assert.Equal(["""{"airFlow":{"windStrengthDetail":"NATURE"}}"""], _client.Controls);
    }

    [Fact]
    public async Task PowerOff_DisablesControlsButNotPowerButtonOrStartTimer()
    {
        _client.StateJson = Samples.State.Replace("POWER_ON", "POWER_OFF");

        var (vm, _) = await CreateAsync();

        Assert.False(vm.IsOn);
        Assert.False(vm.ControlsEnabled);
        Assert.False(vm.TemperatureEnabled);
        Assert.False(vm.AirflowActive);
        Assert.True(vm.PowerEnabled);
        Assert.False(vm.ModeTile.IsEnabled);
        Assert.False(vm.StopTimerTile.IsEnabled);
        Assert.True(vm.StartTimerTile.IsEnabled);
    }

    [Fact]
    public async Task PowerOn_DisablesStartTimer()
    {
        var (vm, _) = await CreateAsync();

        Assert.True(vm.ModeTile.IsEnabled);
        Assert.True(vm.AirflowActive);
        Assert.False(vm.StartTimerTile.IsEnabled);
    }

    [Fact]
    public async Task FanMode_DisablesTemperature()
    {
        _client.StateJson = Samples.State.Replace("\"COOL\"", "\"FAN\"");

        var (vm, _) = await CreateAsync();

        Assert.True(vm.ControlsEnabled);
        Assert.False(vm.TemperatureEnabled);
    }

    [Fact]
    public async Task IncreaseAndDecrease_UseProfileStep()
    {
        var (vm, controller) = await CreateAsync();

        vm.IncreaseTemperatureCommand.Execute(null);

        Assert.Equal(23.5, controller.State!.TargetTemperature);
        Assert.Equal("23.5", vm.TargetTemperatureText);

        vm.DecreaseTemperatureCommand.Execute(null);
        vm.DecreaseTemperatureCommand.Execute(null);

        Assert.Equal("22.5", vm.TargetTemperatureText);
    }

    [Fact]
    public async Task SettingTargetTemperature_SnapsToStepAndCallsController()
    {
        var (vm, controller) = await CreateAsync();

        vm.TargetTemperature = 24.3;

        Assert.Equal(24.5, controller.State!.TargetTemperature);
        Assert.Equal(24.5, vm.TargetTemperature);
        Assert.Equal("24.5", vm.TargetTemperatureText);
    }

    [Fact]
    public async Task ActivatingTileWithOptions_OpensTray_AndAgainClosesIt()
    {
        var (vm, _) = await CreateAsync();

        vm.ActivateTileCommand.Execute(vm.ModeTile);

        Assert.True(vm.IsTrayOpen);
        Assert.Same(vm.ModeTile, vm.TrayTile);

        vm.ActivateTileCommand.Execute(vm.ModeTile);

        Assert.False(vm.IsTrayOpen);
    }

    [Fact]
    public async Task SelectingOptions_CallsController_AndClosesTray()
    {
        var (vm, controller) = await CreateAsync();

        Pick(vm, vm.ModeTile, "AIR_DRY");
        Pick(vm, vm.WindTile, "HIGH");
        Pick(vm, vm.StopTimerTile, "120");
        Pick(vm, vm.SleepTile, "0");

        Assert.Equal("AIR_DRY", controller.State!.Mode);
        Assert.Equal("HIGH", controller.State.WindStrength);
        Assert.Equal(120, controller.State.StopTimerMinutes);
        Assert.Null(controller.State.SleepTimerMinutes);
        Assert.Equal("Dry", vm.ModeTile.Value);
        Assert.Equal("2 h", vm.StopTimerTile.Value);
        Assert.Equal("Off", vm.SleepTile.Value);
        Assert.False(vm.SleepTile.IsActive);
        Assert.False(vm.IsTrayOpen);
    }

    [Fact]
    public async Task StartTimer_CanBeSetWhileOff()
    {
        _client.StateJson = Samples.State.Replace("POWER_ON", "POWER_OFF");
        var (vm, controller) = await CreateAsync();

        Pick(vm, vm.StartTimerTile, "60");

        Assert.Equal(60, controller.State!.StartTimerMinutes);
        Assert.Equal("1 h", vm.StartTimerTile.Value);
    }

    [Fact]
    public async Task ActivatingSwingTile_TogglesSwingWithoutOpeningTray()
    {
        var (vm, controller) = await CreateAsync();

        vm.ActivateTileCommand.Execute(vm.SwingVerticalTile);

        Assert.False(controller.State!.SwingVertical);
        Assert.Equal("Off", vm.SwingVerticalTile.Value);
        Assert.False(vm.SwingVerticalTile.IsActive);
        Assert.False(vm.IsTrayOpen);
    }

    [Fact]
    public async Task DisabledTile_IgnoresActivation()
    {
        _client.StateJson = Samples.State.Replace("POWER_ON", "POWER_OFF");
        var (vm, controller) = await CreateAsync();

        vm.ActivateTileCommand.Execute(vm.ModeTile);
        vm.ActivateTileCommand.Execute(vm.SwingVerticalTile);

        Assert.False(vm.IsTrayOpen);
        Assert.True(controller.State!.SwingVertical);
    }

    [Fact]
    public async Task PowerOff_ClosesOpenTray()
    {
        var (vm, _) = await CreateAsync();
        vm.ActivateTileCommand.Execute(vm.ModeTile);

        vm.IsOn = false;

        Assert.False(vm.IsTrayOpen);
    }

    [Fact]
    public async Task TogglingBoundProperties_CallsController_ButSyncDoesNot()
    {
        var (vm, controller) = await CreateAsync();

        vm.PowerSave = true;
        vm.IsOn = false;
        _time.Advance(AcController.DebounceDelay);

        Assert.Equal(2, _client.Controls.Count);
        Assert.True(controller.State!.PowerSave);

        await controller.RefreshAsync();
        _time.Advance(AcController.DebounceDelay);

        Assert.Equal(2, _client.Controls.Count);
    }

    [Fact]
    public async Task AccentColor_FollowsMode()
    {
        var (vm, _) = await CreateAsync();
        var cool = vm.AccentColor;

        Pick(vm, vm.ModeTile, "AIR_DRY");

        Assert.NotEqual(cool, vm.AccentColor);
    }

    [Fact]
    public async Task Airflow_FollowsFanSpeedAndSwing()
    {
        var (vm, _) = await CreateAsync();
        var medium = vm.AirflowSpeed;

        Assert.True(vm.AirflowSwing);

        Pick(vm, vm.WindTile, "HIGH");
        vm.ActivateTileCommand.Execute(vm.SwingVerticalTile);

        Assert.True(vm.AirflowSpeed > medium);
        Assert.False(vm.AirflowSwing);
    }

    [Fact]
    public async Task FilterText_ShowsUsage()
    {
        var (vm, _) = await CreateAsync();

        Assert.True(vm.HasFilterText);
        Assert.Equal("Filter in use for 720 h", vm.FilterText);
    }

    [Fact]
    public async Task FilterText_WithLifetime_ShowsBoth()
    {
        _client.StateJson = Samples.State.Replace("\"filterLifetime\": 0", "\"filterLifetime\": 2000");

        var (vm, _) = await CreateAsync();

        Assert.Equal("Filter used 720 of 2000 h", vm.FilterText);
    }

    [Fact]
    public async Task FilterText_WithoutFilterInfo_IsHidden()
    {
        _client.StateJson = """{"operation":{"airConOperationMode":"POWER_ON"}}""";

        var (vm, _) = await CreateAsync();

        Assert.False(vm.HasFilterText);
    }

    [Fact]
    public async Task UnauthorizedError_ShowsBannerWithSetupButton()
    {
        var (vm, controller) = await CreateAsync();
        _client.StateError = new ThinQException("1103", "x", ThinQErrorKind.Unauthorized);

        await controller.RefreshAsync();

        Assert.True(vm.HasBanner);
        Assert.Equal("Invalid or expired token.", vm.BannerText);
        Assert.True(vm.ShowSetupButton);
    }

    [Fact]
    public async Task DeviceOffline_DisablesEverything()
    {
        var (vm, controller) = await CreateAsync();
        _client.StateError = new ThinQException("1222", "x", ThinQErrorKind.DeviceOffline);

        await controller.RefreshAsync();

        Assert.Equal("The device is offline.", vm.BannerText);
        Assert.False(vm.ShowSetupButton);
        Assert.False(vm.ControlsEnabled);
        Assert.False(vm.PowerEnabled);
        Assert.All(vm.Tiles, tile => Assert.False(tile.IsEnabled));
    }

    [Fact]
    public async Task RejectedCommand_ShowsToastThatExpires()
    {
        var (vm, _) = await CreateAsync();
        _client.ControlError = new ThinQException("2208", "x", ThinQErrorKind.CommandRejected);

        vm.IncreaseTemperatureCommand.Execute(null);
        _time.Advance(AcController.DebounceDelay);

        Assert.True(vm.HasToast);
        Assert.Equal("The device rejected the command.", vm.ToastText);
        Assert.Equal("23", vm.TargetTemperatureText);

        _time.Advance(FlyoutViewModel.ToastDuration);

        Assert.False(vm.HasToast);
    }

    [Fact]
    public async Task OpenSettingsCommand_RaisesEvent()
    {
        var (vm, _) = await CreateAsync();
        var raised = false;
        vm.SettingsRequested += () => raised = true;

        vm.OpenSettingsCommand.Execute(null);

        Assert.True(raised);
    }
}
