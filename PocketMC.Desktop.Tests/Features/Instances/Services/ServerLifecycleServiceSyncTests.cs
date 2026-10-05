using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using PocketMC.Application.Exceptions;
using PocketMC.Application.Interfaces.Backups;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Networking;
using PocketMC.Desktop.Tests.TestSupport.Fixtures;
using PocketMC.Domain.Models;
using Xunit;

namespace PocketMC.Desktop.Tests.Features.Instances.Services;

public sealed class ServerLifecycleServiceSyncTests
{
    [Fact]
    public async Task StartAsync_WhenServerLocked_ThrowsServerLockedExceptionAndBlocksLaunch()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var processManager = workspace.CreateServerProcessManager();
        var preflightService = workspace.CreatePortPreflightService(processManager);
        var probeService = workspace.CreatePortProbeService();
        var leaseRegistry = workspace.CreatePortLeaseRegistry();
        var recoveryService = workspace.CreatePortRecoveryService(probeService, leaseRegistry);
        var notifications = new RecordingNotificationService();

        var syncServiceMock = new Mock<ICloudSyncService>();
        var lockInfo = new ServerCloudLock
        {
            ServerName = "LockedServer",
            LockedByUser = "Dave",
            LockedByMachine = "DAVE-PC",
            HeartbeatUtc = DateTimeOffset.UtcNow,
            LeaseDurationSeconds = 60
        };

        syncServiceMock
            .Setup(s => s.PreStartSyncAsync(
                It.IsAny<InstanceMetadata>(),
                It.IsAny<string>(),
                It.IsAny<Action<string>>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ServerLockedException(lockInfo));

        var lifecycleService = new ServerLifecycleService(
            processManager,
            workspace.Registry,
            preflightService,
            probeService,
            leaseRegistry,
            recoveryService,
            notifications,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ServerLifecycleService>.Instance,
            workspace.AppState,
            new PocketMC.Infrastructure.Instances.GeyserProvisioningService(null!, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<PocketMC.Infrastructure.Instances.GeyserProvisioningService>.Instance),
            new PocketMC.Infrastructure.Instances.GeyserDetector(new PocketMC.Infrastructure.Marketplace.AddonManifestService()),
            null!,
            null!,
            syncServiceMock.Object);

        var meta = workspace.CreateInstance("LockedServer", serverType: "Paper");
        meta.CloudSync = new InstanceCloudSyncConfig
        {
            Enabled = true,
            Provider = CloudBackupProviderType.GoogleDrive
        };

        await Assert.ThrowsAsync<ServerLockedException>(() => lifecycleService.StartAsync(meta));
        Assert.False(lifecycleService.IsRunning(meta.Id));
        Assert.Contains(notifications.Messages, m => m.Contains("Server In Use") || m.Contains("Dave"));
    }

    [Fact]
    public async Task StopAsync_WhenCloudSyncEnabled_InvokesPostStopSync()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var processManager = workspace.CreateServerProcessManager();
        var preflightService = workspace.CreatePortPreflightService(processManager);
        var probeService = workspace.CreatePortProbeService();
        var leaseRegistry = workspace.CreatePortLeaseRegistry();
        var recoveryService = workspace.CreatePortRecoveryService(probeService, leaseRegistry);

        var syncServiceMock = new Mock<ICloudSyncService>();
        var lifecycleService = new ServerLifecycleService(
            processManager,
            workspace.Registry,
            preflightService,
            probeService,
            leaseRegistry,
            recoveryService,
            new RecordingNotificationService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ServerLifecycleService>.Instance,
            workspace.AppState,
            new PocketMC.Infrastructure.Instances.GeyserProvisioningService(null!, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<PocketMC.Infrastructure.Instances.GeyserProvisioningService>.Instance),
            new PocketMC.Infrastructure.Instances.GeyserDetector(new PocketMC.Infrastructure.Marketplace.AddonManifestService()),
            null!,
            null!,
            syncServiceMock.Object);

        var meta = workspace.CreateInstance("SyncStopServer", serverType: "Paper");
        meta.CloudSync = new InstanceCloudSyncConfig
        {
            Enabled = true,
            Provider = CloudBackupProviderType.GoogleDrive,
            AutoSyncOnStop = true
        };

        await lifecycleService.StopAsync(meta.Id);

        syncServiceMock.Verify(s => s.PostStopSyncAsync(
            meta,
            It.IsAny<string>(),
            It.IsAny<Action<string>>(),
            It.IsAny<IProgress<double>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
