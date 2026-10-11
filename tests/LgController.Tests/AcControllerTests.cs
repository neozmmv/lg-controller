using LgController.Core;
using LgController.ThinQ;
using Microsoft.Extensions.Time.Testing;

namespace LgController.Tests;

public class AcControllerTests
{
    private readonly FakeThinQClient _client = new();
    private readonly FakeTimeProvider _time = new();
    private readonly AcController _controller;

    public AcControllerTests() => _controller = new AcController(_client, "dev-1", _time);

    private static string Temp(double value) =>
        $$$"""{"temperatureInUnits":{"targetTemperature":{{{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}}},"unit":"C"}}""";

    private static readonly ThinQException Rejected = new("2208", "failed", ThinQErrorKind.CommandRejected);

    [Fact]
    public async Task Refresh_LoadsCapabilitiesAndState()
    {
        var changes = 0;
        _controller.StateChanged += () => changes++;

        await _controller.RefreshAsync();

        Assert.Equal(23, _controller.State!.TargetTemperature);
        Assert.Equal(30, _controller.Capabilities!.MaxTemperature);
        Assert.Null(_controller.ConnectionError);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Refresh_Failure_SetsConnectionErrorAndKeepsLastState()
    {
        await _controller.RefreshAsync();
        _client.StateError = new ThinQException("network", "x", ThinQErrorKind.Network);

        await _controller.RefreshAsync();

        Assert.Equal(ThinQErrorKind.Network, _controller.ConnectionError!.Kind);
        Assert.Equal(23, _controller.State!.TargetTemperature);
    }

    [Fact]
    public void Commands_BeforeFirstRefresh_AreIgnored()
    {
        _controller.SetTargetTemperature(25);
        _time.Advance(AcController.DebounceDelay);

        Assert.Null(_controller.State);
        Assert.Empty(_client.Controls);
    }

    [Fact]
    public async Task SetTargetTemperature_UpdatesStateImmediately_AndSendsAfterDebounce()
    {
        await _controller.RefreshAsync();

        _controller.SetTargetTemperature(25);

        Assert.Equal(25, _controller.State!.TargetTemperature);
        Assert.Empty(_client.Controls);

        _time.Advance(AcController.DebounceDelay);

        Assert.Equal([Temp(25)], _client.Controls);
    }

    [Fact]
    public async Task RapidChanges_SendOnlyTheFinalValue()
    {
        await _controller.RefreshAsync();

        _controller.SetTargetTemperature(24);
        _time.Advance(TimeSpan.FromMilliseconds(300));
        _controller.SetTargetTemperature(25);
        _time.Advance(TimeSpan.FromMilliseconds(300));
        _controller.SetTargetTemperature(26);
        _time.Advance(AcController.DebounceDelay);

        Assert.Equal([Temp(26)], _client.Controls);
    }

    [Fact]
    public async Task DifferentFields_AreSentSeparately()
    {
        await _controller.RefreshAsync();

        _controller.SetTargetTemperature(25);
        _controller.SetWindStrength("HIGH");
        _time.Advance(AcController.DebounceDelay);

        Assert.Equal(2, _client.Controls.Count);
        Assert.Contains("""{"airFlow":{"windStrength":"HIGH"}}""", _client.Controls);
        Assert.Equal("HIGH", _controller.State!.WindStrength);
    }

    [Fact]
    public async Task FailedCommand_RestoresPreviousValue_AndRaisesError()
    {
        await _controller.RefreshAsync();
        ThinQException? raised = null;
        _controller.ErrorOccurred += ex => raised = ex;
        _client.ControlError = Rejected;

        _controller.SetTargetTemperature(25);
        _time.Advance(AcController.DebounceDelay);

        Assert.Equal(23, _controller.State!.TargetTemperature);
        Assert.Same(Rejected, raised);
    }

    [Fact]
    public async Task Poll_DoesNotOverwritePendingCommand()
    {
        await _controller.RefreshAsync();
        _controller.SetTargetTemperature(26);
        _client.StateJson = Samples.State.Replace("\"targetTemperature\": 23", "\"targetTemperature\": 20");

        await _controller.RefreshAsync();

        Assert.Equal(26, _controller.State!.TargetTemperature);
    }

    [Fact]
    public async Task SuccessfulCommand_KeepsValue_AndConfirmsAfterDelay()
    {
        await _controller.RefreshAsync();
        var callsBefore = _client.StateCalls;

        _controller.SetTargetTemperature(25);
        _time.Advance(AcController.DebounceDelay);

        Assert.Equal(25, _controller.State!.TargetTemperature);
        Assert.Equal(callsBefore, _client.StateCalls);

        _client.StateJson = Samples.State.Replace("\"targetTemperature\": 23", "\"targetTemperature\": 25");
        _time.Advance(AcController.ConfirmDelay);

        Assert.Equal(callsBefore + 1, _client.StateCalls);
        Assert.Equal(25, _controller.State!.TargetTemperature);
    }

    [Theory]
    [InlineData(99, 30)]
    [InlineData(-5, 16)]
    [InlineData(23.4, 23.5)]
    [InlineData(23.2, 23)]
    [InlineData(30.4, 30)]
    public async Task SetTargetTemperature_ClampsToRangeAndSnapsToStep(double requested, double expected)
    {
        await _controller.RefreshAsync();

        _controller.SetTargetTemperature(requested);
        _time.Advance(AcController.DebounceDelay);

        Assert.Equal(expected, _controller.State!.TargetTemperature);
        Assert.Equal([Temp(expected)], _client.Controls);
    }

    [Fact]
    public async Task OlderCommandFailing_DoesNotDiscardNewerValue()
    {
        await _controller.RefreshAsync();
        var gate = new TaskCompletionSource();
        _client.ControlGate = gate;

        _controller.SetTargetTemperature(24);
        _time.Advance(AcController.DebounceDelay);
        _controller.SetTargetTemperature(25);

        _client.ControlError = Rejected;
        gate.SetResult();

        Assert.Equal(25, _controller.State!.TargetTemperature);

        _client.ControlGate = null;
        _client.ControlError = null;
        _time.Advance(AcController.DebounceDelay);

        Assert.Equal([Temp(24), Temp(25)], _client.Controls);
        Assert.Equal(25, _controller.State!.TargetTemperature);
    }

    [Fact]
    public async Task Polling_UsesClosedIntervalThenOpenInterval()
    {
        await _controller.RefreshAsync();
        _controller.Start();
        var calls = _client.StateCalls;

        _time.Advance(AcController.ClosedPollInterval);
        Assert.Equal(calls + 1, _client.StateCalls);

        _controller.SetPanelOpen(true);
        Assert.Equal(calls + 2, _client.StateCalls);

        _time.Advance(AcController.OpenPollInterval);
        Assert.Equal(calls + 3, _client.StateCalls);
    }

    [Fact]
    public async Task RateLimit_PausesPollingForOneMinute()
    {
        await _controller.RefreshAsync();
        _controller.Start();
        _client.StateError = new ThinQException("1306", "limit", ThinQErrorKind.RateLimited);
        _controller.SetPanelOpen(true);
        var calls = _client.StateCalls;
        _client.StateError = null;

        _time.Advance(AcController.OpenPollInterval);
        Assert.Equal(calls, _client.StateCalls);

        _time.Advance(AcController.OpenPollInterval);
        Assert.Equal(calls + 1, _client.StateCalls);
        Assert.Null(_controller.ConnectionError);
    }

    [Fact]
    public async Task OtherSetters_ApplyOptimistically()
    {
        await _controller.RefreshAsync();

        _controller.SetPower(false);
        _controller.SetMode("FAN");
        _controller.SetSwingVertical(false);
        _controller.SetSwingHorizontal(true);
        _controller.SetPowerSave(true);
        _controller.SetStopTimer(0);

        var state = _controller.State!;
        Assert.False(state.IsOn);
        Assert.Equal("FAN", state.Mode);
        Assert.False(state.SwingVertical);
        Assert.True(state.SwingHorizontal);
        Assert.True(state.PowerSave);
        Assert.Null(state.StopTimerMinutes);

        _time.Advance(AcController.DebounceDelay);
        Assert.Equal(6, _client.Controls.Count);
    }

    [Fact]
    public async Task Dispose_CancelsPendingSends()
    {
        await _controller.RefreshAsync();
        _controller.SetTargetTemperature(25);

        _controller.Dispose();
        _time.Advance(AcController.DebounceDelay);

        Assert.Empty(_client.Controls);
    }
}
