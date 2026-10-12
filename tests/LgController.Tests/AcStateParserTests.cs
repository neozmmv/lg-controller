using LgController.Core;

namespace LgController.Tests;

public class AcStateParserTests
{
    private static readonly AcCapabilities Caps = AcProfileParser.Parse(Samples.Parse(Samples.Profile));

    [Fact]
    public void Parse_SampleState_ReadsAllFields()
    {
        var state = AcStateParser.Parse(Samples.Parse(Samples.State), Caps);

        Assert.True(state.IsOn);
        Assert.Equal("COOL", state.Mode);
        Assert.Equal(26.5, state.CurrentTemperature);
        Assert.Equal(23, state.TargetTemperature);
        Assert.Equal("MID", state.WindStrength);
        Assert.True(state.SwingVertical);
        Assert.False(state.SwingHorizontal);
        Assert.False(state.PowerSave);
        Assert.Equal(90, state.StopTimerMinutes);
        Assert.Null(state.StartTimerMinutes);
        Assert.Equal(120, state.SleepTimerMinutes);
        Assert.Equal(720, state.FilterUsedHours);
        Assert.Equal(0, state.FilterLifetimeHours);
    }

    [Fact]
    public void Parse_StartTimer_ReadsMinutes()
    {
        var state = AcStateParser.Parse(Samples.Parse("""
            {"timer":{"relativeHourToStart":2,"relativeMinuteToStart":15,"relativeStartTimer":"SET",
              "relativeStopTimer":"UNSET"}}
            """), Caps);

        Assert.Equal(135, state.StartTimerMinutes);
        Assert.Null(state.StopTimerMinutes);
    }

    [Fact]
    public void Parse_SleepTimerUnset_HasNoMinutes()
    {
        var state = AcStateParser.Parse(Samples.Parse("""{"sleepTimer":{"relativeStopTimer":"UNSET"}}"""), Caps);

        Assert.Null(state.SleepTimerMinutes);
    }

    [Theory]
    [InlineData("HIGH", "HIGH", "HIGH")]
    [InlineData("AUTO", "LOW", "AUTO")]
    [InlineData("LOW", "NATURE", "NATURE")]
    public void Parse_WindStrength_PrefersDetailOnlyForDetailOnlySpeeds(string strength, string detail, string expected)
    {
        var caps = Caps with { WindStrengthDetails = ["NATURE"] };

        var state = AcStateParser.Parse(Samples.Parse(
            $$$"""{"airFlow":{"windStrength":"{{{strength}}}","windStrengthDetail":"{{{detail}}}"}}"""), caps);

        Assert.Equal(expected, state.WindStrength);
    }

    [Fact]
    public void Parse_OnlyWindStrengthDetail_ReadsIt()
    {
        var caps = Caps with { WindStrengths = [], WindStrengthDetails = ["LOW", "POWER"] };

        var state = AcStateParser.Parse(Samples.Parse("""{"airFlow":{"windStrengthDetail":"POWER"}}"""), caps);

        Assert.Equal("POWER", state.WindStrength);
    }

    [Fact]
    public void Parse_LegacyTemperature_ReadsFromTemperatureResource()
    {
        var caps = Caps with { TemperatureResource = TemperatureResource.Legacy };

        var state = AcStateParser.Parse(Samples.Parse(Samples.State), caps);

        Assert.Equal(26, state.CurrentTemperature);
        Assert.Equal(23, state.TargetTemperature);
    }

    [Fact]
    public void Parse_TimerUnset_HasNoMinutes()
    {
        var state = AcStateParser.Parse(Samples.Parse("""
            {"timer":{"relativeHourToStop":1,"relativeMinuteToStop":30,"relativeStopTimer":"UNSET"}}
            """), Caps);

        Assert.Null(state.StopTimerMinutes);
    }

    [Fact]
    public void Parse_TimerWithoutFlag_UsesMinutesWhenPositive()
    {
        var set = AcStateParser.Parse(Samples.Parse("""{"timer":{"relativeHourToStop":0,"relativeMinuteToStop":45}}"""), Caps);
        var unset = AcStateParser.Parse(Samples.Parse("""{"timer":{"relativeHourToStop":0,"relativeMinuteToStop":0}}"""), Caps);

        Assert.Equal(45, set.StopTimerMinutes);
        Assert.Null(unset.StopTimerMinutes);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"operation":null,"temperatureInUnits":null,"airFlow":{},"windDirection":[]}""")]
    [InlineData("""{"operation":{"airConOperationMode":"POWER_OFF"},"temperatureInUnits":[{"unit":"C","targetTemperature":null}]}""")]
    public void Parse_MissingOrNullResources_ReturnsDefaults(string json)
    {
        var state = AcStateParser.Parse(Samples.Parse(json), Caps);

        Assert.False(state.IsOn);
        Assert.Null(state.TargetTemperature);
        Assert.Null(state.CurrentTemperature);
        Assert.Null(state.Mode);
        Assert.Null(state.WindStrength);
        Assert.False(state.SwingVertical);
        Assert.Null(state.StopTimerMinutes);
        Assert.Null(state.StartTimerMinutes);
        Assert.Null(state.SleepTimerMinutes);
        Assert.Null(state.FilterUsedHours);
    }
}
