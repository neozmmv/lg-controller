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

    private static readonly DeviceInfo Ac1 = new("ac-1", "DEVICE_AIR_CONDITIONER", "Bedroom", "RAC");
    private static readonly DeviceInfo Ac2 = new("ac-2", "DEVICE_AIR_CONDITIONER", "Living room", "RAC");
    private static readonly DeviceInfo Washer = new("w-1", "DEVICE_WASHER", "Washer", "W");

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

        Assert.Equal("Enter the token.", vm.ErrorText);
        Assert.Null(_used);
    }

    [Fact]
    public async Task Connect_UnsupportedCountry_ShowsError()
    {
        var vm = Create();
        vm.Token = "pat";
        vm.CountryCode = "ZZ";

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("Unsupported country. Use the two-letter code, such as BR.", vm.ErrorText);
    }

    [Fact]
    public async Task Connect_NoAirConditioner_ShowsError()
    {
        _client.Devices.Add(Washer);
        var vm = Create();
        vm.Token = "pat";

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("No air conditioner found on this account.", vm.ErrorText);
        Assert.Empty(vm.Devices);
    }

    [Fact]
    public async Task Connect_ApiError_ShowsTranslatedMessage()
    {
        _client.ListError = new ThinQException("1103", "invalid", ThinQErrorKind.Unauthorized);
        var vm = Create();
        vm.Token = "pat";

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("Invalid or expired token.", vm.ErrorText);
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
        Assert.Equal("Bedroom", stored.DeviceAlias);
    }

    [Fact]
    public void ExistingSettings_PrefillFieldsAndKeepClientId()
    {
        var vm = Create(new AppSettings("old-pat", "PT", "thinq-open-keep", "ac-9", "Old"));

        Assert.Equal("old-pat", vm.Token);
        Assert.Equal("PT", vm.CountryCode);
    }

    [Fact]
    public async Task ExistingSettings_ReuseClientId()
    {
        _client.Devices.Add(Ac1);
        var vm = Create(new AppSettings("old-pat", "BR", "thinq-open-keep", "ac-9", "Old"));

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("thinq-open-keep", _used!.ClientId);
    }
}
