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
        var vm = new FlyoutViewModel(controller, "Quarto", _time);
        await controller.RefreshAsync();
        return (vm, controller);
    }

    [Fact]
    public void BeforeFirstState_ControlsAreDisabled()
    {
        var vm = new FlyoutViewModel(new AcController(_client, "dev-1", _time), "Quarto", _time);

        Assert.Equal("Quarto", vm.DeviceName);
        Assert.False(vm.ControlsEnabled);
        Assert.False(vm.PowerEnabled);
        Assert.Equal("--", vm.TargetTemperatureText);
    }

    [Fact]
    public async Task State_IsReflected()
    {
        var (vm, _) = await CreateAsync();

        Assert.True(vm.IsOn);
        Assert.True(vm.ControlsEnabled);
        Assert.True(vm.PowerEnabled);
        Assert.Equal("23°", vm.TargetTemperatureText);
        Assert.Equal("Ambiente 26,5°", vm.RoomTemperatureText);
        Assert.Equal(23, vm.TargetTemperature);
        Assert.Equal(16, vm.MinTemperature);
        Assert.Equal(30, vm.MaxTemperature);
        Assert.Equal(["Frio", "Secar", "Ventilar", "Auto"], vm.Modes.Select(m => m.Label));
        Assert.Equal("COOL", vm.Modes.Single(m => m.IsSelected).Value);
        Assert.Equal(["Baixa", "Média", "Alta", "Auto"], vm.WindStrengths.Select(w => w.Label));
        Assert.Equal("MID", vm.WindStrengths.Single(w => w.IsSelected).Value);
        Assert.True(vm.SwingVertical);
        Assert.Equal("Desliga em 1 h 30 min", vm.TimerText);
    }

    [Fact]
    public async Task UnsupportedFeatures_AreHidden()
    {
        var (vm, _) = await CreateAsync();

        Assert.True(vm.ShowTemperature);
        Assert.True(vm.ShowSwingVertical);
        Assert.False(vm.ShowSwingHorizontal);
        Assert.True(vm.ShowPowerSave);
        Assert.True(vm.ShowTimer);
        Assert.Equal(["Desativado", "30 min", "1 h", "2 h", "4 h"], vm.TimerOptions.Select(t => t.Label));
    }

    [Fact]
    public async Task PowerOff_DisablesControlsButNotPowerButton()
    {
        _client.StateJson = Samples.State.Replace("POWER_ON", "POWER_OFF");

        var (vm, _) = await CreateAsync();

        Assert.False(vm.IsOn);
        Assert.False(vm.ControlsEnabled);
        Assert.False(vm.TemperatureEnabled);
        Assert.True(vm.PowerEnabled);
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
        Assert.Equal("23,5°", vm.TargetTemperatureText);

        vm.DecreaseTemperatureCommand.Execute(null);
        vm.DecreaseTemperatureCommand.Execute(null);

        Assert.Equal("22,5°", vm.TargetTemperatureText);
    }

    [Fact]
    public async Task SelectingOptions_CallsController()
    {
        var (vm, controller) = await CreateAsync();

        vm.SelectModeCommand.Execute(vm.Modes.Single(m => m.Value == "AIR_DRY"));
        vm.SelectWindCommand.Execute(vm.WindStrengths.Single(w => w.Value == "HIGH"));
        vm.SelectTimerCommand.Execute(vm.TimerOptions.Single(t => t.Value == "120"));

        Assert.Equal("AIR_DRY", controller.State!.Mode);
        Assert.Equal("HIGH", controller.State.WindStrength);
        Assert.Equal(120, controller.State.StopTimerMinutes);
        Assert.Equal("AIR_DRY", vm.Modes.Single(m => m.IsSelected).Value);
        Assert.Equal("120", vm.TimerOptions.Single(t => t.IsSelected).Value);
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

        vm.SelectModeCommand.Execute(vm.Modes.Single(m => m.Value == "AIR_DRY"));

        Assert.NotEqual(cool, vm.AccentColor);
    }

    [Fact]
    public async Task UnauthorizedError_ShowsBannerWithSetupButton()
    {
        var (vm, controller) = await CreateAsync();
        _client.StateError = new ThinQException("1103", "x", ThinQErrorKind.Unauthorized);

        await controller.RefreshAsync();

        Assert.True(vm.HasBanner);
        Assert.Equal("Token inválido ou expirado.", vm.BannerText);
        Assert.True(vm.ShowSetupButton);
    }

    [Fact]
    public async Task DeviceOffline_DisablesEverything()
    {
        var (vm, controller) = await CreateAsync();
        _client.StateError = new ThinQException("1222", "x", ThinQErrorKind.DeviceOffline);

        await controller.RefreshAsync();

        Assert.Equal("O aparelho está offline.", vm.BannerText);
        Assert.False(vm.ShowSetupButton);
        Assert.False(vm.ControlsEnabled);
        Assert.False(vm.PowerEnabled);
    }

    [Fact]
    public async Task RejectedCommand_ShowsToastThatExpires()
    {
        var (vm, _) = await CreateAsync();
        _client.ControlError = new ThinQException("2208", "x", ThinQErrorKind.CommandRejected);

        vm.IncreaseTemperatureCommand.Execute(null);
        _time.Advance(AcController.DebounceDelay);

        Assert.True(vm.HasToast);
        Assert.Equal("O aparelho recusou o comando.", vm.ToastText);
        Assert.Equal("23°", vm.TargetTemperatureText);

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
