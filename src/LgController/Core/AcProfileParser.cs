using System.Text.Json;
using LgController.ThinQ;

namespace LgController.Core;

public static class AcProfileParser
{
    public static AcCapabilities Parse(JsonElement profile)
    {
        var property = profile.Prop("property");
        var airFlow = property.Prop("airFlow");
        var windKey = airFlow.Prop("windStrengthDetail").ValueKind == JsonValueKind.Object
            ? "windStrengthDetail"
            : "windStrength";
        var windDirection = property.Prop("windDirection");
        var timer = property.Prop("timer");

        var caps = new AcCapabilities
        {
            CanPower = WritableValues(property.Prop("operation").Prop("airConOperationMode")).Contains("POWER_ON"),
            Modes = WritableValues(property.Prop("airConJobMode").Prop("currentJobMode")),
            WindStrengthKey = windKey,
            WindStrengths = WritableValues(airFlow.Prop(windKey)),
            CanSwingVertical = IsWritable(windDirection.Prop("rotateUpDown")),
            CanSwingHorizontal = IsWritable(windDirection.Prop("rotateLeftRight")),
            CanPowerSave = IsWritable(property.Prop("powerSave").Prop("powerSaveEnabled")),
            CanStopTimer = IsWritable(timer.Prop("relativeHourToStop")),
            CanCancelStopTimer = WritableValues(timer.Prop("relativeStopTimer")).Contains("UNSET"),
        };

        var units = property.Prop("temperatureInUnits").Items();
        var entry = units.FirstOrDefault(u => u.Prop("unit").AsString() == "C");
        if (entry.ValueKind != JsonValueKind.Object && units.Count > 0) entry = units[0];

        if (TryRange(entry.Prop("targetTemperature"), out var range))
        {
            return caps with
            {
                TemperatureResource = TemperatureResource.InUnits,
                TemperatureUnit = entry.Prop("unit").AsString() ?? "C",
                MinTemperature = range.Min,
                MaxTemperature = range.Max,
                TemperatureStep = range.Step,
            };
        }

        if (TryRange(property.Prop("temperature").Prop("targetTemperature"), out range))
        {
            return caps with
            {
                TemperatureResource = TemperatureResource.Legacy,
                MinTemperature = range.Min,
                MaxTemperature = range.Max,
                TemperatureStep = range.Step,
            };
        }

        return caps;
    }

    private static bool IsWritable(JsonElement property) =>
        property.Prop("mode").Items().Any(m => m.AsString() == "w");

    private static IReadOnlyList<string> WritableValues(JsonElement property) =>
        IsWritable(property)
            ? property.Prop("value").Prop("w").Items().Select(v => v.AsString()).OfType<string>().ToList()
            : [];

    private static bool TryRange(JsonElement property, out (double Min, double Max, double Step) range)
    {
        range = default;
        if (!IsWritable(property)) return false;
        var writable = property.Prop("value").Prop("w");
        if (writable.Prop("min").AsDouble() is not { } min || writable.Prop("max").AsDouble() is not { } max) return false;
        if (max <= min) return false;
        var step = writable.Prop("step").AsDouble() is { } s && s > 0 ? s : 1;
        range = (min, max, step);
        return true;
    }
}
