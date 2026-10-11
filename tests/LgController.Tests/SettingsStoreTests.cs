using LgController.Core;
using LgController.ThinQ;

namespace LgController.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lgc-tests-" + Guid.NewGuid());
    private static readonly AppSettings Settings = new("secret-pat", "BR", "thinq-open-1", "dev-1", "Bedroom");

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

        Assert.DoesNotContain("secret-pat", File.ReadAllText(Path.Combine(_dir, "settings.json")));
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("{}")]
    [InlineData("""{"token":"AAAA","countryCode":"BR","clientId":"c","deviceId":"d","deviceAlias":"a"}""")]
    [InlineData("""{"token":"not base64!","countryCode":"BR","clientId":"c","deviceId":"d","deviceAlias":"a"}""")]
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
    [InlineData(ThinQErrorKind.Network, "No internet connection.")]
    [InlineData(ThinQErrorKind.Unauthorized, "Invalid or expired token.")]
    [InlineData(ThinQErrorKind.DeviceOffline, "The device is offline.")]
    [InlineData(ThinQErrorKind.RateLimited, "Too many requests. Please wait a moment.")]
    [InlineData(ThinQErrorKind.CommandRejected, "The device rejected the command.")]
    [InlineData(ThinQErrorKind.Unknown, "LG error: failed (2000)")]
    public void ErrorMessages_AreInEnglish(ThinQErrorKind kind, string expected) =>
        Assert.Equal(expected, ErrorMessages.For(new ThinQException("2000", "failed", kind)));

    [Fact]
    public void ModePalette_OffIsGrey_AndModesDiffer()
    {
        Assert.Equal(((byte)0x6B, (byte)0x72, (byte)0x80), ModePalette.For("COOL", isOn: false));
        Assert.NotEqual(ModePalette.For("COOL", true), ModePalette.For("HEAT", true));
        Assert.Equal(ModePalette.For("COOL", true), ModePalette.For("UNKNOWN", true));
    }
}
