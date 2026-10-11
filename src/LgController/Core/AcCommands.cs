using System.Text.Json.Nodes;

namespace LgController.Core;

/// <summary>Monta o corpo do POST de controle para cada ação.</summary>
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
        Wrap("airFlow", caps.WindStrengthKey, strength);

    public static JsonObject SwingVertical(bool on) => Wrap("windDirection", "rotateUpDown", on);

    public static JsonObject SwingHorizontal(bool on) => Wrap("windDirection", "rotateLeftRight", on);

    public static JsonObject PowerSave(bool on) => Wrap("powerSave", "powerSaveEnabled", on);

    /// <summary>Zero ou menos cancela o timer.</summary>
    public static JsonObject StopTimer(int minutes)
    {
        if (minutes <= 0) return Wrap("timer", "relativeStopTimer", "UNSET");
        var timer = new JsonObject { ["relativeHourToStop"] = minutes / 60 };
        if (minutes % 60 != 0) timer["relativeMinuteToStop"] = minutes % 60;
        return new JsonObject { ["timer"] = timer };
    }

    private static JsonObject Wrap(string resource, string property, JsonNode value) =>
        new() { [resource] = new JsonObject { [property] = value } };
}
