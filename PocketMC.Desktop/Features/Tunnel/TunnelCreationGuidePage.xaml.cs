using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Infrastructure;

namespace PocketMC.Desktop.Features.Tunnel
{
    public partial class TunnelCreationGuidePage : Page
    {
        private readonly IAppNavigationService _navigationService;
        private readonly TunnelService _tunnelService;
        private readonly WindowsToastNotificationService _toastNotificationService;
        private readonly int _serverPort;
        private readonly bool _isBedrockTunnel;
        private CancellationTokenSource? _pollingCts;
        private int _closeRequested;

        public event Action<string>? OnTunnelResolved;
        public string? ResolvedAddress { get; private set; }

        public TunnelCreationGuidePage(
            IAppNavigationService navigationService,
            TunnelService tunnelService,
            WindowsToastNotificationService toastNotificationService,
            int serverPort,
            bool isBedrockTunnel = false)
        {
            InitializeComponent();
            _navigationService = navigationService;
            _tunnelService = tunnelService;
            _toastNotificationService = toastNotificationService;
            _serverPort = serverPort;
            _isBedrockTunnel = isBedrockTunnel;

            PortValueRun.Text = serverPort.ToString();

            // Update step 2 and the port instruction based on tunnel type
            if (isBedrockTunnel)
            {
                TunnelTypeStep.Text = "Select tunnel type: Minecraft Bedrock.";
                PortStepNote.Text = $" — Bedrock clients connect on this UDP port.";
            }

            _pollingCts = new CancellationTokenSource();
            _ = PollForTunnelAsync(_pollingCts.Token);

            Unloaded += TunnelCreationGuidePage_Unloaded;
        }

        private async Task PollForTunnelAsync(CancellationToken token)
        {
            try
            {
                string? address = await _tunnelService.PollForNewTunnelAsync(_serverPort, token);

                if (address != null)
                {
                    ResolvedAddress = address;
                    OnTunnelResolved?.Invoke(address);
                    _toastNotificationService.ShowTunnelCreated(_serverPort, address);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        StatusText.Text = $"✓ Tunnel found: {address}";
                        RequestClose();
                    });
                }
                else
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        StatusText.Text = "Timed out waiting for tunnel.";
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusText.Text = $"Error: {ex.Message}";
                });
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            RequestClose();
        }

        private void TunnelCreationGuidePage_Unloaded(object sender, RoutedEventArgs e)
        {
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            _pollingCts = null;
        }

        private void RequestClose()
        {
            if (Interlocked.Exchange(ref _closeRequested, 1) != 0)
            {
                return;
            }

            _pollingCts?.Cancel();

            if (!_navigationService.NavigateBack())
            {
                _navigationService.NavigateToDashboard();
            }
        }
    }
}
