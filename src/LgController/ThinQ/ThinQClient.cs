using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LgController.ThinQ;

public sealed record ThinQCredentials(string Token, string CountryCode, string ClientId);

public sealed record DeviceInfo(string Id, string Type, string Alias, string ModelName);

public interface IThinQClient
{
    Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default);
    Task<JsonElement> GetProfileAsync(string deviceId, CancellationToken ct = default);
    Task<JsonElement> GetStateAsync(string deviceId, CancellationToken ct = default);
    Task ControlAsync(string deviceId, JsonObject payload, CancellationToken ct = default);
}

public sealed class ThinQClient : IThinQClient
{
    private const string ApiKey = "v6GFvkweNo7DK7yD3ylIZ9w52aKBU0eJ7wLXkSR3";

    private readonly HttpClient _http;
    private readonly ThinQCredentials _credentials;
    private readonly Uri _baseUri;

    public ThinQClient(HttpClient http, ThinQCredentials credentials)
    {
        _http = http;
        _credentials = credentials;
        _baseUri = new Uri($"https://api-{ThinQRegion.FromCountry(credentials.CountryCode)}.lgthinq.com/");
    }

    public async Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default)
    {
        var response = await SendAsync(HttpMethod.Get, "devices", null, ct);
        var devices = new List<DeviceInfo>();
        foreach (var item in response.Items())
        {
            var id = item.Prop("deviceId").AsString();
            if (id is null) continue;
            var info = item.Prop("deviceInfo");
            devices.Add(new DeviceInfo(
                id,
                info.Prop("deviceType").AsString() ?? "",
                info.Prop("alias").AsString() ?? id,
                info.Prop("modelName").AsString() ?? ""));
        }
        return devices;
    }

    public Task<JsonElement> GetProfileAsync(string deviceId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, $"devices/{deviceId}/profile", null, ct);

    public Task<JsonElement> GetStateAsync(string deviceId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, $"devices/{deviceId}/state", null, ct);

    public Task ControlAsync(string deviceId, JsonObject payload, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"devices/{deviceId}/control", payload, ct);

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_credentials.Token}");
        request.Headers.TryAddWithoutValidation("x-country", _credentials.CountryCode.Trim().ToUpperInvariant());
        request.Headers.TryAddWithoutValidation("x-message-id", NewMessageId());
        request.Headers.TryAddWithoutValidation("x-client-id", _credentials.ClientId);
        request.Headers.TryAddWithoutValidation("x-api-key", ApiKey);
        request.Headers.TryAddWithoutValidation("x-service-phase", "OP");
        if (body is not null)
        {
            request.Headers.TryAddWithoutValidation("x-conditional-control", "true");
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ThinQException("network", "Could not reach the LG server.", ThinQErrorKind.Network, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ThinQException("timeout", "The LG server did not respond.", ThinQErrorKind.Network, ex);
        }

        using (response)
        {
            var root = ParseOrDefault(await response.Content.ReadAsStringAsync(ct));
            if (response.IsSuccessStatusCode) return root.Prop("response");

            var status = (int)response.StatusCode;
            var error = root.Prop("error");
            var code = error.Prop("code").AsString() ?? status.ToString();
            var message = error.Prop("message").AsString() ?? $"HTTP {status}";
            throw new ThinQException(code, message, ThinQException.KindFor(code, status));
        }
    }

    private static JsonElement ParseOrDefault(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string NewMessageId() =>
        Convert.ToBase64String(Guid.NewGuid().ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
