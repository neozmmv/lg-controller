# LG Controller Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Um aplicativo de bandeja do Windows que controla um ar-condicionado LG ThinQ por um painel flutuante.

**Architecture:** Um projeto WPF em três camadas com dependência em uma direção (`UI → Core → ThinQ`). `ThinQ/` fala REST com a API ThinQ Connect; `Core/` traduz perfil e estado em um modelo de ar-condicionado e aplica comandos de forma otimista com agrupamento; `UI/` tem o ícone da bandeja, o painel e a tela de configuração.

**Tech Stack:** C# / .NET 10, WPF, CommunityToolkit.Mvvm, H.NotifyIcon.Wpf, xUnit, Microsoft.Extensions.TimeProvider.Testing.

**Spec:** `docs/superpowers/specs/2026-10-11-lg-tray-controller-design.md`

## Global Constraints

- Alvo `net10.0-windows`; um único `.exe` publicado com `PublishSingleFile`.
- Projetos: `src/LgController` (WPF), `tests/LgController.Tests` (xUnit), `tools/Probe` (console de verificação).
- URL base `https://api-{região}.lgthinq.com/`, região `kic`, `aic` ou `eic` conforme o país.
- Cabeçalhos em toda requisição: `Authorization: Bearer {PAT}`, `x-country`, `x-message-id` (UUID v4 em base64 url-safe sem `=`, 22 caracteres), `x-client-id` (`thinq-open-{uuid}`), `x-api-key: v6GFvkweNo7DK7yD3ylIZ9w52aKBU0eJ7wLXkSR3`, `x-service-phase: OP`. O `POST` de controle leva também `x-conditional-control: true`.
- Agrupamento de comandos: 600 ms por tipo. Releitura de confirmação: 2 s. Polling: 30 s com o painel aberto, 5 min fechado. Pausa após limite de chamadas: 1 min.
- Configuração em `%APPDATA%\LgController\settings.json`; token protegido com DPAPI no escopo `CurrentUser`. Log em `%APPDATA%\LgController\log.txt`.
- Painel com 360 px de largura. Todos os textos da interface em inglês (mudança pedida depois da execução; o código abaixo ainda mostra os textos originais em português).
- Só aparece o controle que o perfil do aparelho declara como gravável.
- O token nunca é escrito em log, console ou arquivo fora do `settings.json` protegido.

## Review Focus

Condições que a spec implica e que mais provavelmente atingem quem usa o aplicativo. Cada uma tem um teste na tarefa indicada.

1. **Resposta de erro que não é JSON** (página HTML de um gateway, corpo vazio): o cliente lança `ThinQException`, nunca `JsonException`. Tarefa 1.
2. **Estado sem alguns recursos ou com valores nulos** (aparelho desligado costuma omitir campos): o parser devolve valores padrão sem lançar exceção. Tarefa 2.
3. **`settings.json` corrompido ou com token que não pode ser descriptografado**: `Load` devolve `null` e o aplicativo abre a configuração em vez de quebrar. Tarefa 4.
4. **Temperatura fora da faixa ou fora do passo** (cliques repetidos em + no máximo, passo de 0,5): o valor é limitado à faixa e ajustado ao passo antes de ir para a LG. Tarefa 5.
5. **Clique no ícone da bandeja com o painel aberto**: o painel fecha e permanece fechado, em vez de fechar e reabrir. Tarefa 7.

## Mapa de arquivos

```
src/LgController/
  App.xaml, App.xaml.cs            composição: instância única, sessão, bandeja, painel
  ThinQ/
    JsonExtensions.cs              leitura tolerante de JsonElement
    ThinQRegion.cs                 país → região
    ThinQException.cs              erro da API e sua classificação
    ThinQClient.cs                 IThinQClient, ThinQClient, ThinQCredentials, DeviceInfo
  Core/
    AcModel.cs                     AcCapabilities, AcState, AcField, TemperatureResource
    AcProfileParser.cs             perfil → AcCapabilities
    AcStateParser.cs               estado → AcState
    AcCommands.cs                  ação → payload
    AcController.cs                estado otimista, agrupamento, polling
    SettingsStore.cs               AppSettings, leitura e gravação com DPAPI
    ErrorMessages.cs               ThinQException → texto em português
    ModePalette.cs                 modo → cor
    AutoStart.cs                   iniciar com o Windows
    Log.cs                         log de exceções
  UI/
    Themes/Dark.xaml, Light.xaml   pincéis por tema
    Themes/Controls.xaml           estilos de botões, chips e interruptor
    ThemeManager.cs                segue o tema do Windows
    TrayIconRenderer.cs            desenha o ícone
    TrayIcon.cs                    ícone, dica e menu
    OptionItem.cs                  item de seletor e catálogo de rótulos
    SetupViewModel.cs, SetupWindow.xaml(.cs)
    FlyoutPlacement.cs             posição do painel
    FlyoutToggleGuard.cs           evita reabrir ao clicar no ícone
    ArcGauge.cs                    mostrador circular
    Backdrop.cs                    DWM: acrílico, cantos, modo escuro
    FlyoutViewModel.cs, FlyoutWindow.xaml(.cs)
tests/LgController.Tests/
  Samples.cs, StubHandler.cs, FakeThinQClient.cs
  ThinQRegionTests.cs, ThinQClientTests.cs
  AcProfileParserTests.cs, AcStateParserTests.cs, AcCommandsTests.cs
  RealDeviceTests.cs, Fixtures/real-profile.json, Fixtures/real-state.json
  SettingsStoreTests.cs, AcControllerTests.cs
  SetupViewModelTests.cs, FlyoutPlacementTests.cs, FlyoutToggleGuardTests.cs, FlyoutViewModelTests.cs
tools/Probe/Program.cs
```

---

### Task 1: Solução e cliente ThinQ

**Files:**
- Create: `src/LgController/ThinQ/JsonExtensions.cs`, `ThinQRegion.cs`, `ThinQException.cs`, `ThinQClient.cs`
- Modify: `src/LgController/App.xaml`
- Delete: `src/LgController/MainWindow.xaml`, `src/LgController/MainWindow.xaml.cs`
- Test: `tests/LgController.Tests/StubHandler.cs`, `ThinQRegionTests.cs`, `ThinQClientTests.cs`

**Interfaces:**
- Produces:
  - `JsonElement.Prop(string)`, `.AsString()`, `.AsDouble()`, `.AsBool()`, `.Items()`
  - `ThinQRegion.FromCountry(string countryCode) : string`
  - `enum ThinQErrorKind { Network, Unauthorized, DeviceOffline, RateLimited, CommandRejected, Unknown }`
  - `ThinQException(string code, string message, ThinQErrorKind kind, Exception? inner = null)` com `Code` e `Kind`
  - `record ThinQCredentials(string Token, string CountryCode, string ClientId)`
  - `record DeviceInfo(string Id, string Type, string Alias, string ModelName)`
  - `interface IThinQClient` com `ListDevicesAsync`, `GetProfileAsync`, `GetStateAsync`, `ControlAsync(string deviceId, JsonObject payload, CancellationToken ct = default)`

- [ ] **Step 1: Criar a solução e os projetos**

```powershell
dotnet new gitignore
dotnet new sln -n LgController
dotnet new wpf -n LgController -o src/LgController
dotnet new xunit -n LgController.Tests -o tests/LgController.Tests
dotnet sln add src/LgController tests/LgController.Tests
dotnet add tests/LgController.Tests reference src/LgController
dotnet add src/LgController package CommunityToolkit.Mvvm
dotnet add src/LgController package H.NotifyIcon.Wpf
dotnet add tests/LgController.Tests package Microsoft.Extensions.TimeProvider.Testing
Remove-Item src/LgController/MainWindow.xaml, src/LgController/MainWindow.xaml.cs
Remove-Item tests/LgController.Tests/UnitTest1.cs
```

Em `tests/LgController.Tests/LgController.Tests.csproj`, troque `<TargetFramework>net10.0</TargetFramework>` por:

```xml
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
```

e acrescente antes de `</Project>`:

```xml
  <ItemGroup>
    <None Update="Fixtures\**" CopyToOutputDirectory="PreserveNewest" />
    <!-- UseWPF remove estes dois dos usings implícitos. -->
    <Using Include="System.IO" />
    <Using Include="System.Net.Http" />
  </ItemGroup>
```

Substitua `src/LgController/App.xaml` por:

```xml
<Application x:Class="LgController.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
    <Application.Resources />
</Application>
```

Run: `dotnet build`
Expected: `Build succeeded` com 0 erros.

- [ ] **Step 2: Escrever os testes que falham**

`tests/LgController.Tests/StubHandler.cs`:

```csharp
using System.Net;
using System.Text;

namespace LgController.Tests;

public sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public HttpRequestMessage? Request { get; private set; }
    public string? RequestBody { get; private set; }
    public Exception? Throw { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Request = request;
        RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        if (Throw is not null) throw Throw;
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    public string Header(string name) => string.Join(",", Request!.Headers.GetValues(name));
}
```

`tests/LgController.Tests/ThinQRegionTests.cs`:

```csharp
using LgController.ThinQ;

namespace LgController.Tests;

public class ThinQRegionTests
{
    [Theory]
    [InlineData("BR", "aic")]
    [InlineData("us", "aic")]
    [InlineData("KR", "kic")]
    [InlineData(" pt ", "eic")]
    public void FromCountry_MapsToRegion(string country, string region) =>
        Assert.Equal(region, ThinQRegion.FromCountry(country));

    [Fact]
    public void FromCountry_UnknownCountry_Throws() =>
        Assert.Throws<ArgumentException>(() => ThinQRegion.FromCountry("ZZ"));
}
```

`tests/LgController.Tests/ThinQClientTests.cs`:

```csharp
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
        var (client, _) = Create(HttpStatusCode.BadRequest, $$"""{"error":{"code":"{{code}}","message":"falhou"}}""");

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
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test`
Expected: FALHA de compilação, `The type or namespace name 'ThinQ' does not exist`.

- [ ] **Step 4: Implementar**

`src/LgController/ThinQ/JsonExtensions.cs`:

```csharp
using System.Text.Json;

namespace LgController.ThinQ;

/// <summary>Leitura que nunca lança: um caminho ausente devolve um elemento indefinido.</summary>
public static class JsonExtensions
{
    public static JsonElement Prop(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    public static string? AsString(this JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    public static double? AsDouble(this JsonElement element) =>
        element.ValueKind == JsonValueKind.Number ? element.GetDouble() : null;

    public static bool? AsBool(this JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    public static IReadOnlyList<JsonElement> Items(this JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().ToArray() : [];
}
```

`src/LgController/ThinQ/ThinQRegion.cs`:

```csharp
namespace LgController.ThinQ;

public static class ThinQRegion
{
    private static readonly HashSet<string> Kic =
    [
        "AU", "BD", "CN", "HK", "ID", "IN", "JP", "KH", "KR", "LA", "LK", "MM", "MY", "NP", "NZ", "PH", "SG", "TH",
        "TW", "VN",
    ];

    private static readonly HashSet<string> Aic =
    [
        "AG", "AR", "AW", "BB", "BO", "BR", "BS", "BZ", "CA", "CL", "CO", "CR", "CU", "DM", "DO", "EC", "GD", "GT",
        "GY", "HN", "HT", "JM", "KN", "LC", "MX", "NI", "PA", "PE", "PR", "PY", "SR", "SV", "TT", "US", "UY", "VC",
        "VE",
    ];

    private static readonly HashSet<string> Eic =
    [
        "AE", "AF", "AL", "AM", "AO", "AT", "AZ", "BA", "BE", "BF", "BG", "BH", "BJ", "BY", "CD", "CF", "CG", "CH",
        "CI", "CM", "CV", "CY", "CZ", "DE", "DJ", "DK", "DZ", "EE", "EG", "ES", "ET", "FI", "FR", "GA", "GB", "GE",
        "GH", "GM", "GN", "GQ", "GR", "HR", "HU", "IE", "IL", "IQ", "IR", "IS", "IT", "JO", "KE", "KG", "KW", "KZ",
        "LB", "LR", "LT", "LU", "LV", "LY", "MA", "MD", "ME", "MK", "ML", "MR", "MT", "MU", "MW", "NE", "NG", "NL",
        "NO", "OM", "PK", "PL", "PS", "PT", "QA", "RO", "RS", "RU", "RW", "SA", "SD", "SE", "SI", "SK", "SL", "SN",
        "SO", "ST", "SY", "TD", "TG", "TN", "TR", "TZ", "UA", "UG", "UZ", "XK", "YE", "ZA", "ZM",
    ];

    public static string FromCountry(string countryCode)
    {
        var code = countryCode.Trim().ToUpperInvariant();
        if (Kic.Contains(code)) return "kic";
        if (Aic.Contains(code)) return "aic";
        if (Eic.Contains(code)) return "eic";
        throw new ArgumentException($"País não suportado: {countryCode}", nameof(countryCode));
    }
}
```

`src/LgController/ThinQ/ThinQException.cs`:

```csharp
namespace LgController.ThinQ;

public enum ThinQErrorKind { Network, Unauthorized, DeviceOffline, RateLimited, CommandRejected, Unknown }

public sealed class ThinQException(string code, string message, ThinQErrorKind kind, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
    public ThinQErrorKind Kind { get; } = kind;

    // Códigos da tabela ThinQAPIErrorCodes do SDK oficial.
    public static ThinQErrorKind KindFor(string code, int httpStatus) => code switch
    {
        "1103" or "1218" or "1302" => ThinQErrorKind.Unauthorized,
        "1222" => ThinQErrorKind.DeviceOffline,
        "1306" => ThinQErrorKind.RateLimited,
        "1223" or "2205" or "2207" or "2208" or "2209" or "2301" or "2302" or "2303" or "2304" or "2305"
            => ThinQErrorKind.CommandRejected,
        _ when httpStatus == 401 => ThinQErrorKind.Unauthorized,
        _ when httpStatus == 429 => ThinQErrorKind.RateLimited,
        _ => ThinQErrorKind.Unknown,
    };
}
```

`src/LgController/ThinQ/ThinQClient.cs`:

```csharp
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
            throw new ThinQException("network", "Sem conexão com o servidor da LG.", ThinQErrorKind.Network, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ThinQException("timeout", "O servidor da LG não respondeu.", ThinQErrorKind.Network, ex);
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
```

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet test`
Expected: todos os testes passam, 0 falhas.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: solution scaffold and ThinQ Connect REST client"
```

---

### Task 2: Modelo do ar, parsers e comandos

**Files:**
- Create: `src/LgController/Core/AcModel.cs`, `AcProfileParser.cs`, `AcStateParser.cs`, `AcCommands.cs`
- Test: `tests/LgController.Tests/Samples.cs`, `AcProfileParserTests.cs`, `AcStateParserTests.cs`, `AcCommandsTests.cs`

**Interfaces:**
- Consumes: `JsonElement.Prop/AsString/AsDouble/AsBool/Items` (Tarefa 1)
- Produces:
  - `enum TemperatureResource { None, Legacy, InUnits }`
  - `enum AcField { Power, Mode, Temperature, WindStrength, SwingVertical, SwingHorizontal, PowerSave, StopTimer }`
  - `record AcCapabilities` com `CanPower`, `Modes`, `WindStrengths`, `WindStrengthKey`, `TemperatureResource`, `TemperatureUnit`, `MinTemperature`, `MaxTemperature`, `TemperatureStep`, `CanSwingVertical`, `CanSwingHorizontal`, `CanPowerSave`, `CanStopTimer`, `CanCancelStopTimer`
  - `record AcState` com `IsOn`, `CurrentTemperature`, `TargetTemperature`, `Mode`, `WindStrength`, `SwingVertical`, `SwingHorizontal`, `PowerSave`, `StopTimerMinutes`
  - `AcProfileParser.Parse(JsonElement profile) : AcCapabilities`
  - `AcStateParser.Parse(JsonElement state, AcCapabilities caps) : AcState`
  - `AcCommands.Power(bool)`, `.Mode(string)`, `.Temperature(AcCapabilities, double)`, `.WindStrength(AcCapabilities, string)`, `.SwingVertical(bool)`, `.SwingHorizontal(bool)`, `.PowerSave(bool)`, `.StopTimer(int minutes)`, todos devolvendo `JsonObject`
  - `Samples.Profile`, `Samples.State`, `Samples.Parse(string json) : JsonElement`

Formato do perfil: cada propriedade é `{"type": ..., "mode": ["r","w"], "value": {"r": ..., "w": ...}}`. Para `enum` e `boolean`, `value.w` é a lista de valores aceitos; para `range`, é `{"min", "max", "step"}`. `temperatureInUnits` é uma lista com um objeto por unidade (`"unit": "C"` ou `"F"`), tanto no perfil quanto no estado.

- [ ] **Step 1: Escrever os testes que falham**

`tests/LgController.Tests/Samples.cs`:

```csharp
using System.Text.Json;

namespace LgController.Tests;

public static class Samples
{
    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public const string Profile = """
        {
          "notification": { "push": ["WATER_IS_FULL"] },
          "property": {
            "operation": {
              "airConOperationMode": { "type": "enum", "mode": ["r", "w"],
                "value": { "r": ["POWER_ON", "POWER_OFF"], "w": ["POWER_ON", "POWER_OFF"] } }
            },
            "airConJobMode": {
              "currentJobMode": { "type": "enum", "mode": ["r", "w"],
                "value": { "r": ["COOL", "AIR_DRY", "FAN", "AUTO"], "w": ["COOL", "AIR_DRY", "FAN", "AUTO"] } }
            },
            "temperature": {
              "currentTemperature": { "type": "number", "mode": ["r"] },
              "targetTemperature": { "type": "range", "mode": ["r", "w"],
                "value": { "r": { "max": 30, "min": 18, "step": 1 }, "w": { "max": 30, "min": 18, "step": 1 } } },
              "unit": { "type": "enum", "mode": ["r"], "value": { "r": ["C", "F"] } }
            },
            "temperatureInUnits": [
              { "unit": "F",
                "currentTemperature": { "type": "number", "mode": ["r"] },
                "targetTemperature": { "type": "range", "mode": ["r", "w"],
                  "value": { "r": { "max": 86, "min": 64, "step": 1 }, "w": { "max": 86, "min": 64, "step": 1 } } } },
              { "unit": "C",
                "currentTemperature": { "type": "number", "mode": ["r"] },
                "targetTemperature": { "type": "range", "mode": ["r", "w"],
                  "value": { "r": { "max": 30, "min": 16, "step": 0.5 }, "w": { "max": 30, "min": 16, "step": 0.5 } } } }
            ],
            "airFlow": {
              "windStrength": { "type": "enum", "mode": ["r", "w"],
                "value": { "r": ["LOW", "MID", "HIGH", "AUTO"], "w": ["LOW", "MID", "HIGH", "AUTO"] } }
            },
            "windDirection": {
              "rotateUpDown": { "type": "boolean", "mode": ["r", "w"],
                "value": { "r": [true, false], "w": [true, false] } }
            },
            "powerSave": {
              "powerSaveEnabled": { "type": "boolean", "mode": ["r", "w"],
                "value": { "r": [true, false], "w": [true, false] } }
            },
            "timer": {
              "relativeHourToStop": { "type": "number", "mode": ["r", "w"] },
              "relativeMinuteToStop": { "type": "number", "mode": ["r", "w"] },
              "relativeStopTimer": { "type": "enum", "mode": ["r", "w"],
                "value": { "r": ["SET", "UNSET"], "w": ["UNSET"] } }
            }
          }
        }
        """;

    public const string State = """
        {
          "operation": { "airConOperationMode": "POWER_ON" },
          "airConJobMode": { "currentJobMode": "COOL" },
          "temperature": { "currentTemperature": 26, "targetTemperature": 23, "unit": "C" },
          "temperatureInUnits": [
            { "currentTemperature": 79, "targetTemperature": 73, "unit": "F" },
            { "currentTemperature": 26.5, "targetTemperature": 23, "unit": "C" }
          ],
          "airFlow": { "windStrength": "MID" },
          "windDirection": { "rotateUpDown": true, "rotateLeftRight": false },
          "powerSave": { "powerSaveEnabled": false },
          "timer": { "relativeHourToStop": 1, "relativeMinuteToStop": 30, "relativeStopTimer": "SET" }
        }
        """;
}
```

`tests/LgController.Tests/AcProfileParserTests.cs`:

```csharp
using LgController.Core;

namespace LgController.Tests;

public class AcProfileParserTests
{
    [Fact]
    public void Parse_SampleProfile_ReadsCapabilities()
    {
        var caps = AcProfileParser.Parse(Samples.Parse(Samples.Profile));

        Assert.True(caps.CanPower);
        Assert.Equal(["COOL", "AIR_DRY", "FAN", "AUTO"], caps.Modes);
        Assert.Equal(["LOW", "MID", "HIGH", "AUTO"], caps.WindStrengths);
        Assert.Equal("windStrength", caps.WindStrengthKey);
        Assert.True(caps.CanSwingVertical);
        Assert.False(caps.CanSwingHorizontal);
        Assert.True(caps.CanPowerSave);
        Assert.True(caps.CanStopTimer);
        Assert.True(caps.CanCancelStopTimer);
    }

    [Fact]
    public void Parse_PrefersCelsiusEntryOfTemperatureInUnits()
    {
        var caps = AcProfileParser.Parse(Samples.Parse(Samples.Profile));

        Assert.Equal(TemperatureResource.InUnits, caps.TemperatureResource);
        Assert.Equal("C", caps.TemperatureUnit);
        Assert.Equal(16, caps.MinTemperature);
        Assert.Equal(30, caps.MaxTemperature);
        Assert.Equal(0.5, caps.TemperatureStep);
    }

    [Fact]
    public void Parse_WithoutTemperatureInUnits_FallsBackToLegacy()
    {
        var caps = AcProfileParser.Parse(Samples.Parse("""
            {"property":{"temperature":{"targetTemperature":{"type":"range","mode":["r","w"],
              "value":{"w":{"max":30,"min":18}}}}}}
            """));

        Assert.Equal(TemperatureResource.Legacy, caps.TemperatureResource);
        Assert.Equal(18, caps.MinTemperature);
        Assert.Equal(30, caps.MaxTemperature);
        Assert.Equal(1, caps.TemperatureStep);
    }

    [Fact]
    public void Parse_PrefersWindStrengthDetailWhenPresent()
    {
        var caps = AcProfileParser.Parse(Samples.Parse("""
            {"property":{"airFlow":{
              "windStrength":{"type":"enum","mode":["r","w"],"value":{"w":["LOW","HIGH"]}},
              "windStrengthDetail":{"type":"enum","mode":["r","w"],"value":{"w":["NATURE","POWER"]}}}}}
            """));

        Assert.Equal("windStrengthDetail", caps.WindStrengthKey);
        Assert.Equal(["NATURE", "POWER"], caps.WindStrengths);
    }

    [Fact]
    public void Parse_ReadOnlyProperty_IsNotACapability()
    {
        var caps = AcProfileParser.Parse(Samples.Parse("""
            {"property":{
              "airConJobMode":{"currentJobMode":{"type":"enum","mode":["r"],"value":{"r":["COOL"]}}},
              "powerSave":{"powerSaveEnabled":{"type":"boolean","mode":["r"],"value":{"r":[true,false]}}}}}
            """));

        Assert.Empty(caps.Modes);
        Assert.False(caps.CanPowerSave);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"property":[]}""")]
    [InlineData("""{"property":{"temperatureInUnits":{},"airFlow":null,"timer":7}}""")]
    public void Parse_MalformedProfile_ReturnsEmptyCapabilities(string json)
    {
        var caps = AcProfileParser.Parse(Samples.Parse(json));

        Assert.False(caps.CanPower);
        Assert.Empty(caps.Modes);
        Assert.Equal(TemperatureResource.None, caps.TemperatureResource);
    }
}
```

`tests/LgController.Tests/AcStateParserTests.cs`:

```csharp
using LgController.Core;

namespace LgController.Tests;

public class AcStateParserTests
{
    private static readonly AcCapabilities Caps = AcProfileParser.Parse(Samples.Parse(Samples.Profile));

    [Fact]
    public void Parse_SampleState_ReadsAllFields()
    {
        var state = AcStateParser.Parse(Samples.Parse(Samples.State), Caps);

        Assert.True(state.IsOn);
        Assert.Equal("COOL", state.Mode);
        Assert.Equal(26.5, state.CurrentTemperature);
        Assert.Equal(23, state.TargetTemperature);
        Assert.Equal("MID", state.WindStrength);
        Assert.True(state.SwingVertical);
        Assert.False(state.SwingHorizontal);
        Assert.False(state.PowerSave);
        Assert.Equal(90, state.StopTimerMinutes);
    }

    [Fact]
    public void Parse_LegacyTemperature_ReadsFromTemperatureResource()
    {
        var caps = Caps with { TemperatureResource = TemperatureResource.Legacy };

        var state = AcStateParser.Parse(Samples.Parse(Samples.State), caps);

        Assert.Equal(26, state.CurrentTemperature);
        Assert.Equal(23, state.TargetTemperature);
    }

    [Fact]
    public void Parse_TimerUnset_HasNoMinutes()
    {
        var state = AcStateParser.Parse(Samples.Parse("""
            {"timer":{"relativeHourToStop":1,"relativeMinuteToStop":30,"relativeStopTimer":"UNSET"}}
            """), Caps);

        Assert.Null(state.StopTimerMinutes);
    }

    [Fact]
    public void Parse_TimerWithoutFlag_UsesMinutesWhenPositive()
    {
        var set = AcStateParser.Parse(Samples.Parse("""{"timer":{"relativeHourToStop":0,"relativeMinuteToStop":45}}"""), Caps);
        var unset = AcStateParser.Parse(Samples.Parse("""{"timer":{"relativeHourToStop":0,"relativeMinuteToStop":0}}"""), Caps);

        Assert.Equal(45, set.StopTimerMinutes);
        Assert.Null(unset.StopTimerMinutes);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"operation":null,"temperatureInUnits":null,"airFlow":{},"windDirection":[]}""")]
    [InlineData("""{"operation":{"airConOperationMode":"POWER_OFF"},"temperatureInUnits":[{"unit":"C","targetTemperature":null}]}""")]
    public void Parse_MissingOrNullResources_ReturnsDefaults(string json)
    {
        var state = AcStateParser.Parse(Samples.Parse(json), Caps);

        Assert.False(state.IsOn);
        Assert.Null(state.TargetTemperature);
        Assert.Null(state.CurrentTemperature);
        Assert.Null(state.Mode);
        Assert.Null(state.WindStrength);
        Assert.False(state.SwingVertical);
        Assert.Null(state.StopTimerMinutes);
    }
}
```

`tests/LgController.Tests/AcCommandsTests.cs`:

```csharp
using LgController.Core;

namespace LgController.Tests;

public class AcCommandsTests
{
    private static readonly AcCapabilities Caps = AcProfileParser.Parse(Samples.Parse(Samples.Profile));

    [Fact]
    public void Power() =>
        Assert.Equal("""{"operation":{"airConOperationMode":"POWER_ON"}}""", AcCommands.Power(true).ToJsonString());

    [Fact]
    public void PowerOff() =>
        Assert.Equal("""{"operation":{"airConOperationMode":"POWER_OFF"}}""", AcCommands.Power(false).ToJsonString());

    [Fact]
    public void Mode() =>
        Assert.Equal("""{"airConJobMode":{"currentJobMode":"FAN"}}""", AcCommands.Mode("FAN").ToJsonString());

    [Fact]
    public void Temperature_InUnits() =>
        Assert.Equal(
            """{"temperatureInUnits":{"targetTemperature":23,"unit":"C"}}""",
            AcCommands.Temperature(Caps, 23).ToJsonString());

    [Fact]
    public void Temperature_HalfDegree() =>
        Assert.Equal(
            """{"temperatureInUnits":{"targetTemperature":23.5,"unit":"C"}}""",
            AcCommands.Temperature(Caps, 23.5).ToJsonString());

    [Fact]
    public void Temperature_Legacy() =>
        Assert.Equal(
            """{"temperature":{"targetTemperature":23}}""",
            AcCommands.Temperature(Caps with { TemperatureResource = TemperatureResource.Legacy }, 23).ToJsonString());

    [Fact]
    public void WindStrength_UsesProfileKey() =>
        Assert.Equal(
            """{"airFlow":{"windStrengthDetail":"POWER"}}""",
            AcCommands.WindStrength(Caps with { WindStrengthKey = "windStrengthDetail" }, "POWER").ToJsonString());

    [Fact]
    public void Swings()
    {
        Assert.Equal("""{"windDirection":{"rotateUpDown":true}}""", AcCommands.SwingVertical(true).ToJsonString());
        Assert.Equal("""{"windDirection":{"rotateLeftRight":false}}""", AcCommands.SwingHorizontal(false).ToJsonString());
    }

    [Fact]
    public void PowerSave() =>
        Assert.Equal("""{"powerSave":{"powerSaveEnabled":true}}""", AcCommands.PowerSave(true).ToJsonString());

    [Theory]
    [InlineData(30, """{"timer":{"relativeHourToStop":0,"relativeMinuteToStop":30}}""")]
    [InlineData(120, """{"timer":{"relativeHourToStop":2}}""")]
    [InlineData(90, """{"timer":{"relativeHourToStop":1,"relativeMinuteToStop":30}}""")]
    [InlineData(0, """{"timer":{"relativeStopTimer":"UNSET"}}""")]
    public void StopTimer(int minutes, string expected) =>
        Assert.Equal(expected, AcCommands.StopTimer(minutes).ToJsonString());
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test`
Expected: FALHA de compilação, `The type or namespace name 'Core' does not exist`.

- [ ] **Step 3: Implementar**

`src/LgController/Core/AcModel.cs`:

```csharp
namespace LgController.Core;

public enum TemperatureResource { None, Legacy, InUnits }

public enum AcField { Power, Mode, Temperature, WindStrength, SwingVertical, SwingHorizontal, PowerSave, StopTimer }

/// <summary>O que o aparelho aceita como comando, segundo o perfil.</summary>
public sealed record AcCapabilities
{
    public bool CanPower { get; init; }
    public IReadOnlyList<string> Modes { get; init; } = [];
    public IReadOnlyList<string> WindStrengths { get; init; } = [];
    public string WindStrengthKey { get; init; } = "windStrength";
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
    /// <summary>Minutos até desligar; nulo quando não há timer.</summary>
    public int? StopTimerMinutes { get; init; }
}
```

`src/LgController/Core/AcProfileParser.cs`:

```csharp
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
```

`src/LgController/Core/AcStateParser.cs`:

```csharp
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
```

`src/LgController/Core/AcCommands.cs`:

```csharp
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
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test`
Expected: todos os testes passam, 0 falhas.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: air conditioner model, profile/state parsers and command payloads"
```

---

### Task 3: Probe e verificação com o aparelho real

Esta tarefa precisa do token PAT e do aparelho ligado na rede. Os passos 3 a 5 são executados pela pessoa dona do aparelho; o token fica só em uma variável de ambiente do terminal dela.

**Files:**
- Create: `tools/Probe/Program.cs`, `tools/Probe/Probe.csproj`
- Create: `tests/LgController.Tests/Fixtures/real-profile.json`, `real-state.json` (gerados pelo probe)
- Test: `tests/LgController.Tests/RealDeviceTests.cs`

**Interfaces:**
- Consumes: `ThinQClient`, `ThinQCredentials`, `ThinQException` (Tarefa 1); `AcProfileParser`, `AcStateParser`, `AcCommands` (Tarefa 2)
- Produces: os dois arquivos de fixture com o perfil e o estado reais

- [ ] **Step 1: Criar o projeto do probe**

```powershell
dotnet new console -n Probe -o tools/Probe
dotnet sln add tools/Probe
dotnet add tools/Probe reference src/LgController
```

Em `tools/Probe/Probe.csproj`, troque `<TargetFramework>net10.0</TargetFramework>` por `<TargetFramework>net10.0-windows</TargetFramework>`.

- [ ] **Step 2: Escrever o probe**

`tools/Probe/Program.cs`:

```csharp
using System.Text.Json;
using LgController.Core;
using LgController.ThinQ;

const string Usage = """
    Defina a variável de ambiente LG_THINQ_PAT (e, se não for BR, LG_THINQ_COUNTRY).
    Comandos:
      list
      dump  <deviceId> <pasta>      grava real-profile.json e real-state.json
      show  <deviceId>              mostra capacidades e estado interpretados
      power <deviceId> on|off
      temp  <deviceId> <graus>
      mode  <deviceId> <MODO>
      wind  <deviceId> <VELOCIDADE>
      swing <deviceId> on|off
      save  <deviceId> on|off
      timer <deviceId> <minutos>    0 cancela
    """;

var token = Environment.GetEnvironmentVariable("LG_THINQ_PAT");
if (string.IsNullOrWhiteSpace(token) || args.Length == 0)
{
    Console.Error.WriteLine(Usage);
    return 1;
}

var country = Environment.GetEnvironmentVariable("LG_THINQ_COUNTRY") ?? "BR";
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
var client = new ThinQClient(http, new ThinQCredentials(token.Trim(), country, $"thinq-open-{Guid.NewGuid()}"));
var indented = new JsonSerializerOptions { WriteIndented = true };

try
{
    if (args[0] == "list")
    {
        foreach (var device in await client.ListDevicesAsync())
            Console.WriteLine($"{device.Id}  {device.Type}  {device.Alias}  ({device.ModelName})");
        return 0;
    }

    if (args.Length < 2)
    {
        Console.Error.WriteLine(Usage);
        return 1;
    }

    var id = args[1];
    var profile = await client.GetProfileAsync(id);
    var caps = AcProfileParser.Parse(profile);
    var arg = args.Length > 2 ? args[2] : "";

    switch (args[0])
    {
        case "dump":
            Directory.CreateDirectory(arg);
            File.WriteAllText(Path.Combine(arg, "real-profile.json"), JsonSerializer.Serialize(profile, indented));
            File.WriteAllText(
                Path.Combine(arg, "real-state.json"),
                JsonSerializer.Serialize(await client.GetStateAsync(id), indented));
            Console.WriteLine($"Gravado em {Path.GetFullPath(arg)}");
            break;
        case "show":
            Console.WriteLine(caps);
            Console.WriteLine($"Modos: {string.Join(", ", caps.Modes)}");
            Console.WriteLine($"Vento: {string.Join(", ", caps.WindStrengths)}");
            Console.WriteLine(AcStateParser.Parse(await client.GetStateAsync(id), caps));
            break;
        case "power":
            await client.ControlAsync(id, AcCommands.Power(arg == "on"));
            break;
        case "temp":
            await client.ControlAsync(id, AcCommands.Temperature(caps, double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture)));
            break;
        case "mode":
            await client.ControlAsync(id, AcCommands.Mode(arg));
            break;
        case "wind":
            await client.ControlAsync(id, AcCommands.WindStrength(caps, arg));
            break;
        case "swing":
            await client.ControlAsync(id, AcCommands.SwingVertical(arg == "on"));
            break;
        case "save":
            await client.ControlAsync(id, AcCommands.PowerSave(arg == "on"));
            break;
        case "timer":
            await client.ControlAsync(id, AcCommands.StopTimer(int.Parse(arg)));
            break;
        default:
            Console.Error.WriteLine(Usage);
            return 1;
    }

    Console.WriteLine("OK");
    return 0;
}
catch (ThinQException ex)
{
    Console.Error.WriteLine($"Erro {ex.Code} ({ex.Kind}): {ex.Message}");
    return 2;
}
```

Run: `dotnet build`
Expected: `Build succeeded`.

- [ ] **Step 3: (Pessoa) gerar o token e capturar o perfil e o estado**

1. Abrir `https://connect-pat.lgthinq.com`, entrar com a conta LG e criar um token com todas as permissões marcadas.
2. No próprio terminal:

```powershell
$env:LG_THINQ_PAT = "<token>"
dotnet run --project tools/Probe -- list
dotnet run --project tools/Probe -- dump <deviceId> tests/LgController.Tests/Fixtures
dotnet run --project tools/Probe -- show <deviceId>
```

Expected: `list` mostra uma linha com `DEVICE_AIR_CONDITIONER`; `dump` grava os dois arquivos; `show` mostra modos e velocidades não vazios e a temperatura alvo igual à do aparelho.

- [ ] **Step 4: (Pessoa) testar cada comando no aparelho**

```powershell
dotnet run --project tools/Probe -- power <deviceId> on
dotnet run --project tools/Probe -- temp  <deviceId> 23
dotnet run --project tools/Probe -- wind  <deviceId> <uma velocidade listada em show>
dotnet run --project tools/Probe -- mode  <deviceId> <um modo listado em show>
dotnet run --project tools/Probe -- swing <deviceId> on
dotnet run --project tools/Probe -- save  <deviceId> on
dotnet run --project tools/Probe -- timer <deviceId> 30
dotnet run --project tools/Probe -- timer <deviceId> 0
dotnet run --project tools/Probe -- power <deviceId> off
```

Expected: cada comando imprime `OK` e o aparelho obedece. Um comando para um recurso que `show` marcou como `False` pode ser pulado. Anotar qualquer comando que devolva `Erro`, com o código.

- [ ] **Step 5: Escrever o teste com o perfil real**

`tests/LgController.Tests/RealDeviceTests.cs`:

```csharp
using LgController.Core;

namespace LgController.Tests;

/// <summary>Usa o perfil e o estado capturados do aparelho real pelo probe.</summary>
public class RealDeviceTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void RealProfile_HasTheCoreCapabilities()
    {
        var caps = AcProfileParser.Parse(Samples.Parse(Fixture("real-profile.json")));

        Assert.True(caps.CanPower);
        Assert.NotEmpty(caps.Modes);
        Assert.NotEmpty(caps.WindStrengths);
        Assert.NotEqual(TemperatureResource.None, caps.TemperatureResource);
        Assert.True(caps.MaxTemperature > caps.MinTemperature);
    }

    [Fact]
    public void RealState_ParsesTemperatureAndMode()
    {
        var caps = AcProfileParser.Parse(Samples.Parse(Fixture("real-profile.json")));

        var state = AcStateParser.Parse(Samples.Parse(Fixture("real-state.json")), caps);

        Assert.NotNull(state.TargetTemperature);
        Assert.InRange(state.TargetTemperature!.Value, caps.MinTemperature, caps.MaxTemperature);
        Assert.Contains(state.Mode, caps.Modes);
    }
}
```

Run: `dotnet test`
Expected: todos os testes passam.

- [ ] **Step 6: Corrigir divergências, se houver**

Se um teste de `RealDeviceTests` falhar ou um comando do passo 4 tiver sido rejeitado, o formato real difere do presumido. Abra `real-profile.json` e `real-state.json`, acrescente em `AcProfileParserTests`, `AcStateParserTests` ou `AcCommandsTests` um teste com o trecho real que reproduz a diferença, veja falhar, corrija o parser ou o comando correspondente e rode `dotnet test` até passar. Atualize também a tabela de payloads da spec. Se não houve divergência, este passo não muda nada.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: probe tool and real device fixtures"
```

---

### Task 4: Configuração, mensagens e utilitários

**Files:**
- Create: `src/LgController/Core/SettingsStore.cs`, `ErrorMessages.cs`, `ModePalette.cs`, `AutoStart.cs`, `Log.cs`
- Test: `tests/LgController.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Consumes: `ThinQException`, `ThinQErrorKind` (Tarefa 1)
- Produces:
  - `record AppSettings(string Token, string CountryCode, string ClientId, string DeviceId, string DeviceAlias)`
  - `SettingsStore(string? directory = null)` com `Load() : AppSettings?`, `Save(AppSettings)`, `static NewClientId() : string`, `static DefaultDirectory : string`
  - `ErrorMessages.For(ThinQException) : string`
  - `ModePalette.For(string? mode, bool isOn) : (byte R, byte G, byte B)`
  - `AutoStart.IsEnabled : bool`, `AutoStart.Set(bool)`
  - `Log.Write(string)`, `Log.Write(Exception)`

- [ ] **Step 1: Escrever os testes que falham**

`tests/LgController.Tests/SettingsStoreTests.cs`:

```csharp
using LgController.Core;
using LgController.ThinQ;

namespace LgController.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lgc-tests-" + Guid.NewGuid());
    private static readonly AppSettings Settings = new("segredo-pat", "BR", "thinq-open-1", "dev-1", "Quarto");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Load_WithoutFile_ReturnsNull() => Assert.Null(new SettingsStore(_dir).Load());

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        new SettingsStore(_dir).Save(Settings);

        Assert.Equal(Settings, new SettingsStore(_dir).Load());
    }

    [Fact]
    public void Save_DoesNotWriteTokenInClearText()
    {
        new SettingsStore(_dir).Save(Settings);

        Assert.DoesNotContain("segredo-pat", File.ReadAllText(Path.Combine(_dir, "settings.json")));
    }

    [Theory]
    [InlineData("isto não é json")]
    [InlineData("{}")]
    [InlineData("""{"token":"AAAA","countryCode":"BR","clientId":"c","deviceId":"d","deviceAlias":"a"}""")]
    [InlineData("""{"token":"não é base64!","countryCode":"BR","clientId":"c","deviceId":"d","deviceAlias":"a"}""")]
    [InlineData("null")]
    public void Load_CorruptOrUndecryptableFile_ReturnsNull(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), content);

        Assert.Null(new SettingsStore(_dir).Load());
    }

    [Fact]
    public void NewClientId_HasPrefixAndIsUnique()
    {
        var a = SettingsStore.NewClientId();

        Assert.StartsWith("thinq-open-", a);
        Assert.NotEqual(a, SettingsStore.NewClientId());
    }

    [Theory]
    [InlineData(ThinQErrorKind.Network, "Sem conexão com a internet.")]
    [InlineData(ThinQErrorKind.Unauthorized, "Token inválido ou expirado.")]
    [InlineData(ThinQErrorKind.DeviceOffline, "O aparelho está offline.")]
    [InlineData(ThinQErrorKind.RateLimited, "Muitas requisições. Aguarde um instante.")]
    [InlineData(ThinQErrorKind.CommandRejected, "O aparelho recusou o comando.")]
    [InlineData(ThinQErrorKind.Unknown, "Erro da LG: falhou (2000)")]
    public void ErrorMessages_AreInPortuguese(ThinQErrorKind kind, string expected) =>
        Assert.Equal(expected, ErrorMessages.For(new ThinQException("2000", "falhou", kind)));

    [Fact]
    public void ModePalette_OffIsGrey_AndModesDiffer()
    {
        Assert.Equal(((byte)0x6B, (byte)0x72, (byte)0x80), ModePalette.For("COOL", isOn: false));
        Assert.NotEqual(ModePalette.For("COOL", true), ModePalette.For("HEAT", true));
        Assert.Equal(ModePalette.For("COOL", true), ModePalette.For("DESCONHECIDO", true));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test`
Expected: FALHA de compilação, `'SettingsStore' could not be found`.

- [ ] **Step 3: Implementar**

`src/LgController/Core/SettingsStore.cs`:

```csharp
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LgController.Core;

public sealed record AppSettings(string Token, string CountryCode, string ClientId, string DeviceId, string DeviceAlias);

public sealed class SettingsStore(string? directory = null)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string DefaultDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LgController");

    private readonly string _directory = directory ?? DefaultDirectory;

    private string FilePath => Path.Combine(_directory, "settings.json");

    public static string NewClientId() => $"thinq-open-{Guid.NewGuid()}";

    /// <summary>Nulo quando não há configuração utilizável; quem chama abre a tela de configuração.</summary>
    public AppSettings? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var file = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
            if (file is null
                || string.IsNullOrEmpty(file.Token)
                || string.IsNullOrEmpty(file.CountryCode)
                || string.IsNullOrEmpty(file.ClientId)
                || string.IsNullOrEmpty(file.DeviceId))
            {
                return null;
            }

            var token = Encoding.UTF8.GetString(
                ProtectedData.Unprotect(Convert.FromBase64String(file.Token), null, DataProtectionScope.CurrentUser));
            return file with { Token = token, DeviceAlias = file.DeviceAlias ?? "" };
        }
        catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException or IOException
                                       or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(_directory);
        var protectedToken = Convert.ToBase64String(
            ProtectedData.Protect(Encoding.UTF8.GetBytes(settings.Token), null, DataProtectionScope.CurrentUser));
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings with { Token = protectedToken }, Options));
    }
}
```

Se o build acusar que `ProtectedData` não existe, rode `dotnet add src/LgController package System.Security.Cryptography.ProtectedData`.

`src/LgController/Core/ErrorMessages.cs`:

```csharp
using LgController.ThinQ;

namespace LgController.Core;

public static class ErrorMessages
{
    public static string For(ThinQException ex) => ex.Kind switch
    {
        ThinQErrorKind.Network => "Sem conexão com a internet.",
        ThinQErrorKind.Unauthorized => "Token inválido ou expirado.",
        ThinQErrorKind.DeviceOffline => "O aparelho está offline.",
        ThinQErrorKind.RateLimited => "Muitas requisições. Aguarde um instante.",
        ThinQErrorKind.CommandRejected => "O aparelho recusou o comando.",
        _ => $"Erro da LG: {ex.Message} ({ex.Code})",
    };
}
```

`src/LgController/Core/ModePalette.cs`:

```csharp
namespace LgController.Core;

/// <summary>Cor de destaque de cada modo, usada pelo painel e pelo ícone da bandeja.</summary>
public static class ModePalette
{
    public static (byte R, byte G, byte B) For(string? mode, bool isOn)
    {
        if (!isOn) return (0x6B, 0x72, 0x80);
        return mode switch
        {
            "HEAT" => (0xFF, 0x8A, 0x3D),
            "AIR_DRY" => (0x2C, 0xC5, 0xB5),
            "FAN" => (0x8A, 0x93, 0xA0),
            "AUTO" => (0x7C, 0x6C, 0xFF),
            "AIR_CLEAN" => (0x4C, 0xC3, 0x6B),
            _ => (0x3B, 0x9E, 0xFF),
        };
    }
}
```

`src/LgController/Core/AutoStart.cs`:

```csharp
using Microsoft.Win32;

namespace LgController.Core;

public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LgController";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
```

`src/LgController/Core/Log.cs`:

```csharp
using System.IO;

namespace LgController.Core;

public static class Log
{
    private static readonly object Gate = new();

    public static void Write(Exception exception) => Write(exception.ToString());

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(SettingsStore.DefaultDirectory);
                File.AppendAllText(
                    Path.Combine(SettingsStore.DefaultDirectory, "log.txt"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // O log não pode derrubar o aplicativo.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test`
Expected: todos os testes passam.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: settings store with DPAPI, error messages, palette, autostart, log"
```

---

### Task 5: AcController

**Files:**
- Create: `src/LgController/Core/AcController.cs`
- Test: `tests/LgController.Tests/FakeThinQClient.cs`, `AcControllerTests.cs`

**Interfaces:**
- Consumes: `IThinQClient`, `ThinQException` (Tarefa 1); `AcCapabilities`, `AcState`, `AcField`, `AcProfileParser`, `AcStateParser`, `AcCommands` (Tarefa 2)
- Produces:
  - `AcController(IThinQClient client, string deviceId, TimeProvider time, Action<Action>? dispatch = null)`
  - Propriedades `Capabilities : AcCapabilities?`, `State : AcState?`, `ConnectionError : ThinQException?`
  - Eventos `StateChanged : Action`, `ErrorOccurred : Action<ThinQException>`
  - `Task RefreshAsync()`, `void Start()`, `void SetPanelOpen(bool open)`, `void Dispose()`
  - `SetPower(bool)`, `SetMode(string)`, `SetTargetTemperature(double)`, `SetWindStrength(string)`, `SetSwingVertical(bool)`, `SetSwingHorizontal(bool)`, `SetPowerSave(bool)`, `SetStopTimer(int minutes)`
  - `FakeThinQClient` para os testes desta e das próximas tarefas

Modelo: `_confirmed` é o último estado vindo da LG. Cada comando ainda não confirmado fica em `_pending`, um por `AcField`. `State` é `_confirmed` com os pendentes aplicados por cima. Assim o polling nunca sobrescreve um comando pendente, e desfazer um comando que falhou é só removê-lo de `_pending`.

O `dispatch` leva o trabalho dos timers para a thread da UI; nos testes ele executa na hora.

- [ ] **Step 1: Escrever os testes que falham**

`tests/LgController.Tests/FakeThinQClient.cs`:

```csharp
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
    /// <summary>Quando definido, o envio fica em voo até o teste completar a tarefa.</summary>
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
```

`tests/LgController.Tests/AcControllerTests.cs`:

```csharp
using LgController.Core;
using LgController.ThinQ;
using Microsoft.Extensions.Time.Testing;

namespace LgController.Tests;

public class AcControllerTests
{
    private readonly FakeThinQClient _client = new();
    private readonly FakeTimeProvider _time = new();
    private readonly AcController _controller;

    public AcControllerTests() => _controller = new AcController(_client, "dev-1", _time);

    private static string Temp(double value) =>
        $$"""{"temperatureInUnits":{"targetTemperature":{{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"unit":"C"}}""";

    private static readonly ThinQException Rejected = new("2208", "falhou", ThinQErrorKind.CommandRejected);

    [Fact]
    public async Task Refresh_LoadsCapabilitiesAndState()
    {
        var changes = 0;
        _controller.StateChanged += () => changes++;

        await _controller.RefreshAsync();

        Assert.Equal(23, _controller.State!.TargetTemperature);
        Assert.Equal(30, _controller.Capabilities!.MaxTemperature);
        Assert.Null(_controller.ConnectionError);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Refresh_Failure_SetsConnectionErrorAndKeepsLastState()
    {
        await _controller.RefreshAsync();
        _client.StateError = new ThinQException("network", "x", ThinQErrorKind.Network);

        await _controller.RefreshAsync();

        Assert.Equal(ThinQErrorKind.Network, _controller.ConnectionError!.Kind);
        Assert.Equal(23, _controller.State!.TargetTemperature);
    }

    [Fact]
    public void Commands_BeforeFirstRefresh_AreIgnored()
    {
        _controller.SetTargetTemperature(25);
        _time.Advance(AcController.DebounceDelay);

        Assert.Null(_controller.State);
        Assert.Empty(_client.Controls);
    }

    [Fact]
    public async Task SetTargetTemperature_UpdatesStateImmediately_AndSendsAfterDebounce()
    {
        await _controller.RefreshAsync();

        _controller.SetTargetTemperature(25);

        Assert.Equal(25, _controller.State!.TargetTemperature);
        Assert.Empty(_client.Controls);

        _time.Advance(AcController.DebounceDelay);

        Assert.Equal([Temp(25)], _client.Controls);
    }

    [Fact]
    public async Task RapidChanges_SendOnlyTheFinalValue()
    {
        await _controller.RefreshAsync();

        _controller.SetTargetTemperature(24);
        _time.Advance(TimeSpan.FromMilliseconds(300));
        _controller.SetTargetTemperature(25);
        _time.Advance(TimeSpan.FromMilliseconds(300));
        _controller.SetTargetTemperature(26);
        _time.Advance(AcController.DebounceDelay);

        Assert.Equal([Temp(26)], _client.Controls);
    }

    [Fact]
    public async Task DifferentFields_AreSentSeparately()
    {
        await _controller.RefreshAsync();

        _controller.SetTargetTemperature(25);
        _controller.SetWindStrength("HIGH");
        _time.Advance(AcController.DebounceDelay);

        Assert.Equal(2, _client.Controls.Count);
        Assert.Contains("""{"airFlow":{"windStrength":"HIGH"}}""", _client.Controls);
        Assert.Equal("HIGH", _controller.State!.WindStrength);
    }

    [Fact]
    public async Task FailedCommand_RestoresPreviousValue_AndRaisesError()
    {
        await _controller.RefreshAsync();
        ThinQException? raised = null;
        _controller.ErrorOccurred += ex => raised = ex;
        _client.ControlError = Rejected;

        _controller.SetTargetTemperature(25);
        _time.Advance(AcController.DebounceDelay);

        Assert.Equal(23, _controller.State!.TargetTemperature);
        Assert.Same(Rejected, raised);
    }

    [Fact]
    public async Task Poll_DoesNotOverwritePendingCommand()
    {
        await _controller.RefreshAsync();
        _controller.SetTargetTemperature(26);
        _client.StateJson = Samples.State.Replace("\"targetTemperature\": 23", "\"targetTemperature\": 20");

        await _controller.RefreshAsync();

        Assert.Equal(26, _controller.State!.TargetTemperature);
    }

    [Fact]
    public async Task SuccessfulCommand_KeepsValue_AndConfirmsAfterDelay()
    {
        await _controller.RefreshAsync();
        var callsBefore = _client.StateCalls;

        _controller.SetTargetTemperature(25);
        _time.Advance(AcController.DebounceDelay);

        Assert.Equal(25, _controller.State!.TargetTemperature);
        Assert.Equal(callsBefore, _client.StateCalls);

        _client.StateJson = Samples.State.Replace("\"targetTemperature\": 23", "\"targetTemperature\": 25");
        _time.Advance(AcController.ConfirmDelay);

        Assert.Equal(callsBefore + 1, _client.StateCalls);
        Assert.Equal(25, _controller.State!.TargetTemperature);
    }

    [Theory]
    [InlineData(99, 30)]
    [InlineData(-5, 16)]
    [InlineData(23.4, 23.5)]
    [InlineData(23.2, 23)]
    [InlineData(30.4, 30)]
    public async Task SetTargetTemperature_ClampsToRangeAndSnapsToStep(double requested, double expected)
    {
        await _controller.RefreshAsync();

        _controller.SetTargetTemperature(requested);
        _time.Advance(AcController.DebounceDelay);

        Assert.Equal(expected, _controller.State!.TargetTemperature);
        Assert.Equal([Temp(expected)], _client.Controls);
    }

    [Fact]
    public async Task OlderCommandFailing_DoesNotDiscardNewerValue()
    {
        await _controller.RefreshAsync();
        var gate = new TaskCompletionSource();
        _client.ControlGate = gate;

        _controller.SetTargetTemperature(24);
        _time.Advance(AcController.DebounceDelay);
        _controller.SetTargetTemperature(25);

        _client.ControlError = Rejected;
        gate.SetResult();

        Assert.Equal(25, _controller.State!.TargetTemperature);

        _client.ControlGate = null;
        _client.ControlError = null;
        _time.Advance(AcController.DebounceDelay);

        Assert.Equal([Temp(24), Temp(25)], _client.Controls);
        Assert.Equal(25, _controller.State!.TargetTemperature);
    }

    [Fact]
    public async Task Polling_UsesClosedIntervalThenOpenInterval()
    {
        await _controller.RefreshAsync();
        _controller.Start();
        var calls = _client.StateCalls;

        _time.Advance(AcController.ClosedPollInterval);
        Assert.Equal(calls + 1, _client.StateCalls);

        _controller.SetPanelOpen(true);
        Assert.Equal(calls + 2, _client.StateCalls);

        _time.Advance(AcController.OpenPollInterval);
        Assert.Equal(calls + 3, _client.StateCalls);
    }

    [Fact]
    public async Task RateLimit_PausesPollingForOneMinute()
    {
        await _controller.RefreshAsync();
        _controller.Start();
        _client.StateError = new ThinQException("1306", "limite", ThinQErrorKind.RateLimited);
        _controller.SetPanelOpen(true);
        var calls = _client.StateCalls;
        _client.StateError = null;

        _time.Advance(AcController.OpenPollInterval);
        Assert.Equal(calls, _client.StateCalls);

        _time.Advance(AcController.OpenPollInterval);
        Assert.Equal(calls + 1, _client.StateCalls);
        Assert.Null(_controller.ConnectionError);
    }

    [Fact]
    public async Task OtherSetters_ApplyOptimistically()
    {
        await _controller.RefreshAsync();

        _controller.SetPower(false);
        _controller.SetMode("FAN");
        _controller.SetSwingVertical(false);
        _controller.SetSwingHorizontal(true);
        _controller.SetPowerSave(true);
        _controller.SetStopTimer(0);

        var state = _controller.State!;
        Assert.False(state.IsOn);
        Assert.Equal("FAN", state.Mode);
        Assert.False(state.SwingVertical);
        Assert.True(state.SwingHorizontal);
        Assert.True(state.PowerSave);
        Assert.Null(state.StopTimerMinutes);

        _time.Advance(AcController.DebounceDelay);
        Assert.Equal(6, _client.Controls.Count);
    }

    [Fact]
    public async Task Dispose_CancelsPendingSends()
    {
        await _controller.RefreshAsync();
        _controller.SetTargetTemperature(25);

        _controller.Dispose();
        _time.Advance(AcController.DebounceDelay);

        Assert.Empty(_client.Controls);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test`
Expected: FALHA de compilação, `'AcController' could not be found`.

- [ ] **Step 3: Implementar**

`src/LgController/Core/AcController.cs`:

```csharp
using System.Text.Json.Nodes;
using LgController.ThinQ;

namespace LgController.Core;

/// <summary>
/// Guarda o estado do aparelho, aplica comandos de forma otimista e os envia agrupados.
/// Todo acesso deve acontecer na thread para a qual <c>dispatch</c> encaminha.
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

    /// <summary>Erro da última leitura; nulo quando a última leitura deu certo.</summary>
    public ThinQException? ConnectionError { get; private set; }

    /// <summary>Último estado confirmado com os comandos pendentes aplicados por cima.</summary>
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
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test`
Expected: todos os testes passam.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: AcController with optimistic updates, debounce and polling"
```

---

### Task 6: Bandeja, temas e tela de configuração

Ao final desta tarefa o aplicativo roda: pede a configuração na primeira vez, mostra o ícone na bandeja com a temperatura, e o menu do botão direito funciona. O clique esquerdo passa a abrir o painel na Tarefa 7.

**Files:**
- Create: `src/LgController/UI/Themes/Dark.xaml`, `Light.xaml`, `Controls.xaml`
- Create: `src/LgController/UI/ThemeManager.cs`, `TrayIconRenderer.cs`, `TrayIcon.cs`, `SetupViewModel.cs`, `SetupWindow.xaml`, `SetupWindow.xaml.cs`
- Modify: `src/LgController/App.xaml`, `src/LgController/App.xaml.cs`
- Test: `tests/LgController.Tests/SetupViewModelTests.cs`

**Interfaces:**
- Consumes: `IThinQClient`, `ThinQClient`, `ThinQCredentials`, `DeviceInfo`, `ThinQException` (Tarefa 1); `AcState` (Tarefa 2); `AppSettings`, `SettingsStore`, `ErrorMessages`, `ModePalette`, `AutoStart`, `Log` (Tarefa 4); `AcController` (Tarefa 5)
- Produces:
  - Pincéis `PanelTintBrush`, `PanelSolidBrush`, `CardBrush`, `CardHoverBrush`, `TrackBrush`, `TextBrush`, `SubtleTextBrush`, `AccentBrush`, `BannerBrush`
  - Estilos `ChipButton`, `RoundButton`, `PowerButton`, `Switch`, `PrimaryButton`, `Section`; conversor `BoolVis`
  - `ThemeManager.Initialize(Application)`, `ThemeManager.IsDark`, evento `ThemeManager.Changed`
  - `TrayIcon(Action toggleFlyout, Action togglePower, Action openSettings, Action exit)` com `Update(AcState? state, bool hasError, string alias)` e `Dispose()`
  - `SetupViewModel(Func<ThinQCredentials, IThinQClient> clientFactory, SettingsStore store, AppSettings? existing)` com `Token`, `CountryCode`, `Devices`, `SelectedDevice`, `ErrorText`, `IsBusy`, `ConnectCommand`, `SaveCommand`, `Result : AppSettings?`, evento `Saved`
  - `App.RunSetup(AppSettings? existing) : AppSettings?`, `App.StartSession(AppSettings)`, `App.StopSession()`

- [ ] **Step 1: Escrever os testes que falham**

`tests/LgController.Tests/SetupViewModelTests.cs`:

```csharp
using LgController.Core;
using LgController.ThinQ;
using LgController.UI;

namespace LgController.Tests;

public sealed class SetupViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lgc-setup-" + Guid.NewGuid());
    private readonly FakeThinQClient _client = new();
    private ThinQCredentials? _used;

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private SetupViewModel Create(AppSettings? existing = null) =>
        new(creds => { _used = creds; return _client; }, new SettingsStore(_dir), existing);

    private static readonly DeviceInfo Ac1 = new("ac-1", "DEVICE_AIR_CONDITIONER", "Quarto", "RAC");
    private static readonly DeviceInfo Ac2 = new("ac-2", "DEVICE_AIR_CONDITIONER", "Sala", "RAC");
    private static readonly DeviceInfo Washer = new("w-1", "DEVICE_WASHER", "Lavadora", "W");

    [Fact]
    public async Task Connect_ListsOnlyAirConditioners_AndSelectsTheOnlyOne()
    {
        _client.Devices.AddRange([Washer, Ac1]);
        var vm = Create();
        vm.Token = "  pat-1  ";
        vm.CountryCode = "br";

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal([Ac1], vm.Devices);
        Assert.Equal(Ac1, vm.SelectedDevice);
        Assert.Null(vm.ErrorText);
        Assert.Equal("pat-1", _used!.Token);
        Assert.Equal("BR", _used.CountryCode);
        Assert.StartsWith("thinq-open-", _used.ClientId);
    }

    [Fact]
    public async Task Connect_WithSeveralAirConditioners_LeavesSelectionToTheUser()
    {
        _client.Devices.AddRange([Ac1, Ac2]);
        var vm = Create();
        vm.Token = "pat";

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Devices.Count);
        Assert.Null(vm.SelectedDevice);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Connect_WithoutToken_ShowsError()
    {
        var vm = Create();
        vm.Token = "   ";

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("Informe o token.", vm.ErrorText);
        Assert.Null(_used);
    }

    [Fact]
    public async Task Connect_UnsupportedCountry_ShowsError()
    {
        var vm = Create();
        vm.Token = "pat";
        vm.CountryCode = "ZZ";

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("País não suportado. Use o código de duas letras, como BR.", vm.ErrorText);
    }

    [Fact]
    public async Task Connect_NoAirConditioner_ShowsError()
    {
        _client.Devices.Add(Washer);
        var vm = Create();
        vm.Token = "pat";

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("Nenhum ar-condicionado encontrado nesta conta.", vm.ErrorText);
        Assert.Empty(vm.Devices);
    }

    [Fact]
    public async Task Connect_ApiError_ShowsTranslatedMessage()
    {
        _client.ListError = new ThinQException("1103", "invalid", ThinQErrorKind.Unauthorized);
        var vm = Create();
        vm.Token = "pat";

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("Token inválido ou expirado.", vm.ErrorText);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Save_PersistsSettings_AndRaisesSaved()
    {
        _client.Devices.Add(Ac1);
        var vm = Create();
        vm.Token = "pat-1";
        var saved = false;
        vm.Saved += () => saved = true;
        await vm.ConnectCommand.ExecuteAsync(null);

        vm.SaveCommand.Execute(null);

        Assert.True(saved);
        var stored = new SettingsStore(_dir).Load()!;
        Assert.Equal(vm.Result, stored);
        Assert.Equal("pat-1", stored.Token);
        Assert.Equal("ac-1", stored.DeviceId);
        Assert.Equal("Quarto", stored.DeviceAlias);
    }

    [Fact]
    public void ExistingSettings_PrefillFieldsAndKeepClientId()
    {
        var vm = Create(new AppSettings("old-pat", "PT", "thinq-open-keep", "ac-9", "Antigo"));

        Assert.Equal("old-pat", vm.Token);
        Assert.Equal("PT", vm.CountryCode);
    }

    [Fact]
    public async Task ExistingSettings_ReuseClientId()
    {
        _client.Devices.Add(Ac1);
        var vm = Create(new AppSettings("old-pat", "BR", "thinq-open-keep", "ac-9", "Antigo"));

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("thinq-open-keep", _used!.ClientId);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test`
Expected: FALHA de compilação, `The type or namespace name 'UI' does not exist`.

- [ ] **Step 3: Temas e estilos**

`src/LgController/UI/Themes/Dark.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="PanelTintBrush" Color="#B31C1C1E" />
    <SolidColorBrush x:Key="PanelSolidBrush" Color="#FF202022" />
    <SolidColorBrush x:Key="CardBrush" Color="#14FFFFFF" />
    <SolidColorBrush x:Key="CardHoverBrush" Color="#24FFFFFF" />
    <SolidColorBrush x:Key="TrackBrush" Color="#1FFFFFFF" />
    <SolidColorBrush x:Key="TextBrush" Color="#FFFFFFFF" />
    <SolidColorBrush x:Key="SubtleTextBrush" Color="#99FFFFFF" />
    <SolidColorBrush x:Key="AccentBrush" Color="#FF3B9EFF" />
    <SolidColorBrush x:Key="BannerBrush" Color="#40FF5A5A" />
</ResourceDictionary>
```

`src/LgController/UI/Themes/Light.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="PanelTintBrush" Color="#B3F6F6F8" />
    <SolidColorBrush x:Key="PanelSolidBrush" Color="#FFF6F6F8" />
    <SolidColorBrush x:Key="CardBrush" Color="#0F000000" />
    <SolidColorBrush x:Key="CardHoverBrush" Color="#1C000000" />
    <SolidColorBrush x:Key="TrackBrush" Color="#1F000000" />
    <SolidColorBrush x:Key="TextBrush" Color="#FF1A1A1C" />
    <SolidColorBrush x:Key="SubtleTextBrush" Color="#99000000" />
    <SolidColorBrush x:Key="AccentBrush" Color="#FF3B9EFF" />
    <SolidColorBrush x:Key="BannerBrush" Color="#33D92C2C" />
</ResourceDictionary>
```

`src/LgController/UI/Themes/Controls.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <BooleanToVisibilityConverter x:Key="BoolVis" />
    <FontFamily x:Key="IconFont">Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>

    <!-- Bloco do painel que esmaece quando desabilitado. -->
    <Style x:Key="Section" TargetType="FrameworkElement">
        <Style.Triggers>
            <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Opacity" Value="0.4" />
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- Opção de seletor; o DataContext é um OptionItem. -->
    <Style x:Key="ChipButton" TargetType="Button">
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
        <Setter Property="Background" Value="{DynamicResource CardBrush}" />
        <Setter Property="Margin" Value="3" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="FocusVisualStyle" Value="{x:Null}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="Bd" CornerRadius="12" Background="{TemplateBinding Background}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="{DynamicResource CardHoverBrush}" />
                        </Trigger>
                        <DataTrigger Binding="{Binding IsSelected}" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="{DynamicResource AccentBrush}" />
                            <Setter Property="Foreground" Value="White" />
                        </DataTrigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="RoundButton" TargetType="Button">
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
        <Setter Property="Width" Value="44" />
        <Setter Property="Height" Value="44" />
        <Setter Property="FontSize" Value="22" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="FocusVisualStyle" Value="{x:Null}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="Bd" CornerRadius="22" Background="{DynamicResource CardBrush}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="{DynamicResource CardHoverBrush}" />
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="{DynamicResource AccentBrush}" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="PowerButton" TargetType="ToggleButton">
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
        <Setter Property="Width" Value="48" />
        <Setter Property="Height" Value="48" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="FocusVisualStyle" Value="{x:Null}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ToggleButton">
                    <Border x:Name="Bd" CornerRadius="24" Background="{DynamicResource CardBrush}">
                        <TextBlock Text="&#xE7E8;" FontFamily="{StaticResource IconFont}" FontSize="20"
                                   HorizontalAlignment="Center" VerticalAlignment="Center" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="{DynamicResource CardHoverBrush}" />
                        </Trigger>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="{DynamicResource AccentBrush}" />
                            <Setter Property="Foreground" Value="White" />
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.4" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="Switch" TargetType="CheckBox">
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
        <Setter Property="FontSize" Value="14" />
        <Setter Property="Margin" Value="0,7" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="FocusVisualStyle" Value="{x:Null}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="CheckBox">
                    <Grid Background="Transparent">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <ContentPresenter VerticalAlignment="Center" />
                        <Border x:Name="Track" Grid.Column="1" Width="40" Height="22" CornerRadius="11"
                                Background="{DynamicResource TrackBrush}">
                            <Ellipse x:Name="Thumb" Width="16" Height="16" Margin="3,0" HorizontalAlignment="Left"
                                     Fill="{DynamicResource SubtleTextBrush}" />
                        </Border>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="Track" Property="Background" Value="{DynamicResource AccentBrush}" />
                            <Setter TargetName="Thumb" Property="HorizontalAlignment" Value="Right" />
                            <Setter TargetName="Thumb" Property="Fill" Value="White" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="PrimaryButton" TargetType="Button">
        <Setter Property="Foreground" Value="White" />
        <Setter Property="Padding" Value="18,8" />
        <Setter Property="FontSize" Value="14" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="Bd" CornerRadius="8" Padding="{TemplateBinding Padding}"
                            Background="{DynamicResource AccentBrush}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Bd" Property="Opacity" Value="0.85" />
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter TargetName="Bd" Property="Opacity" Value="0.4" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
</ResourceDictionary>
```

Substitua `src/LgController/App.xaml` por:

```xml
<Application x:Class="LgController.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <!-- O índice 0 é o tema; ThemeManager o substitui. -->
                <ResourceDictionary Source="/UI/Themes/Dark.xaml" />
                <ResourceDictionary Source="/UI/Themes/Controls.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

`src/LgController/UI/ThemeManager.cs`:

```csharp
using System.Windows;
using Microsoft.Win32;

namespace LgController.UI;

/// <summary>Mantém o dicionário de tema igual ao tema de aplicativos do Windows.</summary>
public static class ThemeManager
{
    private const string PersonalizeKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static event Action? Changed;

    public static bool IsDark { get; private set; } = true;

    public static void Initialize(Application app)
    {
        Apply(app);
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General) app.Dispatcher.BeginInvoke(() => Apply(app));
        };
    }

    private static void Apply(Application app)
    {
        IsDark = Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1) is int light && light == 0;
        var name = IsDark ? "Dark" : "Light";
        app.Resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/UI/Themes/{name}.xaml"),
        };
        Changed?.Invoke();
    }
}
```

- [ ] **Step 4: Ícone da bandeja**

`src/LgController/UI/TrayIconRenderer.cs`:

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace LgController.UI;

public static class TrayIconRenderer
{
    private const string PowerGlyph = "";

    /// <summary>Quadrado arredondado na cor dada, com a temperatura ou, se <paramref name="text"/> for nulo, o símbolo de energia.</summary>
    public static Icon Render(string? text, (byte R, byte G, byte B) color, bool alert)
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            using var background = RoundedRectangle(new RectangleF(1, 1, 30, 30), 8);
            using var fill = new SolidBrush(Color.FromArgb(color.R, color.G, color.B));
            g.FillPath(fill, background);

            using var font = text is null
                ? new Font("Segoe MDL2 Assets", 17, FontStyle.Regular, GraphicsUnit.Pixel)
                : new Font("Segoe UI", text.Length > 2 ? 13 : 19, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text ?? PowerGlyph, font, Brushes.White, new RectangleF(0, 1, 32, 32), format);

            if (alert)
            {
                g.FillEllipse(Brushes.Black, 19, 19, 13, 13);
                g.FillEllipse(Brushes.Gold, 21, 21, 9, 9);
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
```

`src/LgController/UI/TrayIcon.cs`:

```csharp
using System.Globalization;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using LgController.Core;

namespace LgController.UI;

public sealed class TrayIcon : IDisposable
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    private readonly TaskbarIcon _icon = new();
    private readonly MenuItem _powerItem;
    private System.Drawing.Icon? _current;
    private string? _renderKey;

    public TrayIcon(Action toggleFlyout, Action togglePower, Action openSettings, Action exit)
    {
        _powerItem = new MenuItem { Header = "Ligar" };
        _powerItem.Click += (_, _) => togglePower();

        var autoStart = new MenuItem { Header = "Iniciar com o Windows", IsCheckable = true, IsChecked = AutoStart.IsEnabled };
        autoStart.Click += (_, _) => AutoStart.Set(autoStart.IsChecked);

        var settings = new MenuItem { Header = "Configurações" };
        settings.Click += (_, _) => openSettings();

        var quit = new MenuItem { Header = "Sair" };
        quit.Click += (_, _) => exit();

        _icon.ContextMenu = new ContextMenu { Items = { _powerItem, autoStart, settings, new Separator(), quit } };
        _icon.LeftClickCommand = new RelayCommand(toggleFlyout);
        _icon.NoLeftClickDelay = true;
        Update(null, hasError: false, "LG Controller");
        _icon.ForceCreate(false);
    }

    public void Update(AcState? state, bool hasError, string alias)
    {
        var isOn = state?.IsOn ?? false;
        var text = isOn && state!.TargetTemperature is { } target
            ? Math.Round(target).ToString("0", Culture)
            : null;
        var color = ModePalette.For(state?.Mode, isOn);
        var key = $"{text}|{color}|{hasError}";
        if (key != _renderKey)
        {
            _renderKey = key;
            var previous = _current;
            _current = TrayIconRenderer.Render(text, color, hasError);
            _icon.Icon = _current;
            previous?.Dispose();
        }

        _powerItem.Header = isOn ? "Desligar" : "Ligar";
        _powerItem.IsEnabled = state is not null;
        _icon.ToolTipText = Describe(state, hasError, alias);
    }

    public void Dispose()
    {
        _icon.Dispose();
        _current?.Dispose();
    }

    private static string Describe(AcState? state, bool hasError, string alias)
    {
        if (state is null) return hasError ? $"{alias}: sem conexão" : $"{alias}: conectando…";
        var status = state.IsOn ? "Ligado" : "Desligado";
        var target = state.IsOn && state.TargetTemperature is { } t ? $", {t.ToString("0.#", Culture)}°" : "";
        var room = state.CurrentTemperature is { } c ? $" (ambiente {c.ToString("0.#", Culture)}°)" : "";
        var warning = hasError ? " · sem conexão" : "";
        return $"{alias}: {status}{target}{room}{warning}";
    }
}
```

- [ ] **Step 5: Tela de configuração**

`src/LgController/UI/SetupViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LgController.Core;
using LgController.ThinQ;

namespace LgController.UI;

public sealed partial class SetupViewModel : ObservableObject
{
    private const string AirConditionerType = "DEVICE_AIR_CONDITIONER";

    private readonly Func<ThinQCredentials, IThinQClient> _clientFactory;
    private readonly SettingsStore _store;
    private readonly string _clientId;
    private string _connectedToken = "";
    private string _connectedCountry = "";

    [ObservableProperty] private string _token;
    [ObservableProperty] private string _countryCode;
    [ObservableProperty] private string? _errorText;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private DeviceInfo? _selectedDevice;

    public SetupViewModel(Func<ThinQCredentials, IThinQClient> clientFactory, SettingsStore store, AppSettings? existing)
    {
        _clientFactory = clientFactory;
        _store = store;
        _clientId = existing?.ClientId ?? SettingsStore.NewClientId();
        Token = existing?.Token ?? "";
        CountryCode = existing?.CountryCode ?? "BR";
    }

    public event Action? Saved;

    public ObservableCollection<DeviceInfo> Devices { get; } = [];

    public AppSettings? Result { get; private set; }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        ErrorText = null;
        Devices.Clear();
        SelectedDevice = null;

        var token = Token.Trim();
        var country = CountryCode.Trim().ToUpperInvariant();
        if (token.Length == 0)
        {
            ErrorText = "Informe o token.";
            return;
        }

        IThinQClient client;
        try
        {
            ThinQRegion.FromCountry(country);
            client = _clientFactory(new ThinQCredentials(token, country, _clientId));
        }
        catch (ArgumentException)
        {
            ErrorText = "País não suportado. Use o código de duas letras, como BR.";
            return;
        }

        IsBusy = true;
        try
        {
            var devices = await client.ListDevicesAsync();
            foreach (var device in devices.Where(d => d.Type == AirConditionerType)) Devices.Add(device);
        }
        catch (ThinQException ex)
        {
            ErrorText = ErrorMessages.For(ex);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        if (Devices.Count == 0)
        {
            ErrorText = "Nenhum ar-condicionado encontrado nesta conta.";
            return;
        }

        _connectedToken = token;
        _connectedCountry = country;
        if (Devices.Count == 1) SelectedDevice = Devices[0];
    }

    private bool CanSave() => SelectedDevice is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (SelectedDevice is not { } device) return;
        Result = new AppSettings(_connectedToken, _connectedCountry, _clientId, device.Id, device.Alias);
        _store.Save(Result);
        Saved?.Invoke();
    }
}
```

`src/LgController/UI/SetupWindow.xaml`:

```xml
<Window x:Class="LgController.UI.SetupWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="LG Controller: configuração" Width="440" SizeToContent="Height"
        ResizeMode="NoResize" WindowStartupLocation="CenterScreen"
        Background="{DynamicResource PanelSolidBrush}" Foreground="{DynamicResource TextBrush}"
        FontFamily="Segoe UI Variable Text, Segoe UI" FontSize="14">
    <StackPanel Margin="24">
        <TextBlock Text="Conectar ao ThinQ" FontSize="22" FontWeight="SemiBold" />
        <TextBlock Margin="0,8,0,0" TextWrapping="Wrap" Foreground="{DynamicResource SubtleTextBrush}">
            <Run Text="Gere um token pessoal com a sua conta LG em" />
            <Hyperlink NavigateUri="https://connect-pat.lgthinq.com" RequestNavigate="OnNavigate"
                       Foreground="{DynamicResource AccentBrush}">connect-pat.lgthinq.com</Hyperlink>
            <Run Text="e cole abaixo. Ele fica guardado criptografado neste computador." />
        </TextBlock>

        <TextBlock Text="Token" Margin="0,18,0,4" />
        <PasswordBox x:Name="TokenBox" Padding="8,6" PasswordChanged="OnTokenChanged" />

        <TextBlock Text="País (código de duas letras)" Margin="0,14,0,4" />
        <TextBox Text="{Binding CountryCode, UpdateSourceTrigger=PropertyChanged}" Padding="8,6" Width="80"
                 MaxLength="2" CharacterCasing="Upper" HorizontalAlignment="Left" />

        <Button Content="Conectar" Style="{StaticResource PrimaryButton}" Command="{Binding ConnectCommand}"
                HorizontalAlignment="Left" Margin="0,16,0,0" />

        <TextBlock Text="{Binding ErrorText}" Foreground="#FFE5484D" TextWrapping="Wrap" Margin="0,12,0,0" />

        <TextBlock Text="Aparelho" Margin="0,8,0,4" />
        <ComboBox ItemsSource="{Binding Devices}" SelectedItem="{Binding SelectedDevice}"
                  DisplayMemberPath="Alias" Padding="8,6" />

        <Button Content="Salvar" Style="{StaticResource PrimaryButton}" Command="{Binding SaveCommand}"
                HorizontalAlignment="Right" Margin="0,22,0,0" />
    </StackPanel>
</Window>
```

`src/LgController/UI/SetupWindow.xaml.cs`:

```csharp
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace LgController.UI;

public partial class SetupWindow : Window
{
    private readonly SetupViewModel _viewModel;

    public SetupWindow(SetupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        TokenBox.Password = viewModel.Token;
        viewModel.Saved += () => DialogResult = true;
    }

    private void OnTokenChanged(object sender, RoutedEventArgs e) => _viewModel.Token = TokenBox.Password;

    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
```

- [ ] **Step 6: Composição do aplicativo**

Substitua `src/LgController/App.xaml.cs` por:

```csharp
using System.Net.Http;
using System.Windows;
using LgController.Core;
using LgController.ThinQ;
using LgController.UI;

namespace LgController;

public partial class App : Application
{
    private readonly SettingsStore _store = new();
    private Mutex? _mutex;
    private AppSettings? _settings;
    private HttpClient? _http;
    private AcController? _controller;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(initiallyOwned: true, "LgController.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write(args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Write(args.ExceptionObject.ToString() ?? "erro");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Write(args.Exception);
            args.SetObserved();
        };

        ThemeManager.Initialize(this);

        var settings = _store.Load() ?? RunSetup(null);
        if (settings is null)
        {
            Shutdown();
            return;
        }

        StartSession(settings);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StopSession();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Abre a configuração; devolve nulo se a pessoa fechar sem salvar.</summary>
    private AppSettings? RunSetup(AppSettings? existing)
    {
        var viewModel = new SetupViewModel(
            credentials => new ThinQClient(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, credentials),
            _store,
            existing);
        new SetupWindow(viewModel).ShowDialog();
        return viewModel.Result;
    }

    private void StartSession(AppSettings settings)
    {
        _settings = settings;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var client = new ThinQClient(_http, new ThinQCredentials(settings.Token, settings.CountryCode, settings.ClientId));
        var controller = new AcController(client, settings.DeviceId, TimeProvider.System, action => Dispatcher.BeginInvoke(action));
        _controller = controller;

        _tray = new TrayIcon(
            toggleFlyout: () => { },
            togglePower: () => controller.SetPower(!(controller.State?.IsOn ?? false)),
            openSettings: OpenSettings,
            exit: Shutdown);

        controller.StateChanged += () =>
            _tray?.Update(controller.State, controller.ConnectionError is not null, settings.DeviceAlias);

        controller.Start();
        _ = controller.RefreshAsync();
    }

    private void StopSession()
    {
        _tray?.Dispose();
        _tray = null;
        _controller?.Dispose();
        _controller = null;
        _http?.Dispose();
        _http = null;
    }

    private void OpenSettings()
    {
        if (RunSetup(_settings) is not { } updated) return;
        StopSession();
        StartSession(updated);
    }
}
```

- [ ] **Step 7: Rodar os testes**

Run: `dotnet test`
Expected: todos os testes passam.

- [ ] **Step 8: Verificar no aplicativo**

Run: `dotnet run --project src/LgController`

Conferir, nesta ordem:
1. Sem `%APPDATA%\LgController\settings.json`, a tela de configuração abre. Colar o token, clicar em Conectar: o aparelho aparece no seletor e Salvar habilita.
2. Após Salvar, o ícone aparece na bandeja. Com o ar ligado, ele mostra a temperatura alvo na cor do modo; desligado, fica cinza com o símbolo de energia.
3. A dica ao passar o mouse mostra nome, estado e temperaturas.
4. Botão direito → Ligar/Desligar muda o aparelho e o ícone acompanha.
5. Botão direito → Iniciar com o Windows marca e desmarca; conferir o valor `LgController` em `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
6. Executar o aplicativo uma segunda vez não cria um segundo ícone.
7. Botão direito → Sair remove o ícone e encerra o processo.

Se o ícone não aparecer ou `ForceCreate` não compilar com a versão instalada de H.NotifyIcon, consultar a assinatura na versão instalada (`dotnet list src/LgController package`) e ajustar a chamada em `TrayIcon`.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: tray icon, themes and setup window"
```

---

### Task 7: Painel flutuante

**Files:**
- Create: `src/LgController/UI/OptionItem.cs`, `FlyoutPlacement.cs`, `FlyoutToggleGuard.cs`, `ArcGauge.cs`, `Backdrop.cs`, `FlyoutViewModel.cs`, `FlyoutWindow.xaml`, `FlyoutWindow.xaml.cs`
- Modify: `src/LgController/App.xaml.cs` (`StartSession`, `StopSession`)
- Test: `tests/LgController.Tests/FlyoutPlacementTests.cs`, `FlyoutToggleGuardTests.cs`, `FlyoutViewModelTests.cs`

**Interfaces:**
- Consumes: `AcController` e seus eventos (Tarefa 5); `ErrorMessages`, `ModePalette` (Tarefa 4); estilos e pincéis (Tarefa 6); `ThemeManager.Changed`, `ThemeManager.IsDark` (Tarefa 6); `FakeThinQClient` (Tarefa 5)
- Produces:
  - `OptionItem(string value, string label, string glyph, string glyphFont)` com `IsSelected`; `OptionCatalog.Mode(string)`, `.Wind(string)`, `.Timer(int minutes)`
  - `FlyoutPlacement.Compute(Rect workArea, Size window, double margin = 12) : Point`
  - `FlyoutToggleGuard(TimeProvider time)` com `MarkClosed()` e `CanOpen() : bool`
  - `ArcGauge` com `Minimum`, `Maximum`, `Value`, `Thickness`, `AccentBrush`, `TrackBrush`
  - `Backdrop.Apply(Window window, bool dark) : bool`, `Backdrop.HideFromAltTab(Window)`
  - `FlyoutViewModel(AcController controller, string alias, TimeProvider time, Action<Action>? dispatch = null)` com evento `SettingsRequested`
  - `FlyoutWindow(FlyoutViewModel viewModel)` com `Toggle()` e evento `OpenChanged : Action<bool>`

- [ ] **Step 1: Escrever os testes que falham**

`tests/LgController.Tests/FlyoutPlacementTests.cs`:

```csharp
using System.Windows;
using LgController.UI;

namespace LgController.Tests;

public class FlyoutPlacementTests
{
    private static readonly Size Window = new(360, 500);

    [Fact]
    public void TaskbarAtBottom_AnchorsBottomRight() =>
        Assert.Equal(new Point(1548, 528), FlyoutPlacement.Compute(new Rect(0, 0, 1920, 1040), Window));

    [Fact]
    public void TaskbarAtTop_AnchorsTopRight() =>
        Assert.Equal(new Point(1548, 52), FlyoutPlacement.Compute(new Rect(0, 40, 1920, 1040), Window));

    [Fact]
    public void TaskbarAtLeft_AnchorsBottomLeft() =>
        Assert.Equal(new Point(72, 568), FlyoutPlacement.Compute(new Rect(60, 0, 1860, 1080), Window));

    [Fact]
    public void WindowTallerThanWorkArea_StaysInsideTop() =>
        Assert.Equal(12, FlyoutPlacement.Compute(new Rect(0, 0, 800, 400), Window).Y);
}
```

`tests/LgController.Tests/FlyoutToggleGuardTests.cs`:

```csharp
using LgController.UI;
using Microsoft.Extensions.Time.Testing;

namespace LgController.Tests;

public class FlyoutToggleGuardTests
{
    [Fact]
    public void CanOpen_BeforeAnyClose() => Assert.True(new FlyoutToggleGuard(new FakeTimeProvider()).CanOpen());

    [Fact]
    public void ClickThatClosedThePanel_DoesNotReopenIt()
    {
        var time = new FakeTimeProvider();
        var guard = new FlyoutToggleGuard(time);

        guard.MarkClosed();
        time.Advance(TimeSpan.FromMilliseconds(50));

        Assert.False(guard.CanOpen());
    }

    [Fact]
    public void LaterClick_OpensAgain()
    {
        var time = new FakeTimeProvider();
        var guard = new FlyoutToggleGuard(time);

        guard.MarkClosed();
        time.Advance(TimeSpan.FromMilliseconds(400));

        Assert.True(guard.CanOpen());
    }
}
```

`tests/LgController.Tests/FlyoutViewModelTests.cs`:

```csharp
using LgController.Core;
using LgController.ThinQ;
using LgController.UI;
using Microsoft.Extensions.Time.Testing;

namespace LgController.Tests;

public class FlyoutViewModelTests
{
    private readonly FakeThinQClient _client = new();
    private readonly FakeTimeProvider _time = new();

    private async Task<(FlyoutViewModel Vm, AcController Controller)> CreateAsync()
    {
        var controller = new AcController(_client, "dev-1", _time);
        var vm = new FlyoutViewModel(controller, "Quarto", _time);
        await controller.RefreshAsync();
        return (vm, controller);
    }

    [Fact]
    public void BeforeFirstState_ControlsAreDisabled()
    {
        var vm = new FlyoutViewModel(new AcController(_client, "dev-1", _time), "Quarto", _time);

        Assert.Equal("Quarto", vm.DeviceName);
        Assert.False(vm.ControlsEnabled);
        Assert.False(vm.PowerEnabled);
        Assert.Equal("--", vm.TargetTemperatureText);
    }

    [Fact]
    public async Task State_IsReflected()
    {
        var (vm, _) = await CreateAsync();

        Assert.True(vm.IsOn);
        Assert.True(vm.ControlsEnabled);
        Assert.True(vm.PowerEnabled);
        Assert.Equal("23°", vm.TargetTemperatureText);
        Assert.Equal("Ambiente 26,5°", vm.RoomTemperatureText);
        Assert.Equal(23, vm.TargetTemperature);
        Assert.Equal(16, vm.MinTemperature);
        Assert.Equal(30, vm.MaxTemperature);
        Assert.Equal(["Frio", "Secar", "Ventilar", "Auto"], vm.Modes.Select(m => m.Label));
        Assert.Equal("COOL", vm.Modes.Single(m => m.IsSelected).Value);
        Assert.Equal(["Baixa", "Média", "Alta", "Auto"], vm.WindStrengths.Select(w => w.Label));
        Assert.Equal("MID", vm.WindStrengths.Single(w => w.IsSelected).Value);
        Assert.True(vm.SwingVertical);
        Assert.Equal("Desliga em 1 h 30 min", vm.TimerText);
    }

    [Fact]
    public async Task UnsupportedFeatures_AreHidden()
    {
        var (vm, _) = await CreateAsync();

        Assert.True(vm.ShowTemperature);
        Assert.True(vm.ShowSwingVertical);
        Assert.False(vm.ShowSwingHorizontal);
        Assert.True(vm.ShowPowerSave);
        Assert.True(vm.ShowTimer);
        Assert.Equal(["Desativado", "30 min", "1 h", "2 h", "4 h"], vm.TimerOptions.Select(t => t.Label));
    }

    [Fact]
    public async Task PowerOff_DisablesControlsButNotPowerButton()
    {
        _client.StateJson = Samples.State.Replace("POWER_ON", "POWER_OFF");

        var (vm, _) = await CreateAsync();

        Assert.False(vm.IsOn);
        Assert.False(vm.ControlsEnabled);
        Assert.False(vm.TemperatureEnabled);
        Assert.True(vm.PowerEnabled);
    }

    [Fact]
    public async Task FanMode_DisablesTemperature()
    {
        _client.StateJson = Samples.State.Replace("\"COOL\"", "\"FAN\"");

        var (vm, _) = await CreateAsync();

        Assert.True(vm.ControlsEnabled);
        Assert.False(vm.TemperatureEnabled);
    }

    [Fact]
    public async Task IncreaseAndDecrease_UseProfileStep()
    {
        var (vm, controller) = await CreateAsync();

        vm.IncreaseTemperatureCommand.Execute(null);

        Assert.Equal(23.5, controller.State!.TargetTemperature);
        Assert.Equal("23,5°", vm.TargetTemperatureText);

        vm.DecreaseTemperatureCommand.Execute(null);
        vm.DecreaseTemperatureCommand.Execute(null);

        Assert.Equal("22,5°", vm.TargetTemperatureText);
    }

    [Fact]
    public async Task SelectingOptions_CallsController()
    {
        var (vm, controller) = await CreateAsync();

        vm.SelectModeCommand.Execute(vm.Modes.Single(m => m.Value == "AIR_DRY"));
        vm.SelectWindCommand.Execute(vm.WindStrengths.Single(w => w.Value == "HIGH"));
        vm.SelectTimerCommand.Execute(vm.TimerOptions.Single(t => t.Value == "120"));

        Assert.Equal("AIR_DRY", controller.State!.Mode);
        Assert.Equal("HIGH", controller.State.WindStrength);
        Assert.Equal(120, controller.State.StopTimerMinutes);
        Assert.Equal("AIR_DRY", vm.Modes.Single(m => m.IsSelected).Value);
        Assert.Equal("120", vm.TimerOptions.Single(t => t.IsSelected).Value);
    }

    [Fact]
    public async Task TogglingBoundProperties_CallsController_ButSyncDoesNot()
    {
        var (vm, controller) = await CreateAsync();

        vm.PowerSave = true;
        vm.IsOn = false;
        _time.Advance(AcController.DebounceDelay);

        Assert.Equal(2, _client.Controls.Count);
        Assert.True(controller.State!.PowerSave);

        await controller.RefreshAsync();
        _time.Advance(AcController.DebounceDelay);

        Assert.Equal(2, _client.Controls.Count);
    }

    [Fact]
    public async Task AccentColor_FollowsMode()
    {
        var (vm, _) = await CreateAsync();
        var cool = vm.AccentColor;

        vm.SelectModeCommand.Execute(vm.Modes.Single(m => m.Value == "AIR_DRY"));

        Assert.NotEqual(cool, vm.AccentColor);
    }

    [Fact]
    public async Task UnauthorizedError_ShowsBannerWithSetupButton()
    {
        var (vm, controller) = await CreateAsync();
        _client.StateError = new ThinQException("1103", "x", ThinQErrorKind.Unauthorized);

        await controller.RefreshAsync();

        Assert.True(vm.HasBanner);
        Assert.Equal("Token inválido ou expirado.", vm.BannerText);
        Assert.True(vm.ShowSetupButton);
    }

    [Fact]
    public async Task DeviceOffline_DisablesEverything()
    {
        var (vm, controller) = await CreateAsync();
        _client.StateError = new ThinQException("1222", "x", ThinQErrorKind.DeviceOffline);

        await controller.RefreshAsync();

        Assert.Equal("O aparelho está offline.", vm.BannerText);
        Assert.False(vm.ShowSetupButton);
        Assert.False(vm.ControlsEnabled);
        Assert.False(vm.PowerEnabled);
    }

    [Fact]
    public async Task RejectedCommand_ShowsToastThatExpires()
    {
        var (vm, _) = await CreateAsync();
        _client.ControlError = new ThinQException("2208", "x", ThinQErrorKind.CommandRejected);

        vm.IncreaseTemperatureCommand.Execute(null);
        _time.Advance(AcController.DebounceDelay);

        Assert.True(vm.HasToast);
        Assert.Equal("O aparelho recusou o comando.", vm.ToastText);
        Assert.Equal("23°", vm.TargetTemperatureText);

        _time.Advance(FlyoutViewModel.ToastDuration);

        Assert.False(vm.HasToast);
    }

    [Fact]
    public async Task OpenSettingsCommand_RaisesEvent()
    {
        var (vm, _) = await CreateAsync();
        var raised = false;
        vm.SettingsRequested += () => raised = true;

        vm.OpenSettingsCommand.Execute(null);

        Assert.True(raised);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test`
Expected: FALHA de compilação, `'FlyoutPlacement' could not be found`.

- [ ] **Step 3: Peças sem janela**

`src/LgController/UI/OptionItem.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace LgController.UI;

/// <summary>Uma opção de um seletor do painel (modo, velocidade, timer).</summary>
public sealed partial class OptionItem(string value, string label, string glyph = "", string glyphFont = OptionCatalog.IconFont)
    : ObservableObject
{
    public string Value { get; } = value;
    public string Label { get; } = label;
    public string Glyph { get; } = glyph;
    public string GlyphFont { get; } = glyphFont;

    [ObservableProperty] private bool _isSelected;
}

public static class OptionCatalog
{
    public const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";
    private const string SymbolFont = "Segoe UI Symbol";

    public static OptionItem Mode(string value) => value switch
    {
        "COOL" => new(value, "Frio", ""),
        "HEAT" => new(value, "Aquecer", ""),
        "AIR_DRY" => new(value, "Secar", ""),
        "FAN" => new(value, "Ventilar", "≋", SymbolFont),
        "AUTO" => new(value, "Auto", ""),
        "AIR_CLEAN" => new(value, "Purificar", "✦", SymbolFont),
        "ENERGY_SAVING" => new(value, "Economia", "☘", SymbolFont),
        _ => new(value, Humanize(value), "●", SymbolFont),
    };

    public static OptionItem Wind(string value) => new(value, value switch
    {
        "SLOW" => "Mínima",
        "LOW" => "Baixa",
        "LOW_MID" => "Baixa+",
        "MID" => "Média",
        "MID_HIGH" => "Média+",
        "HIGH" => "Alta",
        "POWER" => "Turbo",
        "AUTO" => "Auto",
        "NATURE" => "Natural",
        _ => Humanize(value),
    });

    public static OptionItem Timer(int minutes) => new(minutes.ToString(), minutes switch
    {
        <= 0 => "Desativado",
        < 60 => $"{minutes} min",
        _ => $"{minutes / 60} h",
    });

    private static string Humanize(string value)
    {
        var text = value.Replace('_', ' ').ToLowerInvariant();
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
```

`src/LgController/UI/FlyoutPlacement.cs`:

```csharp
using System.Windows;

namespace LgController.UI;

public static class FlyoutPlacement
{
    /// <summary>
    /// Canto da área de trabalho mais próximo da bandeja. A área de trabalho começa depois da barra
    /// de tarefas quando ela está em cima ou à esquerda, e é por aí que se sabe onde ela está.
    /// </summary>
    public static Point Compute(Rect workArea, Size window, double margin = 12)
    {
        var x = workArea.Left > 0 ? workArea.Left + margin : workArea.Right - window.Width - margin;
        var y = workArea.Top > 0 ? workArea.Top + margin : workArea.Bottom - window.Height - margin;
        return new Point(Math.Max(x, workArea.Left + margin), Math.Max(y, workArea.Top + margin));
    }
}
```

`src/LgController/UI/FlyoutToggleGuard.cs`:

```csharp
namespace LgController.UI;

/// <summary>
/// Clicar no ícone com o painel aberto tira o foco do painel, que se fecha, e logo depois chega o clique.
/// Sem esta guarda o painel reabriria.
/// </summary>
public sealed class FlyoutToggleGuard(TimeProvider time)
{
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(300);
    private DateTimeOffset? _closedAt;

    public void MarkClosed() => _closedAt = time.GetUtcNow();

    public bool CanOpen() => _closedAt is not { } closedAt || time.GetUtcNow() - closedAt > Window;
}
```

`src/LgController/UI/ArcGauge.cs`:

```csharp
using System.Windows;
using System.Windows.Media;

namespace LgController.UI;

/// <summary>Arco de 270° com a parte preenchida proporcional a Value entre Minimum e Maximum.</summary>
public sealed class ArcGauge : FrameworkElement
{
    private const double StartAngle = 135;
    private const double SweepAngle = 270;

    public static readonly DependencyProperty MinimumProperty = Register(nameof(Minimum), 0d);
    public static readonly DependencyProperty MaximumProperty = Register(nameof(Maximum), 1d);
    public static readonly DependencyProperty ValueProperty = Register(nameof(Value), 0d);
    public static readonly DependencyProperty ThicknessProperty = Register(nameof(Thickness), 10d);
    public static readonly DependencyProperty AccentBrushProperty = Register<Brush?>(nameof(AccentBrush), null);
    public static readonly DependencyProperty TrackBrushProperty = Register<Brush?>(nameof(TrackBrush), null);

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public Brush? AccentBrush { get => (Brush?)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    private static DependencyProperty Register<T>(string name, T defaultValue) =>
        DependencyProperty.Register(
            name,
            typeof(T),
            typeof(ArcGauge),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        var radius = (size - Thickness) / 2;
        if (radius <= 0) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        if (TrackBrush is not null) DrawArc(drawingContext, TrackBrush, center, radius, 1);

        var fraction = Maximum > Minimum ? Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1) : 0;
        if (AccentBrush is not null && fraction > 0.001) DrawArc(drawingContext, AccentBrush, center, radius, fraction);
    }

    private void DrawArc(DrawingContext drawingContext, Brush brush, Point center, double radius, double fraction)
    {
        var sweep = SweepAngle * fraction;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(PointAt(center, radius, StartAngle), isFilled: false, isClosed: false);
            context.ArcTo(
                PointAt(center, radius, StartAngle + sweep),
                new Size(radius, radius),
                rotationAngle: 0,
                isLargeArc: sweep > 180,
                SweepDirection.Clockwise,
                isStroked: true,
                isSmoothJoin: false);
        }

        geometry.Freeze();
        var pen = new Pen(brush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private static Point PointAt(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }
}
```

`src/LgController/UI/Backdrop.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LgController.UI;

/// <summary>Efeitos de janela do Windows 11 via DWM.</summary>
public static class Backdrop
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int CornerRound = 2;
    private const int BackdropAcrylic = 3;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x80;

    /// <summary>Os fundos de sistema existem a partir do Windows 11 22H2.</summary>
    public static bool IsSupported => Environment.OSVersion.Version.Build >= 22621;

    /// <summary>Aplica acrílico, cantos arredondados e modo escuro. Falso quando o sistema não suporta.</summary>
    public static bool Apply(Window window, bool dark)
    {
        if (!IsSupported) return false;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return false;
        if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;

        Set(handle, DwmwaUseImmersiveDarkMode, dark ? 1 : 0);
        Set(handle, DwmwaWindowCornerPreference, CornerRound);
        return Set(handle, DwmwaSystemBackdropType, BackdropAcrylic);
    }

    public static void HideFromAltTab(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | WsExToolWindow);
    }

    private static bool Set(IntPtr handle, int attribute, int value) =>
        DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int)) == 0;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
```

- [ ] **Step 4: View model do painel**

`src/LgController/UI/FlyoutViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LgController.Core;
using LgController.ThinQ;

namespace LgController.UI;

public sealed partial class FlyoutViewModel : ObservableObject
{
    public static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(4);

    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly int[] TimerMinutes = [30, 60, 120, 240];

    private readonly AcController _controller;
    private readonly TimeProvider _time;
    private readonly Action<Action> _dispatch;
    private ITimer? _toastTimer;
    private bool _syncing;

    [ObservableProperty] private string _deviceName;
    [ObservableProperty] private bool _isOn;
    [ObservableProperty] private bool _powerEnabled;
    [ObservableProperty] private bool _controlsEnabled;
    [ObservableProperty] private string _roomTemperatureText = "";
    [ObservableProperty] private string _targetTemperatureText = "--";
    [ObservableProperty] private string _modeText = "";
    [ObservableProperty] private double _targetTemperature;
    [ObservableProperty] private double _minTemperature = 18;
    [ObservableProperty] private double _maxTemperature = 30;
    [ObservableProperty] private bool _showTemperature;
    [ObservableProperty] private bool _temperatureEnabled;
    [ObservableProperty] private bool _showModes;
    [ObservableProperty] private bool _showWind;
    [ObservableProperty] private bool _showSwingVertical;
    [ObservableProperty] private bool _showSwingHorizontal;
    [ObservableProperty] private bool _showPowerSave;
    [ObservableProperty] private bool _showTimer;
    [ObservableProperty] private bool _swingVertical;
    [ObservableProperty] private bool _swingHorizontal;
    [ObservableProperty] private bool _powerSave;
    [ObservableProperty] private string _timerText = "";
    [ObservableProperty] private Color _accentColor = Color.FromRgb(0x6B, 0x72, 0x80);
    [ObservableProperty] private bool _hasBanner;
    [ObservableProperty] private string _bannerText = "";
    [ObservableProperty] private bool _showSetupButton;
    [ObservableProperty] private bool _hasToast;
    [ObservableProperty] private string _toastText = "";

    public FlyoutViewModel(AcController controller, string alias, TimeProvider time, Action<Action>? dispatch = null)
    {
        _controller = controller;
        _time = time;
        _dispatch = dispatch ?? (action => action());
        DeviceName = alias;
        controller.StateChanged += Sync;
        controller.ErrorOccurred += ShowToast;
        Sync();
    }

    public event Action? SettingsRequested;

    public ObservableCollection<OptionItem> Modes { get; } = [];
    public ObservableCollection<OptionItem> WindStrengths { get; } = [];
    public ObservableCollection<OptionItem> TimerOptions { get; } = [];

    partial void OnIsOnChanged(bool value)
    {
        if (!_syncing) _controller.SetPower(value);
    }

    partial void OnSwingVerticalChanged(bool value)
    {
        if (!_syncing) _controller.SetSwingVertical(value);
    }

    partial void OnSwingHorizontalChanged(bool value)
    {
        if (!_syncing) _controller.SetSwingHorizontal(value);
    }

    partial void OnPowerSaveChanged(bool value)
    {
        if (!_syncing) _controller.SetPowerSave(value);
    }

    [RelayCommand]
    private void IncreaseTemperature() => StepTemperature(+1);

    [RelayCommand]
    private void DecreaseTemperature() => StepTemperature(-1);

    [RelayCommand]
    private void SelectMode(OptionItem option) => _controller.SetMode(option.Value);

    [RelayCommand]
    private void SelectWind(OptionItem option) => _controller.SetWindStrength(option.Value);

    [RelayCommand]
    private void SelectTimer(OptionItem option) => _controller.SetStopTimer(int.Parse(option.Value));

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke();

    private void StepTemperature(int direction)
    {
        if (_controller.Capabilities is not { } caps || _controller.State?.TargetTemperature is not { } current) return;
        _controller.SetTargetTemperature(current + direction * caps.TemperatureStep);
    }

    private void Sync()
    {
        _syncing = true;
        try
        {
            var error = _controller.ConnectionError;
            HasBanner = error is not null;
            BannerText = error is null ? "" : ErrorMessages.For(error);
            ShowSetupButton = error?.Kind == ThinQErrorKind.Unauthorized;

            if (_controller.Capabilities is not { } caps || _controller.State is not { } state)
            {
                PowerEnabled = false;
                ControlsEnabled = false;
                TemperatureEnabled = false;
                return;
            }

            var reachable = error?.Kind != ThinQErrorKind.DeviceOffline;
            IsOn = state.IsOn;
            PowerEnabled = caps.CanPower && reachable;
            ControlsEnabled = state.IsOn && reachable;

            ShowTemperature = caps.TemperatureResource != TemperatureResource.None;
            TemperatureEnabled = ControlsEnabled && state.Mode != "FAN";
            MinTemperature = caps.MinTemperature;
            MaxTemperature = caps.MaxTemperature;
            TargetTemperature = state.TargetTemperature ?? caps.MinTemperature;
            TargetTemperatureText = state.TargetTemperature is { } target ? $"{target.ToString("0.#", Culture)}°" : "--";
            RoomTemperatureText = state.CurrentTemperature is { } room ? $"Ambiente {room.ToString("0.#", Culture)}°" : "";
            ModeText = state.Mode is { } mode ? OptionCatalog.Mode(mode).Label : "";

            SyncOptions(Modes, caps.Modes.Select(OptionCatalog.Mode), state.Mode);
            SyncOptions(WindStrengths, caps.WindStrengths.Select(OptionCatalog.Wind), state.WindStrength);
            ShowModes = Modes.Count > 0;
            ShowWind = WindStrengths.Count > 0;

            ShowSwingVertical = caps.CanSwingVertical;
            ShowSwingHorizontal = caps.CanSwingHorizontal;
            ShowPowerSave = caps.CanPowerSave;
            SwingVertical = state.SwingVertical;
            SwingHorizontal = state.SwingHorizontal;
            PowerSave = state.PowerSave;

            ShowTimer = caps.CanStopTimer;
            var minutes = caps.CanCancelStopTimer ? TimerMinutes.Prepend(0) : TimerMinutes;
            SyncOptions(TimerOptions, minutes.Select(OptionCatalog.Timer), (state.StopTimerMinutes ?? 0).ToString());
            TimerText = DescribeTimer(state.StopTimerMinutes);

            var (r, g, b) = ModePalette.For(state.Mode, state.IsOn);
            AccentColor = Color.FromRgb(r, g, b);
        }
        finally
        {
            _syncing = false;
        }
    }

    private static void SyncOptions(ObservableCollection<OptionItem> target, IEnumerable<OptionItem> source, string? selected)
    {
        var items = source.ToList();
        if (!target.Select(o => o.Value).SequenceEqual(items.Select(o => o.Value)))
        {
            target.Clear();
            foreach (var item in items) target.Add(item);
        }

        foreach (var item in target) item.IsSelected = item.Value == selected;
    }

    private static string DescribeTimer(int? minutes)
    {
        if (minutes is not { } total || total <= 0) return "";
        var hours = total / 60;
        var rest = total % 60;
        var text = hours > 0 && rest > 0 ? $"{hours} h {rest} min" : hours > 0 ? $"{hours} h" : $"{rest} min";
        return $"Desliga em {text}";
    }

    private void ShowToast(ThinQException error)
    {
        ToastText = ErrorMessages.For(error);
        HasToast = true;
        _toastTimer?.Dispose();
        _toastTimer = _time.CreateTimer(
            _ => _dispatch(() => HasToast = false), null, ToastDuration, Timeout.InfiniteTimeSpan);
    }
}
```

- [ ] **Step 5: Janela do painel**

`src/LgController/UI/FlyoutWindow.xaml`:

```xml
<Window x:Class="LgController.UI.FlyoutWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="clr-namespace:LgController.UI"
        Title="LG Controller" Width="360" SizeToContent="Height"
        WindowStyle="None" ResizeMode="NoResize" ShowInTaskbar="False" Topmost="True"
        Background="Transparent" Foreground="{DynamicResource TextBrush}"
        FontFamily="Segoe UI Variable Text, Segoe UI" FontSize="14"
        TextOptions.TextFormattingMode="Ideal" UseLayoutRounding="True">
    <WindowChrome.WindowChrome>
        <WindowChrome GlassFrameThickness="-1" CaptionHeight="0" ResizeBorderThickness="0" CornerRadius="0"
                      UseAeroCaptionButtons="False" />
    </WindowChrome.WindowChrome>

    <Window.Resources>
        <DataTemplate x:Key="IconOption">
            <Button Style="{StaticResource ChipButton}" Height="62"
                    Command="{Binding DataContext.SelectModeCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                    CommandParameter="{Binding}">
                <StackPanel>
                    <TextBlock Text="{Binding Glyph}" FontFamily="{Binding GlyphFont}" FontSize="18"
                               HorizontalAlignment="Center" />
                    <TextBlock Text="{Binding Label}" FontSize="11" Margin="0,5,0,0" HorizontalAlignment="Center" />
                </StackPanel>
            </Button>
        </DataTemplate>
        <DataTemplate x:Key="WindOption">
            <Button Style="{StaticResource ChipButton}" Height="36" Content="{Binding Label}" FontSize="12"
                    Command="{Binding DataContext.SelectWindCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                    CommandParameter="{Binding}" />
        </DataTemplate>
        <DataTemplate x:Key="TimerOption">
            <Button Style="{StaticResource ChipButton}" Height="36" Content="{Binding Label}" FontSize="12"
                    Command="{Binding DataContext.SelectTimerCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                    CommandParameter="{Binding}" />
        </DataTemplate>
        <ItemsPanelTemplate x:Key="OneRow">
            <UniformGrid Rows="1" />
        </ItemsPanelTemplate>
        <Style x:Key="Caption" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{DynamicResource SubtleTextBrush}" />
            <Setter Property="FontSize" Value="12" />
            <Setter Property="Margin" Value="3,16,0,5" />
        </Style>
    </Window.Resources>

    <Border x:Name="Root" Padding="20,18,20,20" Background="{DynamicResource PanelSolidBrush}">
        <Border.RenderTransform>
            <TranslateTransform x:Name="RootShift" />
        </Border.RenderTransform>
        <StackPanel>
            <!-- Cabeçalho -->
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <StackPanel VerticalAlignment="Center">
                    <TextBlock Text="{Binding DeviceName}" FontSize="18" FontWeight="SemiBold"
                               TextTrimming="CharacterEllipsis" />
                    <TextBlock Text="{Binding RoomTemperatureText}" Foreground="{DynamicResource SubtleTextBrush}"
                               FontSize="12" Margin="0,2,0,0" />
                </StackPanel>
                <ToggleButton Grid.Column="1" Style="{StaticResource PowerButton}" IsChecked="{Binding IsOn}"
                              IsEnabled="{Binding PowerEnabled}" ToolTip="Ligar ou desligar" />
            </Grid>

            <!-- Erro de conexão -->
            <Border Background="{DynamicResource BannerBrush}" CornerRadius="10" Padding="12,9" Margin="0,14,0,0"
                    Visibility="{Binding HasBanner, Converter={StaticResource BoolVis}}">
                <DockPanel>
                    <Button DockPanel.Dock="Right" Content="Configurar" Style="{StaticResource PrimaryButton}"
                            Padding="10,4" FontSize="12" Command="{Binding OpenSettingsCommand}"
                            Visibility="{Binding ShowSetupButton, Converter={StaticResource BoolVis}}" />
                    <TextBlock Text="{Binding BannerText}" TextWrapping="Wrap" VerticalAlignment="Center" FontSize="13" />
                </DockPanel>
            </Border>

            <!-- Temperatura -->
            <Grid Height="214" Margin="0,14,0,0" Style="{StaticResource Section}"
                  IsEnabled="{Binding TemperatureEnabled}"
                  Visibility="{Binding ShowTemperature, Converter={StaticResource BoolVis}}">
                <ui:ArcGauge Width="204" Height="204" Thickness="11" VerticalAlignment="Top"
                             Minimum="{Binding MinTemperature}" Maximum="{Binding MaxTemperature}"
                             Value="{Binding TargetTemperature}"
                             AccentBrush="{DynamicResource AccentBrush}" TrackBrush="{DynamicResource TrackBrush}" />
                <StackPanel VerticalAlignment="Top" HorizontalAlignment="Center" Margin="0,54,0,0">
                    <TextBlock Text="{Binding TargetTemperatureText}" FontSize="58" FontWeight="Light"
                               FontFamily="Segoe UI Variable Display, Segoe UI" HorizontalAlignment="Center" />
                    <TextBlock Text="{Binding ModeText}" Foreground="{DynamicResource SubtleTextBrush}" FontSize="13"
                               HorizontalAlignment="Center" Margin="0,-4,0,0" />
                </StackPanel>
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" VerticalAlignment="Bottom">
                    <Button Style="{StaticResource RoundButton}" Content="−" ToolTip="Diminuir"
                            Command="{Binding DecreaseTemperatureCommand}" />
                    <Button Style="{StaticResource RoundButton}" Content="+" ToolTip="Aumentar" Margin="44,0,0,0"
                            Command="{Binding IncreaseTemperatureCommand}" />
                </StackPanel>
            </Grid>

            <!-- Modo, ventilação e extras -->
            <StackPanel Style="{StaticResource Section}" IsEnabled="{Binding ControlsEnabled}">
                <StackPanel Visibility="{Binding ShowModes, Converter={StaticResource BoolVis}}">
                    <TextBlock Text="Modo" Style="{StaticResource Caption}" />
                    <ItemsControl ItemsSource="{Binding Modes}" ItemTemplate="{StaticResource IconOption}"
                                  ItemsPanel="{StaticResource OneRow}" Margin="-3,0" />
                </StackPanel>

                <StackPanel Visibility="{Binding ShowWind, Converter={StaticResource BoolVis}}">
                    <TextBlock Text="Ventilação" Style="{StaticResource Caption}" />
                    <ItemsControl ItemsSource="{Binding WindStrengths}" ItemTemplate="{StaticResource WindOption}"
                                  ItemsPanel="{StaticResource OneRow}" Margin="-3,0" />
                </StackPanel>

                <StackPanel Margin="3,12,3,0">
                    <CheckBox Style="{StaticResource Switch}" Content="Oscilar para cima e para baixo"
                              IsChecked="{Binding SwingVertical}"
                              Visibility="{Binding ShowSwingVertical, Converter={StaticResource BoolVis}}" />
                    <CheckBox Style="{StaticResource Switch}" Content="Oscilar para os lados"
                              IsChecked="{Binding SwingHorizontal}"
                              Visibility="{Binding ShowSwingHorizontal, Converter={StaticResource BoolVis}}" />
                    <CheckBox Style="{StaticResource Switch}" Content="Economia de energia"
                              IsChecked="{Binding PowerSave}"
                              Visibility="{Binding ShowPowerSave, Converter={StaticResource BoolVis}}" />
                </StackPanel>

                <StackPanel Visibility="{Binding ShowTimer, Converter={StaticResource BoolVis}}">
                    <DockPanel>
                        <TextBlock DockPanel.Dock="Right" Text="{Binding TimerText}" Style="{StaticResource Caption}"
                                   Foreground="{DynamicResource AccentBrush}" Margin="0,16,3,5" />
                        <TextBlock Text="Desligar em" Style="{StaticResource Caption}" />
                    </DockPanel>
                    <ItemsControl ItemsSource="{Binding TimerOptions}" ItemTemplate="{StaticResource TimerOption}"
                                  ItemsPanel="{StaticResource OneRow}" Margin="-3,0" />
                </StackPanel>
            </StackPanel>

            <!-- Aviso temporário de comando recusado -->
            <Border Background="{DynamicResource BannerBrush}" CornerRadius="10" Padding="12,8" Margin="0,14,0,0"
                    Visibility="{Binding HasToast, Converter={StaticResource BoolVis}}">
                <TextBlock Text="{Binding ToastText}" TextWrapping="Wrap" FontSize="13" />
            </Border>
        </StackPanel>
    </Border>
</Window>
```

`src/LgController/UI/FlyoutWindow.xaml.cs`:

```csharp
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LgController.UI;

public partial class FlyoutWindow : Window
{
    private readonly FlyoutViewModel _viewModel;
    private readonly FlyoutToggleGuard _guard = new(TimeProvider.System);
    private readonly SolidColorBrush _accent;

    public FlyoutWindow(FlyoutViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // Um único pincel compartilhado: animar a cor dele muda todos os controles de destaque.
        _accent = new SolidColorBrush(viewModel.AccentColor);
        Resources["AccentBrush"] = _accent;
        viewModel.PropertyChanged += OnViewModelChanged;

        Deactivated += (_, _) => HideFlyout();
        SizeChanged += (_, _) => Reposition();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) HideFlyout();
        };
        ThemeManager.Changed += ApplyBackdrop;
    }

    public event Action<bool>? OpenChanged;

    public void Toggle()
    {
        if (IsVisible) HideFlyout();
        else if (_guard.CanOpen()) ShowFlyout();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Backdrop.HideFromAltTab(this);
        ApplyBackdrop();
    }

    protected override void OnClosed(EventArgs e)
    {
        ThemeManager.Changed -= ApplyBackdrop;
        _viewModel.PropertyChanged -= OnViewModelChanged;
        base.OnClosed(e);
    }

    private void ApplyBackdrop()
    {
        var hasBackdrop = Backdrop.Apply(this, ThemeManager.IsDark);
        Root.SetResourceReference(
            System.Windows.Controls.Border.BackgroundProperty, hasBackdrop ? "PanelTintBrush" : "PanelSolidBrush");
    }

    private void ShowFlyout()
    {
        Root.Opacity = 0;
        Show();
        UpdateLayout();
        Reposition();
        Activate();

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        RootShift.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        OpenChanged?.Invoke(true);
    }

    private void HideFlyout()
    {
        if (!IsVisible) return;
        _guard.MarkClosed();
        Hide();
        OpenChanged?.Invoke(false);
    }

    private void Reposition()
    {
        if (!IsVisible) return;
        var position = FlyoutPlacement.Compute(SystemParameters.WorkArea, new Size(ActualWidth, ActualHeight));
        Left = position.X;
        Top = position.Y;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FlyoutViewModel.AccentColor)) return;
        _accent.BeginAnimation(
            SolidColorBrush.ColorProperty,
            new ColorAnimation(_viewModel.AccentColor, TimeSpan.FromMilliseconds(250)));
    }
}
```

- [ ] **Step 6: Ligar o painel no aplicativo**

Em `src/LgController/App.xaml.cs`, acrescente o campo junto dos outros:

```csharp
    private FlyoutWindow? _flyout;
```

Substitua os métodos `StartSession` e `StopSession` por:

```csharp
    private void StartSession(AppSettings settings)
    {
        _settings = settings;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var client = new ThinQClient(_http, new ThinQCredentials(settings.Token, settings.CountryCode, settings.ClientId));
        void Dispatch(Action action) => Dispatcher.BeginInvoke(action);
        var controller = new AcController(client, settings.DeviceId, TimeProvider.System, Dispatch);
        _controller = controller;

        var viewModel = new FlyoutViewModel(controller, settings.DeviceAlias, TimeProvider.System, Dispatch);
        viewModel.SettingsRequested += OpenSettings;
        var flyout = new FlyoutWindow(viewModel);
        flyout.OpenChanged += controller.SetPanelOpen;
        _flyout = flyout;

        _tray = new TrayIcon(
            toggleFlyout: flyout.Toggle,
            togglePower: () => controller.SetPower(!(controller.State?.IsOn ?? false)),
            openSettings: OpenSettings,
            exit: Shutdown);

        controller.StateChanged += () =>
            _tray?.Update(controller.State, controller.ConnectionError is not null, settings.DeviceAlias);

        controller.Start();
        _ = controller.RefreshAsync();
    }

    private void StopSession()
    {
        _tray?.Dispose();
        _tray = null;
        _flyout?.Close();
        _flyout = null;
        _controller?.Dispose();
        _controller = null;
        _http?.Dispose();
        _http = null;
    }
```

- [ ] **Step 7: Rodar os testes**

Run: `dotnet test`
Expected: todos os testes passam.

- [ ] **Step 8: Verificar no aplicativo**

Run: `dotnet run --project src/LgController`

Conferir, com o aparelho real:
1. Clique esquerdo no ícone abre o painel acima da bandeja, com animação, dentro da tela e sem faixa preta sobrando nas bordas.
2. O fundo é translúcido e os cantos são arredondados. Se o fundo sair preto ou opaco, registrar o que aparece e trocar `BackdropAcrylic` por `2` (Mica) em `Backdrop.cs`; se ainda falhar, fazer `IsSupported` devolver `false`, o que usa o fundo sólido.
3. Clicar fora fecha o painel. Esc fecha o painel. Clicar no ícone com o painel aberto fecha e não reabre.
4. O painel não aparece no Alt+Tab nem na barra de tarefas.
5. O botão de energia liga e desliga; desligado, tudo abaixo do cabeçalho fica esmaecido.
6. − e + mudam o número na hora; vários cliques rápidos resultam em um único bipe do aparelho, na temperatura final.
7. Trocar de modo muda a cor de destaque com transição; no modo Ventilar o mostrador fica esmaecido.
8. Cada velocidade, cada interruptor e cada opção de timer muda o aparelho. Só aparecem os controles que o aparelho suporta.
9. Todos os ícones dos modos aparecem como desenho e não como um quadrado vazio. Um quadrado vazio indica código de glifo errado: trocar em `OptionCatalog.Mode`.
10. Com o Wi-Fi do PC desligado, abrir o painel mostra "Sem conexão com a internet." e o ícone ganha o ponto de alerta; religando, some na próxima leitura.
11. Mudar o Windows entre tema claro e escuro (Configurações → Personalização → Cores) troca as cores do painel sem reiniciar o aplicativo, e o texto continua legível nos dois.
12. Com a barra de tarefas alinhada em outra posição, se o Windows permitir, o painel continua ancorado ao lado dela.

Se sobrar uma faixa vazia abaixo do conteúdo (problema conhecido de `SizeToContent` com `WindowChrome`), acrescentar ao fim de `OnSourceInitialized`:

```csharp
        SizeToContent = SizeToContent.Manual;
        SizeToContent = SizeToContent.Height;
```

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: flyout panel with temperature dial, modes, fan and extras"
```

---

### Task 8: Publicação e documentação

**Files:**
- Create: `README.md`
- Modify: `src/LgController/LgController.csproj`

**Interfaces:**
- Consumes: o aplicativo completo das tarefas anteriores
- Produces: `publish/LgController.exe`

- [ ] **Step 1: Configurar a publicação em arquivo único**

Em `src/LgController/LgController.csproj`, dentro do primeiro `<PropertyGroup>`, acrescente:

```xml
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>false</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <DebugType>embedded</DebugType>
    <SatelliteResourceLanguages>pt-BR</SatelliteResourceLanguages>
```

Acrescente `publish/` ao `.gitignore`.

- [ ] **Step 2: Publicar**

Run: `dotnet publish src/LgController -c Release -o publish`
Expected: `publish/LgController.exe` existe.

Run: `dotnet test`
Expected: todos os testes passam.

- [ ] **Step 3: Verificar o executável publicado**

Encerrar qualquer instância em execução, abrir `publish\LgController.exe` com duplo clique e conferir:
1. O ícone aparece na bandeja sem abrir janela de console.
2. O painel abre e controla o aparelho.
3. Botão direito → Iniciar com o Windows grava no registro o caminho deste `.exe`.
4. `%APPDATA%\LgController\settings.json` não contém o token em texto puro.

- [ ] **Step 4: Escrever o README**

`README.md`:

````markdown
# LG Controller

Controle um ar-condicionado LG ThinQ pela bandeja do Windows.

## Requisitos

- Windows 10 ou 11 (o fundo translúcido precisa do Windows 11 22H2 ou mais novo)
- .NET 10 Desktop Runtime
- Um ar-condicionado LG já cadastrado no aplicativo ThinQ

## Primeiro uso

1. Gere um token pessoal em https://connect-pat.lgthinq.com com a sua conta LG.
2. Abra `LgController.exe`, cole o token, informe o país e clique em Conectar.
3. Escolha o aparelho e salve.

O token fica em `%APPDATA%\LgController\settings.json`, criptografado para o seu usuário do Windows.

## Uso

- Clique no ícone da bandeja para abrir o painel.
- Clique com o botão direito para ligar ou desligar, iniciar com o Windows, reconfigurar ou sair.

## Desenvolvimento

```powershell
dotnet test
dotnet run --project src/LgController
dotnet publish src/LgController -c Release -o publish
```

`tools/Probe` é um console para testar a API direto no aparelho. Defina `LG_THINQ_PAT` e rode
`dotnet run --project tools/Probe -- list`.

Erros não tratados vão para `%APPDATA%\LgController\log.txt`.
````

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore: single-file publish and README"
```
