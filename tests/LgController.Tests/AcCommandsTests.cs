using LgController.Core;

namespace LgController.Tests;

public class AcCommandsTests
{
    private static readonly AcCapabilities Caps = AcProfileParser.Parse(Samples.Parse(Samples.Profile));

    [Fact]
    public void Power() =>
        Assert.Equal("""{"operation":{"airConOperationMode":"POWER_ON"}}""", AcCommands.Power(true).ToJsonString());

    [Fact]
    public void PowerOff() =>
        Assert.Equal("""{"operation":{"airConOperationMode":"POWER_OFF"}}""", AcCommands.Power(false).ToJsonString());

    [Fact]
    public void Mode() =>
        Assert.Equal("""{"airConJobMode":{"currentJobMode":"FAN"}}""", AcCommands.Mode("FAN").ToJsonString());

    [Fact]
    public void Temperature_InUnits() =>
        Assert.Equal(
            """{"temperatureInUnits":{"targetTemperature":23,"unit":"C"}}""",
            AcCommands.Temperature(Caps, 23).ToJsonString());

    [Fact]
    public void Temperature_HalfDegree() =>
        Assert.Equal(
            """{"temperatureInUnits":{"targetTemperature":23.5,"unit":"C"}}""",
            AcCommands.Temperature(Caps, 23.5).ToJsonString());

    [Fact]
    public void Temperature_Legacy() =>
        Assert.Equal(
            """{"temperature":{"targetTemperature":23}}""",
            AcCommands.Temperature(Caps with { TemperatureResource = TemperatureResource.Legacy }, 23).ToJsonString());

    [Fact]
    public void WindStrength() =>
        Assert.Equal("""{"airFlow":{"windStrength":"AUTO"}}""", AcCommands.WindStrength(Caps, "AUTO").ToJsonString());

    [Fact]
    public void WindStrength_DetailOnlySpeed_UsesDetailKey() =>
        Assert.Equal(
            """{"airFlow":{"windStrengthDetail":"NATURE"}}""",
            AcCommands.WindStrength(Caps with { WindStrengthDetails = ["NATURE"] }, "NATURE").ToJsonString());

    [Fact]
    public void Swings()
    {
        Assert.Equal("""{"windDirection":{"rotateUpDown":true}}""", AcCommands.SwingVertical(true).ToJsonString());
        Assert.Equal("""{"windDirection":{"rotateLeftRight":false}}""", AcCommands.SwingHorizontal(false).ToJsonString());
    }

    [Fact]
    public void PowerSave() =>
        Assert.Equal("""{"powerSave":{"powerSaveEnabled":true}}""", AcCommands.PowerSave(true).ToJsonString());

    [Theory]
    [InlineData(30, """{"timer":{"relativeHourToStop":0,"relativeMinuteToStop":30}}""")]
    [InlineData(120, """{"timer":{"relativeHourToStop":2}}""")]
    [InlineData(90, """{"timer":{"relativeHourToStop":1,"relativeMinuteToStop":30}}""")]
    [InlineData(0, """{"timer":{"relativeStopTimer":"UNSET"}}""")]
    public void StopTimer(int minutes, string expected) =>
        Assert.Equal(expected, AcCommands.StopTimer(minutes).ToJsonString());

    [Theory]
    [InlineData(30, """{"timer":{"relativeHourToStart":0,"relativeMinuteToStart":30}}""")]
    [InlineData(240, """{"timer":{"relativeHourToStart":4}}""")]
    [InlineData(0, """{"timer":{"relativeStartTimer":"UNSET"}}""")]
    public void StartTimer(int minutes, string expected) =>
        Assert.Equal(expected, AcCommands.StartTimer(minutes).ToJsonString());

    [Theory]
    [InlineData(60, """{"sleepTimer":{"relativeHourToStop":1}}""")]
    [InlineData(90, """{"sleepTimer":{"relativeHourToStop":1,"relativeMinuteToStop":30}}""")]
    [InlineData(0, """{"sleepTimer":{"relativeStopTimer":"UNSET"}}""")]
    public void SleepTimer(int minutes, string expected) =>
        Assert.Equal(expected, AcCommands.SleepTimer(minutes).ToJsonString());
}
