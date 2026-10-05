using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using PocketMC.Application.Interfaces;
using PocketMC.Application.Interfaces.Backups;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Instances.CloudSync;
using PocketMC.Domain.Models;
using Xunit;

namespace PocketMC.Desktop.Tests.Features.Instances.CloudSync;

public sealed class ServerCloudSyncViewModelTests
{
    private readonly Mock<ICloudSyncService> _syncServiceMock;
    private readonly Mock<IDialogService> _dialogServiceMock;
    private readonly InstanceMetadata _metadata;
    private bool _savedMetadata;

    public ServerCloudSyncViewModelTests()
    {
        _syncServiceMock = new Mock<ICloudSyncService>();
        _dialogServiceMock = new Mock<IDialogService>();
        _metadata = new InstanceMetadata
        {
            Id = Guid.NewGuid(),
            Name = "SyncServer",
            CloudSync = new InstanceCloudSyncConfig
            {
                Enabled = true,
                Provider = CloudBackupProviderType.GoogleDrive
            }
        };
        _savedMetadata = false;
    }

    [Fact]
    public async Task RefreshLockStatusAsync_ReportsUnlocked_WhenNoLockExists()
    {
        _syncServiceMock
            .Setup(s => s.CheckLockStatusAsync(_metadata, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ServerCloudLock?)null);

        var vm = new ServerCloudSyncViewModel(
            _metadata,
            _syncServiceMock.Object,
            _dialogServiceMock.Object,
            () => "/fake/dir",
            () => _savedMetadata = true);

        await vm.RefreshLockStatusAsync();

        Assert.False(vm.IsLocked);
        Assert.Contains("Unlocked", vm.LockStatusText);
    }

    [Fact]
    public async Task RefreshLockStatusAsync_ReportsLocked_WhenActiveLockFromAnotherUser()
    {
        var activeLock = new ServerCloudLock
        {
            InstanceId = _metadata.Id,
            ServerName = _metadata.Name,
            LockedByMachine = "HOST-BOB-PC",
            LockedByUser = "Bob",
            HeartbeatUtc = DateTimeOffset.UtcNow,
            LeaseDurationSeconds = 60
        };

        _syncServiceMock
            .Setup(s => s.CheckLockStatusAsync(_metadata, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeLock);

        var vm = new ServerCloudSyncViewModel(
            _metadata,
            _syncServiceMock.Object,
            _dialogServiceMock.Object,
            () => "/fake/dir",
            () => _savedMetadata = true);

        await vm.RefreshLockStatusAsync();

        Assert.True(vm.IsLocked);
        Assert.Contains("Bob", vm.LockStatusText);
        Assert.Contains("HOST-BOB-PC", vm.LockStatusText);
    }

    [Fact]
    public async Task ForceUnlockAsync_InvokesService_WhenUserConfirms()
    {
        _dialogServiceMock
            .Setup(d => d.ShowDialogAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                DialogType.Warning,
                It.IsAny<bool>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .ReturnsAsync(DialogResult.Yes);

        _syncServiceMock
            .Setup(s => s.ForceReleaseLockAsync(_metadata, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var vm = new ServerCloudSyncViewModel(
            _metadata,
            _syncServiceMock.Object,
            _dialogServiceMock.Object,
            () => "/fake/dir",
            () => _savedMetadata = true);

        await vm.ForceUnlockAsync();

        _syncServiceMock.Verify(s => s.ForceReleaseLockAsync(_metadata, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void AutoSyncOnStart_WhenChanged_SavesMetadata()
    {
        var vm = new ServerCloudSyncViewModel(
            _metadata,
            _syncServiceMock.Object,
            _dialogServiceMock.Object,
            () => "/fake/dir",
            () => _savedMetadata = true);

        vm.AutoSyncOnStart = !vm.AutoSyncOnStart;
        Assert.True(_savedMetadata);
    }
}
