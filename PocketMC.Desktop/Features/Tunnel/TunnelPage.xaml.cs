using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Dashboard;

namespace PocketMC.Desktop.Features.Tunnel
{
    public partial class TunnelPage : Page
    {
        private enum TunnelUiState
        {
            Missing,
            Downloading,
            AwaitingSetupCode,
            Provisioning,
            Ready,
            Starting,
            ReauthRequired,
            Connected
        }

        private readonly ApplicationState _applicationState;
        private readonly PlayitAgentService _playitAgentService;
        private readonly PlayitApiClient _playitApiClient;
        private readonly PlayitPartnerProvisioningClient _partnerProvisioningClient;
        private readonly IAppNavigationService _navigationService;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TunnelPage> _logger;
        private bool _isSubscribed;
        private int _refreshVersion;
        private TunnelUiState _currentUiState = TunnelUiState.Missing;

        public TunnelPage(
            ApplicationState applicationState,
            PlayitAgentService playitAgentService,
            PlayitApiClient playitApiClient,
            PlayitPartnerProvisioningClient partnerProvisioningClient,
            IAppNavigationService navigationService,
            IServiceProvider serviceProvider,
            ILogger<TunnelPage> logger)
        {
            InitializeComponent();
            _applicationState = applicationState;
            _playitAgentService = playitAgentService;
            _playitApiClient = playitApiClient;
            _partnerProvisioningClient = partnerProvisioningClient;
            _navigationService = navigationService;
            _serviceProvider = serviceProvider;
            _logger = logger;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            SubscribeToAgent();
            await RefreshStatusAsync();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            UnsubscribeFromAgent();
            // Important: we no longer cancel download on unload,
            // as it runs statefully in the background and can complete while another tab is active.
        }

        private void SubscribeToAgent()
        {
            if (_isSubscribed) return;

            _playitAgentService.OnStateChanged += OnPlayitAgentStateChanged;
            _playitAgentService.OnTunnelRunning += OnPlayitTunnelRunning;
            _playitAgentService.OnDownloadStatusChanged += OnPlayitDownloadStatusChanged;
            _playitAgentService.OnDownloadProgressChanged += OnPlayitDownloadProgressChanged;
            _isSubscribed = true;
        }

        private void UnsubscribeFromAgent()
        {
            if (!_isSubscribed) return;

            _playitAgentService.OnStateChanged -= OnPlayitAgentStateChanged;
            _playitAgentService.OnTunnelRunning -= OnPlayitTunnelRunning;
            _playitAgentService.OnDownloadStatusChanged -= OnPlayitDownloadStatusChanged;
            _playitAgentService.OnDownloadProgressChanged -= OnPlayitDownloadProgressChanged;
            _isSubscribed = false;
        }

        private void OnPlayitDownloadStatusChanged(object? sender, bool isDownloading)
        {
            Dispatcher.BeginInvoke(new Action(() => _ = RefreshStatusAsync()));
        }

        private void OnPlayitDownloadProgressChanged(object? sender, DownloadProgress progressValue)
        {
            Dispatcher.Invoke(() =>
            {
                if (!_playitAgentService.IsDownloadingBinary) return;

                DownloadProgressBar.Visibility = Visibility.Visible;
                TxtDownloadProgress.Visibility = Visibility.Visible;

                if (progressValue.TotalBytes > 0)
                {
                    DownloadProgressBar.IsIndeterminate = false;
                    DownloadProgressBar.Value = progressValue.Percentage;
                    TxtDownloadProgress.Text =
                        $"{Math.Round(progressValue.Percentage)}% \u2022 {FormatBytes(progressValue.BytesRead)} / {FormatBytes(progressValue.TotalBytes)}";
                }
                else
                {
                    DownloadProgressBar.IsIndeterminate = true;
                    TxtDownloadProgress.Text = $"Downloaded {FormatBytes(progressValue.BytesRead)}...";
                }
            });
        }

        private void OnPlayitAgentStateChanged(object? sender, PlayitAgentState state)
        {
            Dispatcher.BeginInvoke(new Action(() => _ = RefreshStatusAsync()));
        }

        private void OnPlayitTunnelRunning(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => _ = RefreshStatusAsync()));
        }

        private async Task RefreshStatusAsync()
        {
            int refreshVersion = Interlocked.Increment(ref _refreshVersion);

            if (!_applicationState.IsConfigured)
            {
                SetUiState(TunnelUiState.Missing, "Missing", "PocketMC is not configured with an app root path yet.", Brushes.Orange);
                TxtExecutablePath.Text = "App root not configured";
                ShowNoTunnels("Finish PocketMC setup before managing tunnels.");
                UpdateActionButtons(binaryExists: false);
                return;
            }

            string executablePath = _applicationState.GetPlayitExecutablePath();
            bool binaryExists = File.Exists(executablePath);
            bool partialExists = File.Exists(executablePath + ".partial");

            TxtExecutablePath.Text = executablePath;
            bool isDownloading = _playitAgentService.IsDownloadingBinary;

            if (!isDownloading)
            {
                DownloadProgressBar.Visibility = Visibility.Collapsed;
                DownloadProgressBar.IsIndeterminate = false;
            }

            if (isDownloading)
            {
                SetUiState(TunnelUiState.Downloading, "Downloading", "PocketMC is downloading the Playit.gg agent.", Brushes.DeepSkyBlue);
                ShowNoTunnels("The tunnel list will appear after the agent is downloaded and connected.");
                UpdateActionButtons(binaryExists);
                return;
            }

            if (!binaryExists)
            {
                string detail = partialExists
                    ? "A partial agent download was found. Click Download Agent to resume the transfer."
                    : "playit.exe is missing from the tunnel folder. Download the agent to enable public tunnels.";
                SetUiState(TunnelUiState.Missing, "Missing", detail, Brushes.Orange);
                ShowNoTunnels("Download the Playit agent to begin tunnel setup.");
                UpdateActionButtons(binaryExists: false);
                return;
            }

            switch (_playitAgentService.State)
            {
                case PlayitAgentState.ProvisioningAgent:
                    SetUiState(TunnelUiState.Provisioning, "Provisioning", "PocketMC is linking your Playit account and creating a self-managed agent.", Brushes.DeepSkyBlue);
                    ShowNoTunnels("Waiting for Playit provisioning to finish.");
                    UpdateActionButtons(binaryExists: true);
                    return;

                case PlayitAgentState.Starting:
                    SetUiState(TunnelUiState.Starting, "Starting", "Launching the Playit agent and waiting for the tunnel service to come online.", Brushes.Gold);
                    ShowNoTunnels("Waiting for the Playit agent to finish starting.");
                    UpdateActionButtons(binaryExists: true);
                    return;

                case PlayitAgentState.AwaitingSetupCode:
                    SetUiState(TunnelUiState.AwaitingSetupCode, "Awaiting Setup", "Click Setup Agent to link your Playit.gg account.", Brushes.Gold);
                    ShowNoTunnels("Link Playit with a setup code to load tunnel information.");
                    UpdateActionButtons(binaryExists: true);
                    return;

                case PlayitAgentState.Connected:
                    await RefreshTunnelInventoryAsync(refreshVersion);
                    UpdateActionButtons(binaryExists: true);
                    return;

                case PlayitAgentState.ReauthRequired:
                    SetUiState(
                        TunnelUiState.ReauthRequired,
                        "Reconnect Required",
                        _playitAgentService.LastErrorMessage ?? "The saved Playit credentials are no longer valid. Click Setup Agent to connect again.",
                        Brushes.Orange);
                    ShowNoTunnels("Reconnect Playit to restore tunnel access.");
                    UpdateActionButtons(binaryExists: true);
                    return;

                case PlayitAgentState.Error:
                case PlayitAgentState.Disconnected:
                case PlayitAgentState.Stopped:
                default:
                    bool hasPartnerConnection = !string.IsNullOrWhiteSpace(_playitAgentService.PartnerConnection?.AgentSecretKey);
                    SetUiState(
                        hasPartnerConnection ? TunnelUiState.Ready : TunnelUiState.AwaitingSetupCode,
                        hasPartnerConnection ? "Ready" : "Awaiting Setup",
                        hasPartnerConnection
                            ? "PocketMC has Playit credentials saved. Click Connect to start the embedded agent."
                            : "Click Setup Agent to link your Playit.gg account.",
                        hasPartnerConnection ? Brushes.Silver : Brushes.Gold);
                    ShowNoTunnels(
                        hasPartnerConnection
                            ? "Connect the Playit agent to load tunnel information."
                            : "Link Playit with the setup wizard to load tunnel information.");
                    UpdateActionButtons(binaryExists: true);
                    return;
            }
        }

        private async Task RefreshTunnelInventoryAsync(int refreshVersion)
        {
            try
            {
                TunnelListResult result = await _playitApiClient.GetTunnelsAsync();
                if (refreshVersion != _refreshVersion)
                {
                    return;
                }

                if (result.Success)
                {
                    int tunnelCount = result.Tunnels.Count;
                    string detail = tunnelCount > 0
                        ? $"Connected. {tunnelCount} tunnel{(tunnelCount == 1 ? string.Empty : "s")} currently available."
                        : "Connected. The agent is online, but no tunnels have been created yet.";

                    SetUiState(TunnelUiState.Connected, "Connected", detail, Brushes.LimeGreen);
                    ShowTunnels(
                        result.Tunnels,
                        tunnelCount > 0
                            ? "Tunnel routing is active."
                            : "Create or start a server tunnel to see entries here.");
                    return;
                }

                if (result.RequiresClaim)
                {
                    SetUiState(
                        TunnelUiState.AwaitingSetupCode,
                        "Awaiting Setup",
                        result.ErrorMessage ?? "PocketMC needs a Playit setup code. Click Setup Agent to get started.",
                        Brushes.Gold);
                    ShowNoTunnels("Link Playit to load your tunnels.");
                    return;
                }

                if (result.IsTokenInvalid)
                {
                    SetUiState(
                        TunnelUiState.Ready,
                        "Reconnect Required",
                        result.ErrorMessage ?? "The saved Playit credentials were rejected. Click Setup Agent to reconnect.",
                        Brushes.Orange);
                    ShowNoTunnels("Tunnel data is unavailable until the agent is linked again.");
                    return;
                }

                SetUiState(TunnelUiState.Connected, "Connected", "The Playit agent is online, but the tunnel API could not be reached right now.", Brushes.LimeGreen);
                ShowNoTunnels(result.ErrorMessage ?? "Tunnel data is temporarily unavailable.");
            }
            catch (Exception ex)
            {
                if (refreshVersion != _refreshVersion)
                {
                    return;
                }

                _logger.LogWarning(ex, "Failed to refresh Playit tunnel inventory.");
                SetUiState(TunnelUiState.Connected, "Connected", "The Playit agent is online, but PocketMC could not refresh the tunnel list.", Brushes.LimeGreen);
                ShowNoTunnels("Retry in a moment or click Refresh to try again.");
            }
        }

        private void SetUiState(TunnelUiState uiState, string status, string detail, Brush foreground)
        {
            _currentUiState = uiState;
            TxtStatusValue.Text = status;
            TxtStatusValue.Foreground = foreground;
            TxtStatusDetail.Text = detail;
        }

        private void ShowNoTunnels(string message)
        {
            TunnelList.ItemsSource = null;
            TunnelList.Visibility = Visibility.Collapsed;
            TxtTunnelListStatus.Text = message;
        }

        private void ShowTunnels(IReadOnlyCollection<TunnelData> tunnels, string message)
        {
            if (tunnels.Count == 0)
            {
                ShowNoTunnels(message);
                return;
            }

            TunnelList.ItemsSource = tunnels;
            TunnelList.Visibility = Visibility.Visible;
            TxtTunnelListStatus.Text = message;
        }

        private void BtnCopyAddress_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is string address && !string.IsNullOrEmpty(address))
            {
                try
                {
                    System.Windows.Clipboard.SetText(address);
                    btn.Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Checkmark24 };
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    timer.Tick += (s, args) =>
                    {
                        btn.Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Copy24 };
                        timer.Stop();
                    };
                    timer.Start();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to copy tunnel address to clipboard.");
                }
            }
        }

        private void UpdateActionButtons(bool binaryExists)
        {
            bool partialExists = _applicationState.IsConfigured && File.Exists(_applicationState.GetPlayitExecutablePath() + ".partial");
            bool isDownloading = _playitAgentService.IsDownloadingBinary;
            bool hasSavedConnection = !string.IsNullOrWhiteSpace(_playitAgentService.PartnerConnection?.AgentSecretKey);

            BtnDownloadAgent.Visibility = binaryExists ? Visibility.Collapsed : Visibility.Visible;
            BtnDownloadAgent.IsEnabled = !isDownloading;
            BtnDownloadAgent.Content = partialExists ? "Resume Download" : "Download Agent";

            // Setup Agent is shown when no saved connection exists (needs setup)
            BtnSetupAgent.Visibility = (!hasSavedConnection && binaryExists) ? Visibility.Visible : Visibility.Collapsed;
            BtnSetupAgent.IsEnabled = !isDownloading && binaryExists;

            // Connect is shown when there IS a saved connection (just needs to start the agent)
            BtnConnect.Visibility = hasSavedConnection ? Visibility.Visible : Visibility.Collapsed;
            BtnConnect.Content = _currentUiState == TunnelUiState.ReauthRequired ? "Reconnect" : "Connect";
            BtnConnect.IsEnabled =
                !isDownloading &&
                binaryExists &&
                _currentUiState is TunnelUiState.Ready or TunnelUiState.AwaitingSetupCode or TunnelUiState.ReauthRequired;

            BtnDisconnect.IsEnabled = !isDownloading && hasSavedConnection;

            BtnRefresh.IsEnabled = !isDownloading;
        }

        private void BtnDownloadAgent_Click(object sender, RoutedEventArgs e)
        {
            if (!_applicationState.IsConfigured || _playitAgentService.IsDownloadingBinary)
            {
                return;
            }

            TxtDownloadProgress.Visibility = Visibility.Visible;
            TxtDownloadProgress.Text = "Starting download...";

            _ = _playitAgentService.DownloadAgentAsync();
        }

        /// <summary>
        /// Opens the Setup Agent wizard as a detail page.
        /// </summary>
        private void BtnSetupAgent_Click(object sender, RoutedEventArgs e)
        {
            var wizardPage = ActivatorUtilities.CreateInstance<PlayitSetupWizardPage>(_serviceProvider);
            _navigationService.NavigateToDetailPage(
                wizardPage,
                "Playit Agent Setup",
                DetailRouteKind.PlayitSetupWizard,
                DetailBackNavigation.Tunnel,
                clearDetailStack: true);
        }

        /// <summary>
        /// Starts or restarts the Playit agent when saved credentials already exist.
        /// </summary>
        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (!_applicationState.IsConfigured || !File.Exists(_applicationState.GetPlayitExecutablePath()) || _playitAgentService.IsDownloadingBinary)
            {
                await RefreshStatusAsync();
                return;
            }

            try
            {
                SetUiState(TunnelUiState.Starting, "Starting", "Launching the Playit agent and waiting for it to connect.", Brushes.Gold);
                ShowNoTunnels("Waiting for the Playit agent to come online.");

                if (_playitAgentService.IsRunning)
                {
                    await _playitAgentService.RestartAsync();
                }
                else
                {
                    _playitAgentService.Start();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Manual Playit connection attempt failed.");
                SetUiState(TunnelUiState.Ready, "Ready", $"PocketMC could not start the agent: {ex.Message}", Brushes.Orange);
            }

            UpdateActionButtons(binaryExists: true);
            await RefreshStatusAsync();
        }

        private async void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            _playitAgentService.Disconnect();
            await RefreshStatusAsync();
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshStatusAsync();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 B";
            }

            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes;
            int unitIndex = 0;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return $"{size:0.#} {units[unitIndex]}";
        }
    }
}
