using System.Text.Json.Nodes;

namespace LgController.Core;

/// <summary>Builds the control POST body for each action.</summary>
public static class AcCommands
{
    public static JsonObject Power(bool on) =>
        Wrap("operation", "airConOperationMode", on ? "POWER_ON" : "POWER_OFF");

    public static JsonObject Mode(string mode) => Wrap("airConJobMode", "currentJobMode", mode);

    public static JsonObject Temperature(AcCapabilities caps, double value) =>
        caps.TemperatureResource == TemperatureResource.InUnits
            ? new JsonObject
            {
                ["temperatureInUnits"] = new JsonObject { ["targetTemperature"] = value, ["unit"] = caps.TemperatureUnit },
            }
            : Wrap("temperature", "targetTemperature", value);

    public static JsonObject WindStrength(AcCapabilities caps, string strength) =>
        Wrap("airFlow", caps.WindStrengthDetails.Contains(strength) ? "windStrengthDetail" : "windStrength", strength);

    public static JsonObject SwingVertical(bool on) => Wrap("windDirection", "rotateUpDown", on);

    public static JsonObject SwingHorizontal(bool on) => Wrap("windDirection", "rotateLeftRight", on);

    public static JsonObject PowerSave(bool on) => Wrap("powerSave", "powerSaveEnabled", on);

    /// <summary>Zero or less cancels the timer.</summary>
    public static JsonObject StopTimer(int minutes) => Timer("timer", "Stop", minutes);

    /// <summary>Zero or less cancels the timer.</summary>
    public static JsonObject StartTimer(int minutes) => Timer("timer", "Start", minutes);

    /// <summary>Zero or less cancels the timer.</summary>
    public static JsonObject SleepTimer(int minutes) => Timer("sleepTimer", "Stop", minutes);

    private static JsonObject Timer(string resource, string action, int minutes)
    {
        if (minutes <= 0) return Wrap(resource, $"relative{action}Timer", "UNSET");
        var timer = new JsonObject { [$"relativeHourTo{action}"] = minutes / 60 };
        if (minutes % 60 != 0) timer[$"relativeMinuteTo{action}"] = minutes % 60;
        return new JsonObject { [resource] = timer };
    }

    private static JsonObject Wrap(string resource, string property, JsonNode value) =>
        new() { [resource] = new JsonObject { [property] = value } };
}
