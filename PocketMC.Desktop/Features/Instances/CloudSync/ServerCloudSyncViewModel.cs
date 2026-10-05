using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PocketMC.Application.Interfaces;
using PocketMC.Application.Interfaces.Backups;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Features.Instances.CloudSync;

/// <summary>
/// ViewModel managing cloud storage synchronization settings, lock monitoring,
/// and manual sync operations for a Minecraft server instance.
/// </summary>
public class ServerCloudSyncViewModel : ViewModelBase
{
    private readonly InstanceMetadata _metadata;
    private readonly ICloudSyncService _cloudSyncService;
    private readonly IDialogService _dialogService;
    private readonly Func<string> _getServerDir;
    private readonly Action _saveMetadata;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private string _lockStatusText = "Checking lock status...";
    public string LockStatusText
    {
        get => _lockStatusText;
        set => SetProperty(ref _lockStatusText, value);
    }

    private bool _isLocked;
    public bool IsLocked
    {
        get => _isLocked;
        set => SetProperty(ref _isLocked, value);
    }

    public bool IsEnabled
    {
        get => _metadata.CloudSync.Enabled;
        set
        {
            if (_metadata.CloudSync.Enabled != value)
            {
                _metadata.CloudSync.Enabled = value;
                OnPropertyChanged();
                _saveMetadata();
                _ = RefreshLockStatusAsync();
            }
        }
    }

    public CloudBackupProviderType SelectedProvider
    {
        get => _metadata.CloudSync.Provider;
        set
        {
            if (_metadata.CloudSync.Provider != value)
            {
                _metadata.CloudSync.Provider = value;
                OnPropertyChanged();
                _saveMetadata();
                _ = RefreshLockStatusAsync();
            }
        }
    }

    public IReadOnlyList<CloudBackupProviderType> AvailableProviders { get; } = new[]
    {
        CloudBackupProviderType.GoogleDrive,
        CloudBackupProviderType.Dropbox,
        CloudBackupProviderType.OneDrive
    };

    public bool AutoSyncOnStart
    {
        get => _metadata.CloudSync.AutoSyncOnStart;
        set
        {
            if (_metadata.CloudSync.AutoSyncOnStart != value)
            {
                _metadata.CloudSync.AutoSyncOnStart = value;
                OnPropertyChanged();
                _saveMetadata();
            }
        }
    }

    public bool AutoSyncOnStop
    {
        get => _metadata.CloudSync.AutoSyncOnStop;
        set
        {
            if (_metadata.CloudSync.AutoSyncOnStop != value)
            {
                _metadata.CloudSync.AutoSyncOnStop = value;
                OnPropertyChanged();
                _saveMetadata();
            }
        }
    }

    public bool SyncWorld
    {
        get => _metadata.CloudSync.SyncWorld;
        set
        {
            if (_metadata.CloudSync.SyncWorld != value)
            {
                _metadata.CloudSync.SyncWorld = value;
                OnPropertyChanged();
                _saveMetadata();
            }
        }
    }

    public bool SyncConfig
    {
        get => _metadata.CloudSync.SyncConfig;
        set
        {
            if (_metadata.CloudSync.SyncConfig != value)
            {
                _metadata.CloudSync.SyncConfig = value;
                OnPropertyChanged();
                _saveMetadata();
            }
        }
    }

    public bool SyncMods
    {
        get => _metadata.CloudSync.SyncMods;
        set
        {
            if (_metadata.CloudSync.SyncMods != value)
            {
                _metadata.CloudSync.SyncMods = value;
                OnPropertyChanged();
                _saveMetadata();
            }
        }
    }

    public bool SyncPlugins
    {
        get => _metadata.CloudSync.SyncPlugins;
        set
        {
            if (_metadata.CloudSync.SyncPlugins != value)
            {
                _metadata.CloudSync.SyncPlugins = value;
                OnPropertyChanged();
                _saveMetadata();
            }
        }
    }

    public string LastSyncText => _metadata.CloudSync.LastSyncedAtUtc.HasValue
        ? $"Last synced: {_metadata.CloudSync.LastSyncedAtUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm} (v{_metadata.CloudSync.LastSyncVersion})"
        : "Never synchronized with cloud storage";

    public ICommand RefreshLockStatusCommand { get; }
    public ICommand ForceUnlockCommand { get; }
    public ICommand ManualSyncNowCommand { get; }

    public ServerCloudSyncViewModel(
        InstanceMetadata metadata,
        ICloudSyncService cloudSyncService,
        IDialogService dialogService,
        Func<string> getServerDir,
        Action saveMetadata)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _cloudSyncService = cloudSyncService ?? throw new ArgumentNullException(nameof(cloudSyncService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _getServerDir = getServerDir ?? throw new ArgumentNullException(nameof(getServerDir));
        _saveMetadata = saveMetadata ?? throw new ArgumentNullException(nameof(saveMetadata));

        RefreshLockStatusCommand = new RelayCommand(async _ => await RefreshLockStatusAsync(), _ => !IsBusy);
        ForceUnlockCommand = new RelayCommand(async _ => await ForceUnlockAsync(), _ => !IsBusy && IsLocked);
        ManualSyncNowCommand = new RelayCommand(async _ => await ManualSyncNowAsync(), _ => !IsBusy && IsEnabled);

        _ = RefreshLockStatusAsync();
    }

    public async Task RefreshLockStatusAsync()
    {
        if (!IsEnabled)
        {
            LockStatusText = "Cloud sync is disabled for this server.";
            IsLocked = false;
            return;
        }

        try
        {
            IsBusy = true;
            LockStatusText = "Checking cloud lock...";

            var remoteLock = await _cloudSyncService.CheckLockStatusAsync(_metadata, CancellationToken.None);

            if (remoteLock == null || remoteLock.IsExpired())
            {
                LockStatusText = "Available (Unlocked)";
                IsLocked = false;
            }
            else
            {
                bool isMine = remoteLock.IsOwnedBy(Environment.MachineName);
                LockStatusText = isMine
                    ? $"Locked by this machine (Active session)"
                    : $"In use by {remoteLock.LockedByUser} on {remoteLock.LockedByMachine}";
                IsLocked = true;
            }
        }
        catch (Exception ex)
        {
            LockStatusText = $"Unable to check lock status: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ForceUnlockAsync()
    {
        var result = await _dialogService.ShowDialogAsync(
            "Force Unlock Server?",
            "Forcing an unlock when another user is actively running the server can cause world corruption. Are you sure the previous session crashed or abandoned the lock?",
            DialogType.Warning);

        if (result != DialogResult.Yes && result != DialogResult.Ok) return;

        try
        {
            IsBusy = true;
            await _cloudSyncService.ForceReleaseLockAsync(_metadata, CancellationToken.None);
            _dialogService.ShowMessage("Success", "Cloud server lock has been released.", DialogType.Information);
            await RefreshLockStatusAsync();
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessage("Unlock Error", $"Failed to release lock: {ex.Message}", DialogType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ManualSyncNowAsync()
    {
        try
        {
            IsBusy = true;
            string serverDir = _getServerDir();

            StatusMessage = "Starting cloud sync...";
            await _cloudSyncService.PostStopSyncAsync(
                _metadata,
                serverDir,
                status => StatusMessage = status,
                null,
                CancellationToken.None);

            OnPropertyChanged(nameof(LastSyncText));
            _dialogService.ShowMessage("Sync Complete", "Server files have been successfully synchronized with the cloud.", DialogType.Information);
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessage("Sync Error", $"Manual synchronization failed: {ex.Message}", DialogType.Error);
        }
        finally
        {
            IsBusy = false;
            StatusMessage = string.Empty;
            await RefreshLockStatusAsync();
        }
    }
}
