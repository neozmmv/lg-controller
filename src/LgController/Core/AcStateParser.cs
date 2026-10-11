using System.Text.Json;
using LgController.ThinQ;

namespace LgController.Core;

public static class AcStateParser
{
    public static AcState Parse(JsonElement state, AcCapabilities caps)
    {
        var temperature = caps.TemperatureResource == TemperatureResource.InUnits
            ? state.Prop("temperatureInUnits").Items().FirstOrDefault(u => u.Prop("unit").AsString() == caps.TemperatureUnit)
            : state.Prop("temperature");
        var windDirection = state.Prop("windDirection");

        return new AcState
        {
            IsOn = state.Prop("operation").Prop("airConOperationMode").AsString() == "POWER_ON",
            Mode = state.Prop("airConJobMode").Prop("currentJobMode").AsString(),
            CurrentTemperature = temperature.Prop("currentTemperature").AsDouble(),
            TargetTemperature = temperature.Prop("targetTemperature").AsDouble(),
            WindStrength = state.Prop("airFlow").Prop(caps.WindStrengthKey).AsString(),
            SwingVertical = windDirection.Prop("rotateUpDown").AsBool() ?? false,
            SwingHorizontal = windDirection.Prop("rotateLeftRight").AsBool() ?? false,
            PowerSave = state.Prop("powerSave").Prop("powerSaveEnabled").AsBool() ?? false,
            StopTimerMinutes = StopTimerMinutes(state.Prop("timer")),
        };
    }

    private static int? StopTimerMinutes(JsonElement timer)
    {
        if (timer.Prop("relativeStopTimer").AsString() == "UNSET") return null;
        var minutes = (int)((timer.Prop("relativeHourToStop").AsDouble() ?? 0) * 60
                            + (timer.Prop("relativeMinuteToStop").AsDouble() ?? 0));
        return minutes > 0 ? minutes : null;
    }
}
