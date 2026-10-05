using System;
using System.Threading;
using System.Threading.Tasks;
using PocketMC.Domain.Models;

namespace PocketMC.Application.Interfaces.Backups;

/// <summary>
/// Contract for cloud providers supporting distributed locking, server sync packages,
/// and manifest negotiation.
/// </summary>
public interface ICloudSyncProvider
{
    CloudBackupProviderType ProviderType { get; }

    Task<ServerCloudLock?> ReadLockAsync(Guid instanceId, string instanceName, CancellationToken ct);

    Task<bool> WriteLockAsync(Guid instanceId, string instanceName, ServerCloudLock lockInfo, CancellationToken ct);

    Task DeleteLockAsync(Guid instanceId, string instanceName, string lockToken, CancellationToken ct);

    Task<ServerSyncManifest?> GetRemoteManifestAsync(Guid instanceId, string instanceName, CancellationToken ct);

    Task<CloudBackupUploadResult> UploadSyncPackageAsync(
        Guid instanceId,
        string instanceName,
        string localPackagePath,
        ServerSyncManifest manifest,
        IProgress<CloudBackupProgress>? progress,
        CancellationToken ct);

    Task DownloadSyncPackageAsync(
        Guid instanceId,
        string instanceName,
        string localDestinationPath,
        IProgress<double>? progress,
        CancellationToken ct);
}
