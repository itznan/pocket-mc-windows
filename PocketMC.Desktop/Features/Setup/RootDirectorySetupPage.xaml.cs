using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Infrastructure.Process;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Core.Presentation;

namespace PocketMC.Desktop.Features.Setup
{
    public partial class RootDirectorySetupPage : Page
    {
        public event EventHandler<string>? DirectorySelected;
        private string? _selectedRootPath;

        public RootDirectorySetupPage()
        {
            InitializeComponent();

            string defaultParentDirectory = RootDirectorySetupHelper.GetDefaultParentDirectory();
            TxtSuggestedFolderName.Text = RootDirectorySetupHelper.SuggestedFolderName;
            TxtSuggestedPath.Text = Path.Combine(defaultParentDirectory, RootDirectorySetupHelper.SuggestedFolderName);
        }

        private void BtnSelectDirectory_Click(object sender, RoutedEventArgs e)
        {
            string defaultParentDirectory = RootDirectorySetupHelper.GetDefaultParentDirectory();

            var dialog = new OpenFolderDialog
            {
                Title = "Choose where to create the PocketMC root folder",
                Multiselect = false,
                InitialDirectory = defaultParentDirectory,
                DefaultDirectory = defaultParentDirectory,
                FolderName = RootDirectorySetupHelper.SuggestedFolderName
            };

            if (dialog.ShowDialog() != true)
            {
                SelectDirectoryButton.Focus();
                return;
            }

            _selectedRootPath = RootDirectorySetupHelper.ResolveRootPath(dialog.FolderName);
            TxtSuggestedPath.Text = _selectedRootPath;
            ContinueButton.IsEnabled = true;
            ContinueButton.Focus();
        }

        private void BtnContinue_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_selectedRootPath))
            {
                return;
            }

            DirectorySelected?.Invoke(this, _selectedRootPath);
        }
    }
}
