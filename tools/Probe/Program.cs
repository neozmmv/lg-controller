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
