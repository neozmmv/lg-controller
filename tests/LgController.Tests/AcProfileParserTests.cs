using LgController.Core;

namespace LgController.Tests;

public class AcProfileParserTests
{
    [Fact]
    public void Parse_SampleProfile_ReadsCapabilities()
    {
        var caps = AcProfileParser.Parse(Samples.Parse(Samples.Profile));

        Assert.True(caps.CanPower);
        Assert.Equal(["COOL", "AIR_DRY", "FAN", "AUTO"], caps.Modes);
        Assert.Equal(["LOW", "MID", "HIGH", "AUTO"], caps.WindStrengths);
        Assert.Equal("windStrength", caps.WindStrengthKey);
        Assert.True(caps.CanSwingVertical);
        Assert.False(caps.CanSwingHorizontal);
        Assert.True(caps.CanPowerSave);
        Assert.True(caps.CanStopTimer);
        Assert.True(caps.CanCancelStopTimer);
    }

    [Fact]
    public void Parse_PrefersCelsiusEntryOfTemperatureInUnits()
    {
        var caps = AcProfileParser.Parse(Samples.Parse(Samples.Profile));

        Assert.Equal(TemperatureResource.InUnits, caps.TemperatureResource);
        Assert.Equal("C", caps.TemperatureUnit);
        Assert.Equal(16, caps.MinTemperature);
        Assert.Equal(30, caps.MaxTemperature);
        Assert.Equal(0.5, caps.TemperatureStep);
    }

    [Fact]
    public void Parse_WithoutTemperatureInUnits_FallsBackToLegacy()
    {
        var caps = AcProfileParser.Parse(Samples.Parse("""
            {"property":{"temperature":{"targetTemperature":{"type":"range","mode":["r","w"],
              "value":{"w":{"max":30,"min":18}}}}}}
            """));

        Assert.Equal(TemperatureResource.Legacy, caps.TemperatureResource);
        Assert.Equal(18, caps.MinTemperature);
        Assert.Equal(30, caps.MaxTemperature);
        Assert.Equal(1, caps.TemperatureStep);
    }

    [Fact]
    public void Parse_PrefersWindStrengthDetailWhenPresent()
    {
        var caps = AcProfileParser.Parse(Samples.Parse("""
            {"property":{"airFlow":{
              "windStrength":{"type":"enum","mode":["r","w"],"value":{"w":["LOW","HIGH"]}},
              "windStrengthDetail":{"type":"enum","mode":["r","w"],"value":{"w":["NATURE","POWER"]}}}}}
            """));

        Assert.Equal("windStrengthDetail", caps.WindStrengthKey);
        Assert.Equal(["NATURE", "POWER"], caps.WindStrengths);
    }

    [Fact]
    public void Parse_ReadOnlyProperty_IsNotACapability()
    {
        var caps = AcProfileParser.Parse(Samples.Parse("""
            {"property":{
              "airConJobMode":{"currentJobMode":{"type":"enum","mode":["r"],"value":{"r":["COOL"]}}},
              "powerSave":{"powerSaveEnabled":{"type":"boolean","mode":["r"],"value":{"r":[true,false]}}}}}
            """));

        Assert.Empty(caps.Modes);
        Assert.False(caps.CanPowerSave);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"property":[]}""")]
    [InlineData("""{"property":{"temperatureInUnits":{},"airFlow":null,"timer":7}}""")]
    public void Parse_MalformedProfile_ReturnsEmptyCapabilities(string json)
    {
        var caps = AcProfileParser.Parse(Samples.Parse(json));

        Assert.False(caps.CanPower);
        Assert.Empty(caps.Modes);
        Assert.Equal(TemperatureResource.None, caps.TemperatureResource);
    }
}
