using System.Text.Json.Nodes;
using LgController.ThinQ;

namespace LgController.Core;

/// <summary>
/// Holds the device state, applies commands optimistically and sends them debounced.
/// All access must happen on the thread that <c>dispatch</c> forwards to.
/// </summary>
public sealed class AcController : IDisposable
{
    public static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(600);
    public static readonly TimeSpan ConfirmDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan OpenPollInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ClosedPollInterval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RateLimitPause = TimeSpan.FromMinutes(1);

    private sealed class PendingChange(Func<AcState, AcState> apply, JsonObject payload)
    {
        public Func<AcState, AcState> Apply { get; } = apply;
        public JsonObject Payload { get; } = payload;
        public ITimer? Timer { get; set; }
    }

    private readonly IThinQClient _client;
    private readonly string _deviceId;
    private readonly TimeProvider _time;
    private readonly Action<Action> _dispatch;
    private readonly Dictionary<AcField, PendingChange> _pending = [];
    private AcState? _confirmed;
    private ITimer? _pollTimer;
    private ITimer? _confirmTimer;
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;
    private bool _refreshing;
    private bool _disposed;

    public AcController(IThinQClient client, string deviceId, TimeProvider time, Action<Action>? dispatch = null)
    {
        _client = client;
        _deviceId = deviceId;
        _time = time;
        _dispatch = dispatch ?? (action => action());
    }

    public event Action? StateChanged;
    public event Action<ThinQException>? ErrorOccurred;

    public AcCapabilities? Capabilities { get; private set; }

    /// <summary>Error from the last read; null when the last read succeeded.</summary>
    public ThinQException? ConnectionError { get; private set; }

    /// <summary>Last confirmed state with the pending commands applied on top.</summary>
    public AcState? State =>
        _confirmed is null ? null : _pending.Values.Aggregate(_confirmed, (state, change) => change.Apply(state));

    public async Task RefreshAsync()
    {
        if (_refreshing || _disposed) return;
        _refreshing = true;
        try
        {
            Capabilities ??= AcProfileParser.Parse(await _client.GetProfileAsync(_deviceId));
            _confirmed = AcStateParser.Parse(await _client.GetStateAsync(_deviceId), Capabilities);
            ConnectionError = null;
        }
        catch (ThinQException ex)
        {
            ConnectionError = ex;
            if (ex.Kind == ThinQErrorKind.RateLimited) _pausedUntil = _time.GetUtcNow() + RateLimitPause;
        }
        finally
        {
            _refreshing = false;
        }

        if (!_disposed) StateChanged?.Invoke();
    }

    public void Start()
    {
        _pollTimer ??= _time.CreateTimer(_ => _dispatch(Poll), null, ClosedPollInterval, ClosedPollInterval);
    }

    public void SetPanelOpen(bool open)
    {
        var interval = open ? OpenPollInterval : ClosedPollInterval;
        _pollTimer?.Change(interval, interval);
        if (open) Poll();
    }

    public void SetPower(bool on) =>
        Enqueue(AcField.Power, s => s with { IsOn = on }, AcCommands.Power(on));

    public void SetMode(string mode) =>
        Enqueue(AcField.Mode, s => s with { Mode = mode }, AcCommands.Mode(mode));

    public void SetTargetTemperature(double value)
    {
        if (Capabilities is not { } caps || caps.TemperatureResource == TemperatureResource.None) return;
        var steps = Math.Round((value - caps.MinTemperature) / caps.TemperatureStep, MidpointRounding.AwayFromZero);
        var snapped = Math.Clamp(caps.MinTemperature + steps * caps.TemperatureStep, caps.MinTemperature, caps.MaxTemperature);
        Enqueue(AcField.Temperature, s => s with { TargetTemperature = snapped }, AcCommands.Temperature(caps, snapped));
    }

    public void SetWindStrength(string strength)
    {
        if (Capabilities is not { } caps) return;
        Enqueue(AcField.WindStrength, s => s with { WindStrength = strength }, AcCommands.WindStrength(caps, strength));
    }

    public void SetSwingVertical(bool on) =>
        Enqueue(AcField.SwingVertical, s => s with { SwingVertical = on }, AcCommands.SwingVertical(on));

    public void SetSwingHorizontal(bool on) =>
        Enqueue(AcField.SwingHorizontal, s => s with { SwingHorizontal = on }, AcCommands.SwingHorizontal(on));

    public void SetPowerSave(bool on) =>
        Enqueue(AcField.PowerSave, s => s with { PowerSave = on }, AcCommands.PowerSave(on));

    public void SetStopTimer(int minutes) =>
        Enqueue(
            AcField.StopTimer,
            s => s with { StopTimerMinutes = minutes > 0 ? minutes : null },
            AcCommands.StopTimer(minutes));

    public void SetStartTimer(int minutes) =>
        Enqueue(
            AcField.StartTimer,
            s => s with { StartTimerMinutes = minutes > 0 ? minutes : null },
            AcCommands.StartTimer(minutes));

    public void SetSleepTimer(int minutes) =>
        Enqueue(
            AcField.SleepTimer,
            s => s with { SleepTimerMinutes = minutes > 0 ? minutes : null },
            AcCommands.SleepTimer(minutes));

    public void Dispose()
    {
        _disposed = true;
        _pollTimer?.Dispose();
        _confirmTimer?.Dispose();
        foreach (var change in _pending.Values) change.Timer?.Dispose();
        _pending.Clear();
    }

    private void Poll()
    {
        if (_disposed || _time.GetUtcNow() < _pausedUntil) return;
        _ = RefreshAsync();
    }

    private void Enqueue(AcField field, Func<AcState, AcState> apply, JsonObject payload)
    {
        if (_confirmed is null || _disposed) return;
        if (_pending.TryGetValue(field, out var previous)) previous.Timer?.Dispose();

        var change = new PendingChange(apply, payload);
        _pending[field] = change;
        change.Timer = _time.CreateTimer(
            _ => _dispatch(() => _ = SendAsync(field, change)), null, DebounceDelay, Timeout.InfiniteTimeSpan);
        StateChanged?.Invoke();
    }

    private bool IsCurrent(AcField field, PendingChange change) =>
        _pending.TryGetValue(field, out var current) && ReferenceEquals(current, change);

    private async Task SendAsync(AcField field, PendingChange change)
    {
        if (_disposed || !IsCurrent(field, change)) return;
        change.Timer?.Dispose();
        try
        {
            await _client.ControlAsync(_deviceId, change.Payload);
        }
        catch (ThinQException ex)
        {
            if (_disposed) return;
            if (IsCurrent(field, change)) _pending.Remove(field);
            StateChanged?.Invoke();
            ErrorOccurred?.Invoke(ex);
            return;
        }

        if (_disposed) return;
        if (_confirmed is not null) _confirmed = change.Apply(_confirmed);
        if (IsCurrent(field, change)) _pending.Remove(field);
        _confirmTimer?.Dispose();
        _confirmTimer = _time.CreateTimer(_ => _dispatch(Poll), null, ConfirmDelay, Timeout.InfiniteTimeSpan);
    }
}
