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
        var airFlow = state.Prop("airFlow");
        var windDetail = airFlow.Prop("windStrengthDetail").AsString();
        var timer = state.Prop("timer");
        var filter = state.Prop("filterInfo");

        return new AcState
        {
            IsOn = state.Prop("operation").Prop("airConOperationMode").AsString() == "POWER_ON",
            Mode = state.Prop("airConJobMode").Prop("currentJobMode").AsString(),
            CurrentTemperature = temperature.Prop("currentTemperature").AsDouble(),
            TargetTemperature = temperature.Prop("targetTemperature").AsDouble(),
            WindStrength = windDetail is not null && caps.WindStrengthDetails.Contains(windDetail)
                ? windDetail
                : airFlow.Prop("windStrength").AsString() ?? windDetail,
            SwingVertical = windDirection.Prop("rotateUpDown").AsBool() ?? false,
            SwingHorizontal = windDirection.Prop("rotateLeftRight").AsBool() ?? false,
            PowerSave = state.Prop("powerSave").Prop("powerSaveEnabled").AsBool() ?? false,
            StopTimerMinutes = TimerMinutes(timer, "Stop"),
            StartTimerMinutes = TimerMinutes(timer, "Start"),
            SleepTimerMinutes = TimerMinutes(state.Prop("sleepTimer"), "Stop"),
            FilterUsedHours = filter.Prop("usedTime").AsDouble(),
            FilterLifetimeHours = filter.Prop("filterLifetime").AsDouble(),
        };
    }

    /// <summary><paramref name="action"/> is the property name suffix: "Stop" or "Start".</summary>
    private static int? TimerMinutes(JsonElement timer, string action)
    {
        if (timer.Prop($"relative{action}Timer").AsString() == "UNSET") return null;
        var minutes = (int)((timer.Prop($"relativeHourTo{action}").AsDouble() ?? 0) * 60
                            + (timer.Prop($"relativeMinuteTo{action}").AsDouble() ?? 0));
        return minutes > 0 ? minutes : null;
    }
}
