using System.Net;
using System.Text.Json.Nodes;
using LgController.ThinQ;

namespace LgController.Tests;

public class ThinQClientTests
{
    private static readonly ThinQCredentials Creds = new("pat-123", "BR", "thinq-open-abc");

    private static (ThinQClient Client, StubHandler Handler) Create(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        return (new ThinQClient(new HttpClient(handler), Creds), handler);
    }

    [Fact]
    public async Task ListDevices_SendsHeadersToRegionalHost()
    {
        var (client, handler) = Create(HttpStatusCode.OK, """{"response":[]}""");

        await client.ListDevicesAsync();

        Assert.Equal("https://api-aic.lgthinq.com/devices", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer pat-123", handler.Header("Authorization"));
        Assert.Equal("BR", handler.Header("x-country"));
        Assert.Equal("thinq-open-abc", handler.Header("x-client-id"));
        Assert.Equal("v6GFvkweNo7DK7yD3ylIZ9w52aKBU0eJ7wLXkSR3", handler.Header("x-api-key"));
        Assert.Equal("OP", handler.Header("x-service-phase"));
        var messageId = handler.Header("x-message-id");
        Assert.Equal(22, messageId.Length);
        Assert.DoesNotContain(messageId, c => c is '=' or '+' or '/');
    }

    [Fact]
    public async Task ListDevices_ParsesDevices()
    {
        var (client, _) = Create(HttpStatusCode.OK, """
            {"response":[
              {"deviceId":"abc","deviceInfo":{"deviceType":"DEVICE_AIR_CONDITIONER","modelName":"RAC","alias":"Quarto"}},
              {"deviceId":"def","deviceInfo":{"deviceType":"DEVICE_WASHER","modelName":"W1"}},
              {"noId":true}
            ]}
            """);

        var devices = await client.ListDevicesAsync();

        Assert.Equal(2, devices.Count);
        Assert.Equal(new DeviceInfo("abc", "DEVICE_AIR_CONDITIONER", "Quarto", "RAC"), devices[0]);
        Assert.Equal("def", devices[1].Alias);
    }

    [Fact]
    public async Task GetState_ReturnsResponseElement()
    {
        var (client, handler) = Create(HttpStatusCode.OK, """{"response":{"operation":{"airConOperationMode":"POWER_ON"}}}""");

        var state = await client.GetStateAsync("abc");

        Assert.Equal("https://api-aic.lgthinq.com/devices/abc/state", handler.Request!.RequestUri!.ToString());
        Assert.Equal("POWER_ON", state.Prop("operation").Prop("airConOperationMode").AsString());
    }

    [Fact]
    public async Task GetProfile_UsesProfilePath()
    {
        var (client, handler) = Create(HttpStatusCode.OK, """{"response":{"property":{}}}""");

        await client.GetProfileAsync("abc");

        Assert.Equal("https://api-aic.lgthinq.com/devices/abc/profile", handler.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Control_PostsPayloadWithConditionalHeader()
    {
        var (client, handler) = Create(HttpStatusCode.OK, """{"response":{}}""");
        var payload = new JsonObject { ["operation"] = new JsonObject { ["airConOperationMode"] = "POWER_OFF" } };

        await client.ControlAsync("abc", payload);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api-aic.lgthinq.com/devices/abc/control", handler.Request.RequestUri!.ToString());
        Assert.Equal("true", handler.Header("x-conditional-control"));
        Assert.Equal("""{"operation":{"airConOperationMode":"POWER_OFF"}}""", handler.RequestBody);
    }

    [Theory]
    [InlineData("1103", ThinQErrorKind.Unauthorized)]
    [InlineData("1218", ThinQErrorKind.Unauthorized)]
    [InlineData("1222", ThinQErrorKind.DeviceOffline)]
    [InlineData("1306", ThinQErrorKind.RateLimited)]
    [InlineData("2208", ThinQErrorKind.CommandRejected)]
    [InlineData("2304", ThinQErrorKind.CommandRejected)]
    [InlineData("2000", ThinQErrorKind.Unknown)]
    public async Task ApiError_IsClassified(string code, ThinQErrorKind kind)
    {
        var (client, _) = Create(HttpStatusCode.BadRequest, $$$"""{"error":{"code":"{{{code}}}","message":"falhou"}}""");

        var ex = await Assert.ThrowsAsync<ThinQException>(() => client.GetStateAsync("abc"));

        Assert.Equal(code, ex.Code);
        Assert.Equal("falhou", ex.Message);
        Assert.Equal(kind, ex.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, "<html>Bad Gateway</html>", ThinQErrorKind.Unknown)]
    [InlineData(HttpStatusCode.Unauthorized, "", ThinQErrorKind.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests, "slow down", ThinQErrorKind.RateLimited)]
    public async Task NonJsonErrorBody_BecomesThinQException(HttpStatusCode status, string body, ThinQErrorKind kind)
    {
        var (client, _) = Create(status, body);

        var ex = await Assert.ThrowsAsync<ThinQException>(() => client.GetStateAsync("abc"));

        Assert.Equal(kind, ex.Kind);
        Assert.Equal(((int)status).ToString(), ex.Code);
    }

    [Fact]
    public async Task NonJsonSuccessBody_ReturnsUndefinedElement()
    {
        var (client, _) = Create(HttpStatusCode.OK, "");

        var state = await client.GetStateAsync("abc");

        Assert.Null(state.Prop("operation").AsString());
    }

    [Fact]
    public async Task NetworkFailure_IsNetworkKind()
    {
        var (client, handler) = Create(HttpStatusCode.OK, "{}");
        handler.Throw = new HttpRequestException("no route");

        var ex = await Assert.ThrowsAsync<ThinQException>(() => client.ListDevicesAsync());

        Assert.Equal(ThinQErrorKind.Network, ex.Kind);
    }

    [Fact]
    public async Task Timeout_IsNetworkKind()
    {
        var (client, handler) = Create(HttpStatusCode.OK, "{}");
        handler.Throw = new TaskCanceledException("timeout");

        var ex = await Assert.ThrowsAsync<ThinQException>(() => client.ListDevicesAsync());

        Assert.Equal(ThinQErrorKind.Network, ex.Kind);
    }
}
