using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using PocketMC.Application.Exceptions;
using PocketMC.Application.Interfaces.Backups;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Backups;
using PocketMC.Infrastructure.Configuration;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Backups;

public sealed class CloudSyncServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<ICloudSyncProvider> _providerMock;
    private readonly Mock<ILogger<CloudSyncService>> _loggerMock;
    private readonly SettingsManager _settingsManager;

    public CloudSyncServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pocketmc-sync-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _providerMock = new Mock<ICloudSyncProvider>();
        _providerMock.SetupGet(p => p.ProviderType).Returns(CloudBackupProviderType.GoogleDrive);

        _loggerMock = new Mock<ILogger<CloudSyncService>>();
        _settingsManager = new SettingsManager(_tempDir);
    }

    [Fact]
    public async Task PreStartSyncAsync_ThrowsServerLockedException_WhenLockedByAnotherHost()
    {
        var meta = new InstanceMetadata
        {
            Id = Guid.NewGuid(),
            Name = "ActiveServer",
            CloudSync = new InstanceCloudSyncConfig
            {
                Enabled = true,
                Provider = CloudBackupProviderType.GoogleDrive
            }
        };

        var activeLock = new ServerCloudLock
        {
            InstanceId = meta.Id,
            ServerName = meta.Name,
            LockedByMachine = "OTHER-MACHINE-XYZ",
            LockedByUser = "Bob",
            HeartbeatUtc = DateTimeOffset.UtcNow,
            LeaseDurationSeconds = 120
        };

        _providerMock
            .Setup(p => p.ReadLockAsync(meta.Id, meta.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeLock);

        var service = new CloudSyncService(
            new[] { _providerMock.Object },
            _settingsManager,
            _loggerMock.Object);

        var ex = await Assert.ThrowsAsync<ServerLockedException>(() =>
            service.PreStartSyncAsync(meta, _tempDir));

        Assert.Equal("Bob", ex.LockInfo.LockedByUser);
        Assert.Equal("OTHER-MACHINE-XYZ", ex.LockInfo.LockedByMachine);
    }

    [Fact]
    public async Task PreStartSyncAsync_AcquiresLock_WhenExistingLockIsExpired()
    {
        var meta = new InstanceMetadata
        {
            Id = Guid.NewGuid(),
            Name = "ExpiredLockServer",
            CloudSync = new InstanceCloudSyncConfig
            {
                Enabled = true,
                Provider = CloudBackupProviderType.GoogleDrive,
                AutoSyncOnStart = false
            }
        };

        var expiredLock = new ServerCloudLock
        {
            InstanceId = meta.Id,
            ServerName = meta.Name,
            LockedByMachine = "CRASHED-PC",
            LockedByUser = "Charlie",
            HeartbeatUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            LeaseDurationSeconds = 60
        };

        _providerMock
            .Setup(p => p.ReadLockAsync(meta.Id, meta.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredLock);

        _providerMock
            .Setup(p => p.WriteLockAsync(meta.Id, meta.Name, It.IsAny<ServerCloudLock>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new CloudSyncService(
            new[] { _providerMock.Object },
            _settingsManager,
            _loggerMock.Object);

        await service.PreStartSyncAsync(meta, _tempDir);

        _providerMock.Verify(p => p.WriteLockAsync(meta.Id, meta.Name, It.IsAny<ServerCloudLock>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BuildSyncPackage_ExcludesLogsAndIncludesWorldAndProperties()
    {
        var meta = new InstanceMetadata
        {
            Id = Guid.NewGuid(),
            Name = "PackageTestServer",
            CloudSync = new InstanceCloudSyncConfig
            {
                Enabled = true,
                SyncWorld = true
            }
        };

        string serverDir = Path.Combine(_tempDir, "server");
        string worldDir = Path.Combine(serverDir, "world");
        string logsDir = Path.Combine(serverDir, "logs");
        Directory.CreateDirectory(worldDir);
        Directory.CreateDirectory(logsDir);

        await File.WriteAllTextAsync(Path.Combine(serverDir, "server.properties"), "motd=Test");
        await File.WriteAllTextAsync(Path.Combine(worldDir, "level.dat"), "world-data");
        await File.WriteAllTextAsync(Path.Combine(worldDir, "session.lock"), "lock");
        await File.WriteAllTextAsync(Path.Combine(logsDir, "latest.log"), "log-content");

        var service = new CloudSyncService(
            new[] { _providerMock.Object },
            _settingsManager,
            _loggerMock.Object);

        string zipPath = Path.Combine(_tempDir, "test-sync.zip");
        var manifest = await service.BuildSyncPackageAsync(meta, serverDir, zipPath, 1, CancellationToken.None);

        Assert.True(File.Exists(zipPath));
        Assert.True(manifest.Entries.ContainsKey("server.properties"));
        Assert.True(manifest.Entries.ContainsKey("world/level.dat"));
        Assert.False(manifest.Entries.ContainsKey("world/session.lock"));
        Assert.False(manifest.Entries.ContainsKey("logs/latest.log"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }
}
