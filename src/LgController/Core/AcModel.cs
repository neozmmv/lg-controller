namespace LgController.Core;

public enum TemperatureResource { None, Legacy, InUnits }

public enum AcField { Power, Mode, Temperature, WindStrength, SwingVertical, SwingHorizontal, PowerSave, StopTimer, StartTimer, SleepTimer }

/// <summary>What the device accepts as commands, according to its profile.</summary>
public sealed record AcCapabilities
{
    public bool CanPower { get; init; }
    public IReadOnlyList<string> Modes { get; init; } = [];
    public IReadOnlyList<string> WindStrengths { get; init; } = [];
    /// <summary>Speeds that only the <c>windStrengthDetail</c> property accepts.</summary>
    public IReadOnlyList<string> WindStrengthDetails { get; init; } = [];
    public IReadOnlyList<string> AllWindStrengths => [.. WindStrengths, .. WindStrengthDetails];
    public TemperatureResource TemperatureResource { get; init; }
    public string TemperatureUnit { get; init; } = "C";
    public double MinTemperature { get; init; } = 18;
    public double MaxTemperature { get; init; } = 30;
    public double TemperatureStep { get; init; } = 1;
    public bool CanSwingVertical { get; init; }
    public bool CanSwingHorizontal { get; init; }
    public bool CanPowerSave { get; init; }
    public bool CanStopTimer { get; init; }
    public bool CanCancelStopTimer { get; init; }
    public bool CanStartTimer { get; init; }
    public bool CanCancelStartTimer { get; init; }
    public bool CanSleepTimer { get; init; }
    public bool CanCancelSleepTimer { get; init; }
}

public sealed record AcState
{
    public bool IsOn { get; init; }
    public double? CurrentTemperature { get; init; }
    public double? TargetTemperature { get; init; }
    public string? Mode { get; init; }
    public string? WindStrength { get; init; }
    public bool SwingVertical { get; init; }
    public bool SwingHorizontal { get; init; }
    public bool PowerSave { get; init; }
    /// <summary>Minutes until power-off; null when no timer is set.</summary>
    public int? StopTimerMinutes { get; init; }
    /// <summary>Minutes until power-on; null when no timer is set.</summary>
    public int? StartTimerMinutes { get; init; }
    /// <summary>Minutes left of sleep mode; null when it is not set.</summary>
    public int? SleepTimerMinutes { get; init; }
    public double? FilterUsedHours { get; init; }
    public double? FilterLifetimeHours { get; init; }
}
