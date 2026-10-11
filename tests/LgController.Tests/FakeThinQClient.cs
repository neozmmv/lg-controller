using System.Text.Json;
using System.Text.Json.Nodes;
using LgController.ThinQ;

namespace LgController.Tests;

public sealed class FakeThinQClient : IThinQClient
{
    public List<DeviceInfo> Devices { get; } = [];
    public string ProfileJson { get; set; } = Samples.Profile;
    public string StateJson { get; set; } = Samples.State;
    public List<string> Controls { get; } = [];
    public int StateCalls { get; private set; }
    public ThinQException? ListError { get; set; }
    public ThinQException? StateError { get; set; }
    public ThinQException? ControlError { get; set; }
    /// <summary>When set, the send stays in flight until the test completes the task.</summary>
    public TaskCompletionSource? ControlGate { get; set; }

    public Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default) =>
        ListError is null ? Task.FromResult<IReadOnlyList<DeviceInfo>>(Devices) : Task.FromException<IReadOnlyList<DeviceInfo>>(ListError);

    public Task<JsonElement> GetProfileAsync(string deviceId, CancellationToken ct = default) =>
        Task.FromResult(Samples.Parse(ProfileJson));

    public Task<JsonElement> GetStateAsync(string deviceId, CancellationToken ct = default)
    {
        StateCalls++;
        return StateError is null ? Task.FromResult(Samples.Parse(StateJson)) : Task.FromException<JsonElement>(StateError);
    }

    public async Task ControlAsync(string deviceId, JsonObject payload, CancellationToken ct = default)
    {
        Controls.Add(payload.ToJsonString());
        if (ControlGate is { } gate) await gate.Task;
        if (ControlError is { } error) throw error;
    }
}
