using System;

namespace PocketMC.Domain.Models;

/// <summary>
/// Server instance cloud synchronization settings and state.
/// </summary>
public class InstanceCloudSyncConfig
{
    public bool Enabled { get; set; } = false;
    public CloudBackupProviderType Provider { get; set; } = CloudBackupProviderType.GoogleDrive;
    public string? RemoteFolderId { get; set; }
    public string? RemoteShareLink { get; set; }
    public bool AutoSyncOnStart { get; set; } = true;
    public bool AutoSyncOnStop { get; set; } = true;
    public bool SyncWorld { get; set; } = true;
    public bool SyncConfig { get; set; } = true;
    public bool SyncMods { get; set; } = true;
    public bool SyncPlugins { get; set; } = true;
    public DateTimeOffset? LastSyncedAtUtc { get; set; }
    public long LastSyncVersion { get; set; }
    public string? LastSyncSha256 { get; set; }
}
