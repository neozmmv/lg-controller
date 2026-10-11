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
