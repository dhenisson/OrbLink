using System.Windows;
using OrbLink.Core;
using OrbLink.Network;
using OrbLink.Storage;
using Forms = System.Windows.Forms;
using OrbLink.App.Services;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using System.Diagnostics;

namespace OrbLink.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private Forms.NotifyIcon? _notifyIcon;
    private BubbleWindow? _bubble;
    private SqliteStore? _store;
    private NetworkCoordinator? _network;
    private LocalLog? _log;
    private AppearanceService? _appearance;
    private ClipboardImageSender? _imageSender;
    private bool _ownsMutex;
    private bool _diagnostic;
    private volatile bool _shuttingDown;
    private int _recoveringNetwork;
    private readonly CancellationTokenSource _recoveryStop = new();
    public SqliteStore Store => _store ?? throw new InvalidOperationException("Aplicativo não iniciado.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var diagnostic = e.Args.Length > 0 && e.Args[0] == "--smoke-test";
        _diagnostic = diagnostic;
        _singleInstance = new Mutex(false, diagnostic ? "Local\\OrbLink.Smoke." + Guid.NewGuid() : "Local\\OrbLink.Application");
        try { _ownsMutex = _singleInstance.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsMutex = true; }
        if (!_ownsMutex)
        {
            System.Windows.MessageBox.Show("O OrbLink já está em execução. Feche a versão aberta pela bandeja antes de iniciar outra.\n\n" + FindBlockingProcess(), "OrbLink");
            Shutdown();
            return;
        }

        try
        {
            var diagnosticRoot = e.Args.Length == 2 ? Path.GetFullPath(e.Args[1]) :
                Path.Combine(Path.GetTempPath(), "orblink-smoke-" + Guid.NewGuid().ToString("N"));
            if (diagnostic && Directory.Exists(diagnosticRoot) && Directory.EnumerateFileSystemEntries(diagnosticRoot).Any())
                throw new InvalidOperationException("O diagnóstico exige uma pasta vazia, separada dos dados pessoais.");
            var paths = diagnostic ? new AppPaths(diagnosticRoot) : new AppPaths();
            _log = new LocalLog(paths);
            _store = new SqliteStore(paths, new SecretProtector());
            await _store.InitializeAsync();
            _appearance = new AppearanceService(paths, _store);
            await _appearance.InitializeAsync();
            _network = new NetworkCoordinator(_store, paths, _log, diagnostic ?
                new NetworkOptions { TransferPort = 0, EnableDiscovery = false } : null);
            _network.PairingRequested += HandlePairingRequestAsync;
            await _network.StartAsync();
            _imageSender = new ClipboardImageSender(paths, _network);
            _bubble = new BubbleWindow(_store, _network, _log, _appearance, _imageSender);
            _bubble.Closed += async (_, _) =>
            {
                if (_shuttingDown) return;
                _bubble = null;
                await QuitAsync(); // Closing the only orb must never leave an invisible background process.
            };
            _bubble.Show();
            try { CreateTrayIcon(); }
            catch (Exception ex)
            {
                _notifyIcon?.Dispose(); _notifyIcon = null;
                var fallbackMenu = new System.Windows.Controls.ContextMenu();
                var openItem = new System.Windows.Controls.MenuItem { Header = "Abrir" };
                openItem.Click += (_, _) => _bubble?.ToggleTray();
                var quitItem = new System.Windows.Controls.MenuItem { Header = "Sair" };
                quitItem.Click += async (_, _) => await QuitAsync();
                fallbackMenu.Items.Add(openItem); fallbackMenu.Items.Add(quitItem);
                _bubble.ContextMenu = fallbackMenu;
                await _log.WriteAsync("WARN", "Bandeja indisponível; a orb continua visível.", ex);
                if (!diagnostic) MessageBox.Show("A bandeja do Windows está indisponível. Use a orb para abrir o OrbLink.", "OrbLink");
            }
            if (!diagnostic)
            {
                try { StartupManager.RefreshIfEnabled(); }
                catch (Exception ex) { await _log.WriteAsync("WARN", "Não foi possível atualizar a inicialização com Windows.", ex); }
                NetworkChange.NetworkAddressChanged += OnNetworkChanged;
                SystemEvents.PowerModeChanged += OnPowerChanged;
                SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            }
            await _log.WriteAsync("INFO", "OrbLink iniciado.");
            if (diagnostic)
            {
                _bubble.ToggleTray();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                timer.Tick += async (_, _) =>
                {
                    timer.Stop();
                    try
                    {
                        await _log.WriteAsync("INFO", "Diagnóstico: orb e painel abertos; iniciando encerramento.");
                        await QuitAsync();
                    }
                    catch (Exception ex)
                    {
                        await _log.WriteAsync("ERROR", "Falha ao encerrar diagnóstico.", ex);
                        Shutdown(1);
                    }
                };
                timer.Start();
            }
        }
        catch (Exception ex)
        {
            if (_log is not null) await _log.WriteAsync("FATAL", "Falha ao iniciar o aplicativo.", ex);
            if (diagnostic) { Shutdown(1); return; }
            System.Windows.MessageBox.Show($"Não foi possível iniciar o OrbLink.\n\n{ex.Message}",
                "OrbLink", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private static string FindBlockingProcess()
    {
        var descriptions = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            try
            {
                if (process.Id == Environment.ProcessId || process.ProcessName is not ("OrbLink" or "OrbLink.App")) continue;
                var module = process.MainModule;
                descriptions.Add($"{process.ProcessName} · PID {process.Id} · versão {module?.FileVersionInfo.ProductVersion ?? "desconhecida"}\n{module?.FileName}");
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
        }
        return descriptions.Count == 0 ? "Outra instância detém o bloqueio deste usuário Windows." : string.Join("\n\n", descriptions);
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => RecoverNetworkAsync();
    private void OnPowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) RecoverNetworkAsync(); }
    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(async () =>
    { if (!_shuttingDown && _bubble is not null) await _bubble.RecoverPositionAsync(); }));
    private async void RecoverNetworkAsync()
    {
        if (_shuttingDown || Interlocked.Exchange(ref _recoveringNetwork, 1) != 0) return;
        try
        {
            if (_network is not null) await _network.RefreshPeersAsync(_recoveryStop.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (_log is not null) await _log.WriteAsync("WARN", "Recuperação de rede: o heartbeat continuará tentando.", ex); }
        finally { Interlocked.Exchange(ref _recoveringNetwork, 0); }
    }

    private async Task HandlePairingRequestAsync(IncomingPairRequest request)
    {
        if (_network is null) return;
        var accepted = false;
        await Dispatcher.InvokeAsync(() =>
        {
            accepted = System.Windows.MessageBox.Show(
                $"{request.Message.RequesterName} deseja conectar ao OrbLink.\n\nEndereço: {request.RemoteAddress}\n\nAceitar este computador?",
                "Solicitação de pareamento", MessageBoxButton.YesNo, MessageBoxImage.Question,
                MessageBoxResult.No) == MessageBoxResult.Yes;
        });
        if (accepted) await _network.AcceptPairAsync(request.RequestId);
        else _network.RejectPair(request.RequestId);
    }

    private void CreateTrayIcon()
    {
        _notifyIcon = new Forms.NotifyIcon { Text = "OrbLink", Icon = TrayIconFactory.Create(), Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => Dispatcher.Invoke(() => _bubble?.ToggleTray()));
        menu.Items.Add("Computadores", null, (_, _) => Dispatcher.Invoke(OpenDevices));
        menu.Items.Add("Histórico", null, (_, _) => Dispatcher.Invoke(OpenHistory));
        menu.Items.Add("Configurações", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add(new Forms.ToolStripSeparator());
        var startup = new Forms.ToolStripMenuItem("Iniciar com o Windows")
        {
            Checked = StartupManager.IsEnabled(), CheckOnClick = true
        };
        startup.CheckedChanged += (_, _) =>
        {
            try { StartupManager.SetEnabled(startup.Checked); }
            catch (Exception ex) { MessageBox.Show($"Não foi possível configurar a inicialização.\n{ex.Message}", "OrbLink"); }
        };
        menu.Items.Add(startup);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => Dispatcher.InvokeAsync(QuitAsync));
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(() => _bubble?.ToggleTray());
    }

    private void OpenDevices()
    {
        if (_network is not null && _store is not null) new DevicesWindow(_network, _store) { Topmost = true }.Show();
    }

    private void OpenHistory()
    {
        if (_store is not null) new HistoryWindow(_store) { Topmost = true }.Show();
    }

    private void OpenSettings()
    {
        if (_appearance is not null && _network is not null)
            new SettingsWindow(_appearance, _network) { Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen }.Show();
    }

    private async Task QuitAsync()
    {
        _shuttingDown = true;
        _recoveryStop.Cancel();
        if (_diagnostic) await _log!.WriteAsync("DEBUG", "Diagnóstico: fechando orb e painel.");
        _bubble?.Close();
        if (_diagnostic) await _log!.WriteAsync("DEBUG", "Diagnóstico: encerrando rede.");
        if (_network is not null)
        {
            await _network.DisposeAsync();
            _network = null;
        }
        if (_diagnostic) await _log!.WriteAsync("INFO", "Diagnóstico: rede encerrada; encerramento normal.");
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shuttingDown = true;
        _recoveryStop.Cancel();
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        SystemEvents.PowerModeChanged -= OnPowerChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _notifyIcon?.Dispose();
        if (_network is not null) Task.Run(async () => await _network.DisposeAsync()).Wait(TimeSpan.FromSeconds(4));
        if (_ownsMutex) _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
