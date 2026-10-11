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
    }
}
