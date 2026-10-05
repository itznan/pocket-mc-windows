using System;
using System.Threading;
using System.Threading.Tasks;
using PocketMC.Domain.Models;

namespace PocketMC.Application.Interfaces.Backups;

/// <summary>
/// Application service responsible for coordinating pre-start cloud synchronization,
/// post-stop uploads, distributed lock acquisition, and background lock heartbeat renewals.
/// </summary>
public interface ICloudSyncService
{
    Task PreStartSyncAsync(
        InstanceMetadata meta,
        string serverDir,
        Action<string>? onStatus = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    Task PostStopSyncAsync(
        InstanceMetadata meta,
        string serverDir,
        Action<string>? onStatus = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    Task<ServerCloudLock?> CheckLockStatusAsync(InstanceMetadata meta, CancellationToken ct = default);

    Task ForceReleaseLockAsync(InstanceMetadata meta, CancellationToken ct = default);

    Task<ServerSyncManifest?> CheckRemoteUpdatesAsync(InstanceMetadata meta, CancellationToken ct = default);
}
