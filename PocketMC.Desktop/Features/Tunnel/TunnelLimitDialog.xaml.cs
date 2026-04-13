using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Dashboard;

namespace PocketMC.Desktop.Features.Tunnel
{
    public partial class TunnelLimitDialog : Wpf.Ui.Controls.FluentWindow
    {
        /// <summary>
        /// True if the user chose "Change Port" — the caller should navigate
        /// to the server configuration screen.
        /// </summary>
        public bool UserChoseChangePort { get; private set; }

        public TunnelLimitDialog(List<TunnelData> existingTunnels)
        {
            InitializeComponent();
            TunnelListControl.ItemsSource = existingTunnels;
        }

        private void OpenDashboardButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://playit.gg/account/setup/new-tunnel",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open the Playit dashboard: {ex}");
            }

            Close();
        }

        private void ChangePortButton_Click(object sender, RoutedEventArgs e)
        {
            UserChoseChangePort = true;
            Close();
        }
    }
}
