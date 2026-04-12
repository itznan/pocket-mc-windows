using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Utils;
using PocketMC.Desktop.Features.Mods;
using PocketMC.Desktop.Features.Marketplace;

namespace PocketMC.Desktop.Features.Settings
{
    public class SettingsAddonsVM : ViewModelBase
    {
        private readonly InstanceMetadata _metadata;
        private readonly string _serverDir;
        private readonly ModpackService _modpackService;
        private readonly IDialogService _dialogService;
        private readonly IAppNavigationService _navigationService;
        private readonly IServiceProvider _serviceProvider;
        private readonly Func<bool> _isRunningCheck;
        private readonly Action _onAddonChanged;

        public ObservableCollection<PluginItemViewModel> Plugins { get; } = new();
        public ObservableCollection<ModItemViewModel> Mods { get; } = new();

        public bool ShowVanillaWarning => _metadata.ServerType?.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) == true;

        public ICommand AddPluginCommand { get; }
        public ICommand DeletePluginCommand { get; }
        public ICommand BrowseModrinthPluginsCommand { get; }
        public ICommand AddModCommand { get; }
        public ICommand DeleteModCommand { get; }
        public ICommand BrowseModrinthModsCommand { get; }
        public ICommand ImportModpackCommand { get; }
        public ICommand BrowseModpacksCommand { get; }

        public SettingsAddonsVM(
            InstanceMetadata metadata,
            string serverDir,
            ModpackService modpackService,
            IDialogService dialogService,
            IAppNavigationService navigationService,
            IServiceProvider serviceProvider,
            Func<bool> isRunningCheck,
            Action onAddonChanged)
        {
            _metadata = metadata;
            _serverDir = serverDir;
            _modpackService = modpackService;
            _dialogService = dialogService;
            _navigationService = navigationService;
            _serviceProvider = serviceProvider;
            _isRunningCheck = isRunningCheck;
            _onAddonChanged = onAddonChanged;

            AddPluginCommand = new RelayCommand(async _ => await AddPluginAsync(), _ => !_isRunningCheck() && !ShowVanillaWarning);
            DeletePluginCommand = new RelayCommand(async p => await DeletePluginAsync(p as string), _ => !_isRunningCheck());
            BrowseModrinthPluginsCommand = new RelayCommand(_ => BrowseModrinth("project_type:plugin"));

            AddModCommand = new RelayCommand(async _ => await AddModAsync(), _ => !_isRunningCheck());
            DeleteModCommand = new RelayCommand(async p => await DeleteModAsync(p as string), _ => !_isRunningCheck());
            BrowseModrinthModsCommand = new RelayCommand(_ => BrowseModrinth("project_type:mod"));

            ImportModpackCommand = new RelayCommand(async _ => await ImportModpackAsync());
            BrowseModpacksCommand = new RelayCommand(_ => BrowseModrinth("project_type:modpack"));
        }

        public void LoadAddons() { LoadPlugins(); LoadMods(); }

        private void LoadPlugins()
        {
            Plugins.Clear();
            var dir = Path.Combine(_serverDir, "plugins");
            if (!Directory.Exists(dir)) return;
            foreach (var jar in Directory.GetFiles(dir, "*.jar"))
            {
                var fi = new FileInfo(jar);
                string api = PluginScanner.TryGetApiVersion(jar) ?? "Unknown";
                string name = PluginScanner.TryGetPluginName(jar) ?? fi.Name;
                bool mismatch = PluginScanner.IsIncompatible(api == "Unknown" ? null : api, _metadata.MinecraftVersion);
                Plugins.Add(new PluginItemViewModel { Name = name, Path = jar, ApiVersion = api, SizeKb = fi.Length / 1024.0, IsMismatch = mismatch, LastModified = fi.LastWriteTime });
            }
        }

        private async Task AddPluginAsync()
        {
            var files = await _dialogService.OpenFilesDialogAsync("Select Plugin JAR(s)", "JAR Files (*.jar)|*.jar");
            foreach (var f in files)
            {
                var dir = Path.Combine(_serverDir, "plugins");
                Directory.CreateDirectory(dir);
                await FileUtils.CopyFileAsync(f, Path.Combine(dir, Path.GetFileName(f)), true);
            }
            LoadPlugins(); _onAddonChanged();
        }

        private async Task DeletePluginAsync(string? path)
        {
            if (path != null && await _dialogService.ShowDialogAsync("Confirm", $"Delete {Path.GetFileName(path)}?", DialogType.Question) == DialogResult.Yes)
            {
                try { await FileUtils.DeleteFileAsync(path); LoadPlugins(); _onAddonChanged(); }
                catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
            }
        }

        private void LoadMods()
        {
            Mods.Clear();
            var dir = Path.Combine(_serverDir, "mods");
            if (!Directory.Exists(dir)) return;
            foreach (var jar in Directory.GetFiles(dir, "*.jar"))
            {
                var fi = new FileInfo(jar);
                Mods.Add(new ModItemViewModel { Name = fi.Name, Path = jar, SizeKb = fi.Length / 1024.0, LastModified = fi.LastWriteTime });
            }
        }

        private async Task AddModAsync()
        {
            var files = await _dialogService.OpenFilesDialogAsync("Select Mod JAR(s)", "JAR Files (*.jar)|*.jar");
            foreach (var f in files)
            {
                var dir = Path.Combine(_serverDir, "mods");
                Directory.CreateDirectory(dir);
                await FileUtils.CopyFileAsync(f, Path.Combine(dir, Path.GetFileName(f)), true);
            }
            LoadMods(); _onAddonChanged();
        }

        private async Task DeleteModAsync(string? path)
        {
            if (path != null && await _dialogService.ShowDialogAsync("Confirm", $"Delete {Path.GetFileName(path)}?", DialogType.Question) == DialogResult.Yes)
            {
                try { await FileUtils.DeleteFileAsync(path); LoadMods(); _onAddonChanged(); }
                catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
            }
        }

        private void BrowseModrinth(string projectType)
        {
            var browserPage = ActivatorUtilities.CreateInstance<PluginBrowserPage>(_serviceProvider, _serverDir, _metadata.MinecraftVersion, projectType, (Action)(() => { if (projectType.Contains("plugin")) LoadPlugins(); else LoadMods(); _onAddonChanged(); }));
            if (projectType == "project_type:modpack") browserPage.OnModpackDownloaded += async (tempZip) => { await ImportModpackActionAsync(tempZip); try { File.Delete(tempZip); } catch { } };
            _navigationService.NavigateToDetailPage(browserPage, "Marketplace", DetailRouteKind.PluginBrowser, DetailBackNavigation.PreviousDetail);
        }

        private async Task ImportModpackAsync()
        {
            var file = await _dialogService.OpenFileDialogAsync("Select Modpack ZIP", "ZIP Files (*.zip)|*.zip");
            if (file != null) await ImportModpackActionAsync(file);
        }

        private async Task ImportModpackActionAsync(string zipPath)
        {
            try
            {
                var result = await _modpackService.ParseModpackZipAsync(zipPath);
                if (await _dialogService.ShowDialogAsync("Import Modpack", $"Import modpack '{result.Name}'?", DialogType.Question) == DialogResult.Yes)
                {
                    await _modpackService.ImportToExistingInstanceAsync(result, _metadata, _serverDir, zipPath);
                    LoadAddons(); _onAddonChanged();
                }
            }
            catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
        }
    }

    public class PluginItemViewModel
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string ApiVersion { get; set; } = "";
        public double SizeKb { get; set; }
        public bool IsMismatch { get; set; }
        public DateTime LastModified { get; set; }
    }

    public class ModItemViewModel
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public double SizeKb { get; set; }
        public DateTime LastModified { get; set; }
    }
}
