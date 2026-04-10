using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Services;
using PocketMC.Desktop.Views;

namespace PocketMC.Desktop.ViewModels
{
    // The existing InstanceCardViewModel is in Views, but let's assume it's in the namespace.
    // I'll keep the `ObservableCollection<InstanceCardViewModel> Instances` property.
    public class DashboardViewModel : ViewModelBase
    {
        private readonly ApplicationState _applicationState;
        private readonly InstanceManager _instanceManager;
        private readonly ServerProcessManager _serverProcessManager;
        private readonly ResourceMonitorService _resourceMonitorService;
        private readonly PlayitAgentService _playitAgentService;
        private readonly PlayitApiClient _playitApiClient;
        private readonly IDialogService _dialogService;
        private readonly IAppNavigationService _navigationService;
        private readonly IAppDispatcher _dispatcher;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DashboardViewModel> _logger;

        public ObservableCollection<InstanceCardViewModel> Instances { get; } = new();

        public ICommand NewInstanceCommand { get; }
        public ICommand RefreshInstancesCommand { get; }
        public ICommand StartServerCommand { get; }
        public ICommand StopServerCommand { get; }
        public ICommand DeleteInstanceCommand { get; }
        public ICommand RenameInstanceCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand CopyCrashReportCommand { get; }
        public ICommand ServerSettingsCommand { get; }
        public ICommand OpenConsoleCommand { get; }

        public DashboardViewModel(
            ApplicationState applicationState,
            InstanceManager instanceManager,
            ServerProcessManager serverProcessManager,
            ResourceMonitorService resourceMonitorService,
            PlayitAgentService playitAgentService,
            PlayitApiClient playitApiClient,
            IDialogService dialogService,
            IAppNavigationService navigationService,
            IAppDispatcher dispatcher,
            IServiceProvider serviceProvider,
            ILogger<DashboardViewModel> logger)
        {
            _applicationState = applicationState;
            _instanceManager = instanceManager;
            _serverProcessManager = serverProcessManager;
            _resourceMonitorService = resourceMonitorService;
            _playitAgentService = playitAgentService;
            _playitApiClient = playitApiClient;
            _dialogService = dialogService;
            _navigationService = navigationService;
            _dispatcher = dispatcher;
            _serviceProvider = serviceProvider;
            _logger = logger;

            NewInstanceCommand = new RelayCommand(_ => NavigateToNewInstance());
            RefreshInstancesCommand = new RelayCommand(_ => LoadInstances());
            StartServerCommand = new RelayCommand(StartServer);
            StopServerCommand = new RelayCommand(StopServer);
            DeleteInstanceCommand = new RelayCommand(DeleteInstance);
            RenameInstanceCommand = new RelayCommand(RenameInstance);
            OpenFolderCommand = new RelayCommand(OpenFolder);
            CopyCrashReportCommand = new RelayCommand(CopyCrashReport);
            ServerSettingsCommand = new RelayCommand(OpenSettings);
            OpenConsoleCommand = new RelayCommand(OpenConsole);
        }

                private void NavigateToNewInstance()
        {
            var newInstancePage = ActivatorUtilities.CreateInstance<NewInstancePage>(_serviceProvider);
            _navigationService.NavigateToDetailPage(newInstancePage, "New Instance");
        }

        public void LoadInstances()
        {
            if (!_applicationState.IsConfigured) return;

            var existingVms = Instances.ToList();
            Instances.Clear();
            var metas = _instanceManager.GetAllInstances();
            foreach (var meta in metas)
            {
                var existing = existingVms.FirstOrDefault(v => v.Id == meta.Id);
                if (existing != null)
                {
                    existing.UpdateFromMetadata(meta);
                    Instances.Add(existing);
                }
                else
                {
                    var newVm = new InstanceCardViewModel(meta, _serverProcessManager);
                    Instances.Add(newVm);
                }
            }

            foreach (var vm in Instances)
            {
                var process = _serverProcessManager.GetProcess(vm.Id);
                if (process != null)
                {
                    vm.State = process.State;
                }

                _ = RefreshTunnelAddressAsync(vm);
            }
        }

        private async Task RefreshTunnelAddressAsync(InstanceCardViewModel vm)
        {
            var propsFile = Path.Combine(_instanceManager.GetInstancePath(vm.Id)!, "server.properties");
            var props = ServerPropertiesParser.Read(propsFile);
            if (props.TryGetValue("server-port", out string? portString) && int.TryParse(portString, out int port))
            {
                try
                {
                    var res = await _playitApiClient.GetTunnelsAsync();
                    if (res.Success && res.Tunnels != null)
                    {
                        var match = PlayitApiClient.FindTunnelForPort(res.Tunnels, port);
                        if (match != null)
                        {
                            _dispatcher.Invoke(() => vm.TunnelAddress = match.PublicAddress);
                        }
                    }
                }
                catch (Exception) { }
            }
        }

        private async void StartServer(object? parameter)
        {
            if (parameter is InstanceCardViewModel vm)
            {
                string? instancePath = _instanceManager.GetInstancePath(vm.Id);
                if (instancePath == null) return;

                var process = await _serverProcessManager.StartProcessAsync(vm.Metadata, _applicationState.GetRequiredAppRootPath());
                if (process == null) return;

                vm.State = process.State;

                // Automatically start playit
                if (_applicationState.IsConfigured && File.Exists(_applicationState.GetPlayitExecutablePath()))
                {
                    if (_playitAgentService.State == PlayitAgentState.Stopped || _playitAgentService.State == PlayitAgentState.Starting)
                    {
                        _playitAgentService.Start();
                    }
                }


            }
        }



        private async void StopServer(object? parameter)
        {
            if (parameter is InstanceCardViewModel vm)
            {
                var process = _serverProcessManager.GetProcess(vm.Id);
                if (process != null) await process.WriteInputAsync("stop");
            }
        }

        private async void DeleteInstance(object? parameter)
        {
            if (parameter is InstanceCardViewModel vm)
            {
                if (_serverProcessManager.IsRunning(vm.Id))
                {
                    _dialogService.ShowMessage("Server Running", "Cannot delete a running server. Stop it first.", DialogType.Warning);
                    return;
                }

                var prompt = await _dialogService.ShowDialogAsync("Delete Server", $"Are you sure you want to completely erase the {vm.Name} server? All worlds and files will be permanently deleted.", DialogType.Warning, false);
                if (prompt == DialogResult.Yes)
                {
                    string? path = _instanceManager.GetInstancePath(vm.Id);
                    if (path != null && Directory.Exists(path))
                    {
                        await PocketMC.Desktop.Utils.FileUtils.CleanDirectoryAsync(path);
                        Directory.Delete(path, true);
                    }
                    LoadInstances();
                }
            }
        }

        private async void RenameInstance(object? parameter)
        {
            // Placeholder: Implementing a full rename dialog might require a custom popup window,
            // but we can ask the UI layer to handle it or use a simple input dialog if available.
        }

        private void OpenFolder(object? parameter)
        {
            if (parameter is InstanceCardViewModel vm)
            {
                string? path = _instanceManager.GetInstancePath(vm.Id);
                if (path != null && Directory.Exists(path))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
            }
        }

        private void CopyCrashReport(object? parameter)
        {
            if (parameter is InstanceCardViewModel vm)
            {
                string? path = _instanceManager.GetInstancePath(vm.Id);
                if (path == null) return;

                var crashReportsDir = Path.Combine(path, "crash-reports");
                if (Directory.Exists(crashReportsDir))
                {
                    var latestReport = new DirectoryInfo(crashReportsDir)
                        .GetFiles("*.txt")
                        .OrderByDescending(f => f.LastWriteTime)
                        .FirstOrDefault();

                    if (latestReport != null)
                    {
                        string content = File.ReadAllText(latestReport.FullName);
                        System.Windows.Clipboard.SetText(content);
                        _dialogService.ShowMessage("Copied", "The latest crash report has been copied to your clipboard.", DialogType.Information);
                        return;
                    }
                }

                _dialogService.ShowMessage("No Crash Reports", "No crash reports found for this server.", DialogType.Information);
            }
        }

        private void OpenSettings(object? parameter)
        {
            if (parameter is InstanceCardViewModel vm)
            {
                var settingsViewModel = ActivatorUtilities.CreateInstance<ServerSettingsViewModel>(_serviceProvider, vm.Metadata);
                var settingsPage = ActivatorUtilities.CreateInstance<ServerSettingsPage>(_serviceProvider, settingsViewModel);
                _navigationService.NavigateToDetailPage(settingsPage, $"Settings: {vm.Name}");
            }
        }

        private void OpenConsole(object? parameter)
        {
            if (parameter is InstanceCardViewModel vm)
            {
                var process = _serverProcessManager.GetProcess(vm.Id);
                if (process == null)
                {
                    _dialogService.ShowMessage("Unavailable", "Start the server at least once before opening the console.", DialogType.Information);
                    return;
                }

                var consolePage = ActivatorUtilities.CreateInstance<ServerConsolePage>(_serviceProvider, vm.Metadata, process);
                _navigationService.NavigateToDetailPage(consolePage, $"Console: {vm.Name}");
            }
        }
    }
}
