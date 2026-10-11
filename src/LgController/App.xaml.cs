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
