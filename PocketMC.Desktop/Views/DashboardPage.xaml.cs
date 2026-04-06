using System;
using System.IO;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Controls;
using PocketMC.Desktop.Utils;
using PocketMC.Desktop.Services;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.ViewModels;
using System.Linq;
using System.Threading.Tasks;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace PocketMC.Desktop.Views
{
    public partial class DashboardPage : Page
    {
        private readonly ApplicationState _applicationState;
        private readonly InstanceManager _instanceManager;
        private readonly ServerProcessManager _serverProcessManager;
        private readonly ResourceMonitorService _resourceMonitorService;
        private readonly PlayitAgentService _playitAgentService;
        private readonly PlayitApiClient _playitApiClient;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DashboardPage> _logger;
        private ObservableCollection<InstanceCardViewModel> _viewModels = new();
        private bool _isSubscribedToServices;

        public DashboardPage(
            ApplicationState applicationState,
            InstanceManager instanceManager,
            ServerProcessManager serverProcessManager,
            ResourceMonitorService resourceMonitorService,
            PlayitAgentService playitAgentService,
            PlayitApiClient playitApiClient,
            IServiceProvider serviceProvider,
            ILogger<DashboardPage> logger)
        {
            InitializeComponent();
            _applicationState = applicationState;
            _instanceManager = instanceManager;
            _serverProcessManager = serverProcessManager;
            _resourceMonitorService = resourceMonitorService;
            _playitAgentService = playitAgentService;
            _playitApiClient = playitApiClient;
            _serviceProvider = serviceProvider;
            _logger = logger;
            LoadInstances();

            Loaded += DashboardPage_Loaded;
            Unloaded += DashboardPage_Unloaded;
        }

        private void DashboardPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isSubscribedToServices)
            {
                _serverProcessManager.OnInstanceStateChanged += OnServerStateChanged;
                _serverProcessManager.OnRestartCountdownTick += OnRestartCountdownTick;
                _resourceMonitorService.OnGlobalMetricsUpdated += UpdateMetrics;
                _isSubscribedToServices = true;
            }

            LoadInstances();
            SyncViewModelStates();
        }

        private void DashboardPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_isSubscribedToServices)
            {
                return;
            }

            _serverProcessManager.OnInstanceStateChanged -= OnServerStateChanged;
            _serverProcessManager.OnRestartCountdownTick -= OnRestartCountdownTick;
            _resourceMonitorService.OnGlobalMetricsUpdated -= UpdateMetrics;
            _isSubscribedToServices = false;
        }

        private int _metricTicks = 0;

        private void UpdateMetrics()
        {
            _metricTicks++;
            if (_metricTicks % 5 == 0) // Roughly every 10 seconds based on global monitor ticks
            {
                RunBackgroundTunnelPoll();
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                var dictionary = _resourceMonitorService.Metrics;
                foreach (var vm in _viewModels)
                {
                    if (dictionary.TryGetValue(vm.Id, out var metric))
                    {
                        vm.EnsureChartsReady();
                        
                        vm.CpuHistory.Add(metric.CpuUsage);
                        if (vm.CpuHistory.Count > 30) vm.CpuHistory.RemoveAt(0);
                        vm.CpuText = $"CPU {Math.Round(metric.CpuUsage)}%";

                        vm.RamHistory.Add(metric.RamUsageMb);
                        if (vm.RamHistory.Count > 30) vm.RamHistory.RemoveAt(0);
                        vm.RamText = $"RAM {Math.Round(metric.RamUsageMb)} MB";

                        vm.PlayerStatus = $"{metric.PlayerCount} Players Online";
                        
                        // High RAM warning badge logic
                        if (vm.Metadata.MaxRamMb > 0 && metric.RamUsageMb > vm.Metadata.MaxRamMb * 1.1)
                        {
                            vm.RamText += " ⚠ (High)";
                        }
                    }
                    else
                    {
                        // Default out
                        vm.PlayerStatus = "0 Players Online";
                        vm.CpuText = "CPU 0%";
                        vm.RamText = "RAM 0 MB";
                    }
                }
            });
        }

        private void RunBackgroundTunnelPoll()
        {
            // Only search for running instances missing a tunnel IP
            var missingTunnels = _viewModels.Where(v => v.IsRunning && string.IsNullOrEmpty(v.TunnelAddress)).ToList();
            if (!missingTunnels.Any()) return;

            Task.Run(async () =>
            {
                try
                {
                    var result = await _playitApiClient.GetTunnelsAsync();
                    if (!result.Success) return;

                    foreach (var vm in missingTunnels)
                    {
                        var serverDir = _instanceManager.GetInstancePath(vm.Id);
                        if (serverDir == null) continue;
                        string propsPath = System.IO.Path.Combine(serverDir, "server.properties");
                        var props = ServerPropertiesParser.Read(propsPath);
                        int serverPort = 25565;
                        if (props.TryGetValue("server-port", out var portStr) && int.TryParse(portStr, out var parsed))
                            serverPort = parsed;

                        var match = PlayitApiClient.FindTunnelForPort(result.Tunnels, serverPort);
                        if (match != null)
                        {
                            Dispatcher.Invoke(() => vm.TunnelAddress = match.PublicAddress);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Background Playit tunnel polling failed.");
                }
            });
        }

        private void LoadInstances()
        {
            var instances = _instanceManager.GetAllInstances();
            _viewModels = new ObservableCollection<InstanceCardViewModel>(
                instances.Select(m => new InstanceCardViewModel(m, _serverProcessManager)));
            InstanceGrid.ItemsSource = _viewModels;
            TxtEmpty.Visibility = _viewModels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SyncViewModelStates()
        {
            foreach (var vm in _viewModels)
            {
                var currentState = _serverProcessManager.GetProcess(vm.Id)?.State ?? ServerState.Stopped;
                vm.UpdateState(currentState);
            }
        }

        private void OnServerStateChanged(Guid instanceId, ServerState newState)
        {
            Dispatcher.Invoke(() =>
            {
                var vm = _viewModels.FirstOrDefault(v => v.Id == instanceId);
                vm?.UpdateState(newState);
            });
        }

        private void OnRestartCountdownTick(Guid instanceId, int seconds)
        {
            Dispatcher.Invoke(() =>
            {
                var vm = _viewModels.FirstOrDefault(v => v.Id == instanceId);
                if (vm != null)
                {
                    vm.UpdateCountdown(seconds);
                }
            });
        }

        // --- Helper to get ViewModel from sender ---
        private InstanceCardViewModel? GetViewModel(object sender)
        {
            if (sender is FrameworkElement element && element.DataContext is InstanceCardViewModel vm)
                return vm;
            return null;
        }

        private void BtnMoreOptions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.DataContext = btn.DataContext;
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void BtnManage_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetViewModel(sender);
            if (vm != null)
            {
                var page = ActivatorUtilities.CreateInstance<ServerSettingsPage>(_serviceProvider, vm.Metadata);
                var mainWindow = Window.GetWindow(this) as MainWindow;

                if (mainWindow?.NavigateToDetailPage(page, "Server Settings") != true)
                {
                    NavigationService?.Navigate(page);
                }
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetViewModel(sender);
            if (vm != null)
            {
                string? folderName = FindFolderById(vm.Id);
                if (folderName != null)
                    _instanceManager.OpenInExplorer(folderName);
            }
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetViewModel(sender);
            if (vm == null) return;

            try
            {
                // Resolve tunnel address before starting (NET-06, NET-09)
                await ResolveTunnelForInstance(vm);

                await _serverProcessManager.StartProcessAsync(vm.Metadata, _applicationState.GetRequiredAppRootPath());
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    ex.Message,
                    "Cannot Start Server",
                    MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Resolves the Playit tunnel address for a server instance (NET-06).
        /// Reads server-port from server.properties to match against API tunnels.
        /// </summary>
        private async Task ResolveTunnelForInstance(InstanceCardViewModel vm)
        {
            if (_playitAgentService.State != PlayitAgentState.Connected &&
                _playitAgentService.State != PlayitAgentState.Starting)
            {
                vm.TunnelAddress = null;
                return;
            }

            // Read server port from server.properties
            string? serverDir = _instanceManager.GetInstancePath(vm.Id);
            if (serverDir == null) return;

            string propsPath = System.IO.Path.Combine(serverDir, "server.properties");
            var props = ServerPropertiesParser.Read(propsPath);
            int serverPort = 25565; // Default Minecraft port
            if (props.TryGetValue("server-port", out var portStr) && int.TryParse(portStr, out var parsed))
                serverPort = parsed;

            var tunnelService = ActivatorUtilities.CreateInstance<TunnelService>(_serviceProvider);
            var result = await tunnelService.ResolveTunnelAsync(serverPort);

            switch (result.Status)
            {
                case TunnelResolutionResult.TunnelStatus.Found:
                    vm.TunnelAddress = result.PublicAddress;
                    break;

                case TunnelResolutionResult.TunnelStatus.LimitReached:
                    var tunnelResult = await _playitApiClient.GetTunnelsAsync();
                    var dialog = new TunnelLimitDialog(tunnelResult.Tunnels);
                    dialog.Owner = Window.GetWindow(this);
                    dialog.ShowDialog();
                    vm.TunnelAddress = null;
                    break;

                case TunnelResolutionResult.TunnelStatus.CreationStarted:
                    var guidePage = ActivatorUtilities.CreateInstance<TunnelCreationGuidePage>(_serviceProvider, serverPort);
                    guidePage.OnTunnelResolved += (address) =>
                    {
                        Dispatcher.Invoke(() => vm.TunnelAddress = address);
                    };
                    NavigationService.Navigate(guidePage);
                    break;

                case TunnelResolutionResult.TunnelStatus.Error:
                    vm.TunnelAddress = null;
                    break;

                default:
                    vm.TunnelAddress = null;
                    break;
            }
        }

        /// <summary>
        /// Click-to-copy handler for tunnel address pill (D-03).
        /// </summary>
        private void TunnelPill_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is InstanceCardViewModel vm
                && !string.IsNullOrEmpty(vm.TunnelAddress))
            {
                System.Windows.Clipboard.SetText(vm.TunnelAddress);
                // Brief visual feedback — swap text momentarily
                var originalAddress = vm.TunnelAddress;
                vm.TunnelAddress = "\u2713 Copied";
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1500);
                    Dispatcher.Invoke(() =>
                    {
                        if (vm.TunnelAddress == "\u2713 Copied")
                        {
                            vm.TunnelAddress = originalAddress;
                        }
                    });
                });
            }
        }

        private async void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetViewModel(sender);
            if (vm == null) return;

            try
            {
                if (vm.IsWaitingToRestart)
                {
                    _serverProcessManager.AbortRestartDelay(vm.Id);
                }
                else
                {
                    await _serverProcessManager.StopProcessAsync(vm.Id);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    ex.Message,
                    "Stop Error",
                    MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void BtnConsole_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetViewModel(sender);
            if (vm == null) return;

            var process = _serverProcessManager.GetProcess(vm.Id);
            if (process != null)
            {
                var page = ActivatorUtilities.CreateInstance<ServerConsolePage>(_serviceProvider, vm.Metadata, process);
                var mainWindow = Window.GetWindow(this) as MainWindow;

                if (mainWindow?.NavigateToDetailPage(page, "Console") != true)
                {
                    NavigationService?.Navigate(page);
                }
            }
        }

        private string? FindFolderById(Guid id)
        {
            var instancePath = _instanceManager.GetInstancePath(id);
            return instancePath == null ? null : System.IO.Path.GetFileName(instancePath);
        }

        private void BtnNewInstance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var page = _serviceProvider.GetRequiredService<NewInstancePage>();
                var mainWindow = Window.GetWindow(this) as MainWindow;

                if (mainWindow?.NavigateToDetailPage(page, "New Instance") != true)
                {
                    NavigationService?.Navigate(page);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open the New Instance page.");
                System.Windows.MessageBox.Show(
                    "PocketMC could not open the New Instance page. The error was logged, and the app stayed open so you can keep working.\n\nPlease restart the app and try again.",
                    "Could Not Open New Instance Page",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void BtnBrowseModpacks_Click(object sender, RoutedEventArgs e)
        {
            var browserPage = new PluginBrowserPage(null, "*", "project_type:modpack");
            browserPage.OnModpackDownloaded += async (tempZip) =>
            {
                await ImportModpackAsync(tempZip);
                try { File.Delete(tempZip); } catch { }
            };
            NavigationService.Navigate(browserPage);
        }

        private async void BtnImportModpack_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Modpack ZIP (*.zip)|*.zip|All Files (*.*)|*.*",
                Title = "Select Modpack ZIP"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                await ImportModpackAsync(openFileDialog.FileName);
            }
        }

        private async void DeleteInstance_Click(object sender, RoutedEventArgs e)
        {
            InstanceCardViewModel? vm = null;
            if (sender is MenuItem menuItem && menuItem.DataContext is InstanceCardViewModel mvm)
                vm = mvm;
            
            if (vm == null) return;

            if (_serverProcessManager.IsRunning(vm.Id))
            {
                System.Windows.MessageBox.Show(
                    "Cannot delete a running server. Stop it first.",
                    "Server Running",
                    MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            var prompt = System.Windows.MessageBox.Show(
                "Are you sure you want to completely erase the " + vm.Name + " server? All worlds and files will be permanently deleted.",
                "Delete Server",
                MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (prompt == MessageBoxResult.Yes)
            {
                var folder = FindFolderById(vm.Id);
                if (folder != null)
                {
                    await Task.Run(() => _instanceManager.DeleteInstance(folder));
                    LoadInstances();
                }
            }
        }

        private async void RenameInstance_Click(object sender, RoutedEventArgs e)
        {
            InstanceCardViewModel? vm = null;
            if (sender is MenuItem menuItem && menuItem.DataContext is InstanceCardViewModel mvm)
                vm = mvm;
            
            if (vm == null) return;

            var dialog = new Wpf.Ui.Controls.MessageBox
            {
                Title = "Rename Server",
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel"
            };

            var stackPanel = new StackPanel();
            var txtName = new Wpf.Ui.Controls.TextBox { Text = vm.Name };
            var txtDesc = new Wpf.Ui.Controls.TextBox { Text = vm.Description, Margin = new Thickness(0, 10, 0, 0) };
            stackPanel.Children.Add(txtName);
            stackPanel.Children.Add(txtDesc);
            dialog.Content = stackPanel;

            var result = await dialog.ShowDialogAsync();
            if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
            {
                if (!string.IsNullOrWhiteSpace(txtName.Text))
                {
                    var folder = FindFolderById(vm.Id);
                    if (folder != null)
                    {
                        _instanceManager.UpdateMetadata(folder, txtName.Text, txtDesc.Text);
                        LoadInstances();
                    }
                }
            }
        }

        private void Page_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private async void Page_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    string zipPath = files[0];
                    if (Path.GetExtension(zipPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        await ImportModpackAsync(zipPath);
                    }
                }
            }
        }

        private async Task ImportModpackAsync(string zipPath)
        {
            var modpackService = _serviceProvider.GetRequiredService<ModpackService>();
            
            try
            {
                var result = await modpackService.ParseModpackZipAsync(zipPath);
                
                var confirm = System.Windows.MessageBox.Show(
                    $"Import modpack '{result.Name}' for Minecraft {result.MinecraftVersion} ({result.Loader})?",
                    "Import Modpack",
                    MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (confirm == MessageBoxResult.Yes)
                {
                    // Show a progress indicator or dialog here if possible
                    // For now, let's just do it in the background
                    await modpackService.ImportAsync(result, result.Name, _applicationState.GetRequiredAppRootPath(), _instanceManager, zipPath);
                    LoadInstances();
                    System.Windows.MessageBox.Show("Modpack imported successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import modpack from {ZipPath}.", zipPath);
                System.Windows.MessageBox.Show($"Failed to import modpack: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CopyCrashReport_Click(object sender, RoutedEventArgs e)
        {
            var m = sender as MenuItem;
            var vm = m?.DataContext as InstanceCardViewModel;
            if (vm == null) return;

            var process = _serverProcessManager.GetProcess(vm.Id);
            if (process != null && !string.IsNullOrEmpty(process.CrashContext))
            {
                System.Windows.Clipboard.SetText(process.CrashContext);
                System.Windows.MessageBox.Show("Crash report copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show("No crash report available for this instance.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
