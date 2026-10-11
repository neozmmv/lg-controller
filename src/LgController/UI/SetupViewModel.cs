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
            ErrorText = "Enter the token.";
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
            ErrorText = "Unsupported country. Use the two-letter code, such as BR.";
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
            ErrorText = "No air conditioner found on this account.";
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
