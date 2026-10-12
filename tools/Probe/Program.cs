using System.Text.Json;
using LgController.Core;
using LgController.ThinQ;

const string Usage = """
    Set the LG_THINQ_PAT environment variable (and LG_THINQ_COUNTRY if not BR).
    Commands:
      list
      dump  <deviceId> <folder>     writes real-profile.json and real-state.json
      show  <deviceId>              shows the parsed capabilities and state
      power <deviceId> on|off
      temp  <deviceId> <degrees>
      mode  <deviceId> <MODE>
      wind  <deviceId> <SPEED>
      swing <deviceId> on|off
      save  <deviceId> on|off
      timer <deviceId> <minutes>    turn-off timer; 0 cancels
      start <deviceId> <minutes>    turn-on timer; 0 cancels
      sleep <deviceId> <minutes>    sleep timer; 0 cancels
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
            Console.WriteLine($"Written to {Path.GetFullPath(arg)}");
            break;
        case "show":
            Console.WriteLine(caps);
            Console.WriteLine($"Modes: {string.Join(", ", caps.Modes)}");
            Console.WriteLine($"Wind: {string.Join(", ", caps.AllWindStrengths)}");
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
        case "start":
            await client.ControlAsync(id, AcCommands.StartTimer(int.Parse(arg)));
            break;
        case "sleep":
            await client.ControlAsync(id, AcCommands.SleepTimer(int.Parse(arg)));
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
    Console.Error.WriteLine($"Error {ex.Code} ({ex.Kind}): {ex.Message}");
    return 2;
}
