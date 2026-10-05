using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Application.Exceptions;
using PocketMC.Application.Interfaces.Backups;
using PocketMC.Domain.Models;
using PocketMC.Domain.Security;
using PocketMC.Domain.Storage;
using PocketMC.Infrastructure.Configuration;

namespace PocketMC.Infrastructure.Backups;

public class CloudSyncService : ICloudSyncService, IDisposable
{
    private readonly IEnumerable<ICloudSyncProvider> _syncProviders;
    private readonly SettingsManager _settingsManager;
    private readonly ILogger<CloudSyncService> _logger;

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _heartbeatCts = new();
    private readonly ConcurrentDictionary<Guid, ServerCloudLock> _activeLocks = new();

    private static readonly HashSet<string> ExcludedTopLevelFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "logs",
        "crash-reports",
        "backups",
        "java",
        "php",
        "bin",
        "temp"
    };

    private static readonly HashSet<string> ExcludedFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".log",
        ".hprof",
        ".dump",
        ".sock",
        ".tmp"
    };

    public CloudSyncService(
        IEnumerable<ICloudSyncProvider> syncProviders,
        SettingsManager settingsManager,
        ILogger<CloudSyncService> logger)
    {
        _syncProviders = syncProviders;
        _settingsManager = settingsManager;
        _logger = logger;
    }

    private ICloudSyncProvider GetRequiredProvider(CloudBackupProviderType type)
    {
        var provider = _syncProviders.FirstOrDefault(p => p.ProviderType == type);
        if (provider == null)
        {
            throw new InvalidOperationException($"No cloud sync provider found for {type}.");
        }
        return provider;
    }

    public async Task PreStartSyncAsync(
        InstanceMetadata meta,
        string serverDir,
        Action<string>? onStatus = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (meta == null) throw new ArgumentNullException(nameof(meta));
        if (string.IsNullOrWhiteSpace(serverDir)) throw new ArgumentException("Server directory cannot be empty.", nameof(serverDir));

        if (!meta.CloudSync.Enabled) return;

        var provider = GetRequiredProvider(meta.CloudSync.Provider);
        onStatus?.Invoke("Checking remote server lock...");

        // 1. Inspect remote lock
        var existingLock = await provider.ReadLockAsync(meta.Id, meta.Name, ct);
        string machineName = Environment.MachineName;

        if (existingLock != null && !existingLock.IsExpired() && !existingLock.IsOwnedBy(machineName))
        {
            _logger.LogWarning("Server '{ServerName}' is currently locked by '{User}' on '{Machine}'.",
                meta.Name, existingLock.LockedByUser, existingLock.LockedByMachine);
            throw new ServerLockedException(existingLock);
        }

        // 2. Acquire lock lease
        onStatus?.Invoke("Acquiring cloud server lock...");
        var newLock = new ServerCloudLock
        {
            InstanceId = meta.Id,
            ServerName = meta.Name,
            LockedByMachine = machineName,
            LockedByUser = Environment.UserName,
            LockedAtUtc = DateTimeOffset.UtcNow,
            HeartbeatUtc = DateTimeOffset.UtcNow,
            LeaseDurationSeconds = 60,
            LockToken = Guid.NewGuid().ToString("N")
        };

        bool lockAcquired = await provider.WriteLockAsync(meta.Id, meta.Name, newLock, ct);
        if (!lockAcquired)
        {
            throw new InvalidOperationException($"Failed to acquire cloud lock for '{meta.Name}'. Another host may have locked it simultaneously.");
        }

        _activeLocks[meta.Id] = newLock;
        StartHeartbeat(meta, provider, newLock);

        // 3. Check and pull latest remote changes if available
        if (meta.CloudSync.AutoSyncOnStart)
        {
            onStatus?.Invoke("Checking for remote updates...");
            var remoteManifest = await provider.GetRemoteManifestAsync(meta.Id, meta.Name, ct);

            if (remoteManifest != null && remoteManifest.Version > meta.CloudSync.LastSyncVersion)
            {
                onStatus?.Invoke($"Downloading latest server version (v{remoteManifest.Version})...");
                string tempPackagePath = Path.Combine(Path.GetTempPath(), $"pocketmc-sync-{meta.Id}-{Guid.NewGuid():N}.zip");

                try
                {
                    await provider.DownloadSyncPackageAsync(meta.Id, meta.Name, tempPackagePath, progress, ct);

                    if (File.Exists(tempPackagePath))
                    {
                        onStatus?.Invoke("Applying remote updates to local server...");
                        await SafeZipExtractor.ExtractAsync(tempPackagePath, serverDir);

                        meta.CloudSync.LastSyncVersion = remoteManifest.Version;
                        meta.CloudSync.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                        _logger.LogInformation("Successfully updated server '{ServerName}' to cloud version {Version}.", meta.Name, remoteManifest.Version);
                    }
                }
                finally
                {
                    if (File.Exists(tempPackagePath))
                    {
                        try { File.Delete(tempPackagePath); } catch { /* best effort cleanup */ }
                    }
                }
            }
        }
    }

    public async Task PostStopSyncAsync(
        InstanceMetadata meta,
        string serverDir,
        Action<string>? onStatus = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (meta == null) throw new ArgumentNullException(nameof(meta));
        if (string.IsNullOrWhiteSpace(serverDir)) return;

        if (!meta.CloudSync.Enabled) return;

        StopHeartbeat(meta.Id);

        var provider = GetRequiredProvider(meta.CloudSync.Provider);

        try
        {
            if (meta.CloudSync.AutoSyncOnStop)
            {
                onStatus?.Invoke("Packaging server files for cloud synchronization...");
                long nextVersion = meta.CloudSync.LastSyncVersion + 1;

                string tempPackagePath = Path.Combine(Path.GetTempPath(), $"pocketmc-sync-upload-{meta.Id}-{Guid.NewGuid():N}.zip");

                try
                {
                    var manifest = await BuildSyncPackageAsync(meta, serverDir, tempPackagePath, nextVersion, ct);

                    onStatus?.Invoke("Uploading synchronized server package to cloud...");
                    var uploadResult = await provider.UploadSyncPackageAsync(
                        meta.Id,
                        meta.Name,
                        tempPackagePath,
                        manifest,
                        new Progress<CloudBackupProgress>(p => progress?.Report(p.Percent)),
                        ct);

                    if (uploadResult.Success)
                    {
                        meta.CloudSync.LastSyncVersion = nextVersion;
                        meta.CloudSync.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                        meta.CloudSync.LastSyncSha256 = uploadResult.Sha256;
                        onStatus?.Invoke("Cloud synchronization complete!");
                        _logger.LogInformation("Successfully uploaded server '{ServerName}' sync version {Version}.", meta.Name, nextVersion);
                    }
                    else
                    {
                        onStatus?.Invoke($"Cloud sync upload failed: {uploadResult.ErrorMessage}");
                        _logger.LogWarning("Failed to upload server sync package for '{ServerName}': {Error}", meta.Name, uploadResult.ErrorMessage);
                    }
                }
                finally
                {
                    if (File.Exists(tempPackagePath))
                    {
                        try { File.Delete(tempPackagePath); } catch { /* best effort cleanup */ }
                    }
                }
            }
        }
        finally
        {
            // Release lock lease
            if (_activeLocks.TryRemove(meta.Id, out var activeLock))
            {
                try
                {
                    onStatus?.Invoke("Releasing cloud server lock...");
                    await provider.DeleteLockAsync(meta.Id, meta.Name, activeLock.LockToken, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to release cloud lock for server '{ServerName}'. Lease will expire automatically.", meta.Name);
                }
            }
        }
    }

    public async Task<ServerCloudLock?> CheckLockStatusAsync(InstanceMetadata meta, CancellationToken ct = default)
    {
        if (meta == null || !meta.CloudSync.Enabled) return null;
        var provider = GetRequiredProvider(meta.CloudSync.Provider);
        return await provider.ReadLockAsync(meta.Id, meta.Name, ct);
    }

    public async Task ForceReleaseLockAsync(InstanceMetadata meta, CancellationToken ct = default)
    {
        if (meta == null || !meta.CloudSync.Enabled) return;
        StopHeartbeat(meta.Id);
        _activeLocks.TryRemove(meta.Id, out _);

        var provider = GetRequiredProvider(meta.CloudSync.Provider);
        await provider.DeleteLockAsync(meta.Id, meta.Name, string.Empty, ct);
    }

    public async Task<ServerSyncManifest?> CheckRemoteUpdatesAsync(InstanceMetadata meta, CancellationToken ct = default)
    {
        if (meta == null || !meta.CloudSync.Enabled) return null;
        var provider = GetRequiredProvider(meta.CloudSync.Provider);
        return await provider.GetRemoteManifestAsync(meta.Id, meta.Name, ct);
    }

    private void StartHeartbeat(InstanceMetadata meta, ICloudSyncProvider provider, ServerCloudLock currentLock)
    {
        StopHeartbeat(meta.Id);

        var cts = new CancellationTokenSource();
        _heartbeatCts[meta.Id] = cts;

        _ = Task.Run(async () =>
        {
            using var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(20));
            try
            {
                while (await periodicTimer.WaitForNextTickAsync(cts.Token))
                {
                    currentLock.HeartbeatUtc = DateTimeOffset.UtcNow;
                    await provider.WriteLockAsync(meta.Id, meta.Name, currentLock, cts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error renewing cloud lock heartbeat for server '{ServerName}'.", meta.Name);
            }
        }, cts.Token);
    }

    private void StopHeartbeat(Guid instanceId)
    {
        if (_heartbeatCts.TryRemove(instanceId, out var cts))
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch { }
        }
    }

    internal async Task<ServerSyncManifest> BuildSyncPackageAsync(
        InstanceMetadata meta,
        string serverDir,
        string destinationZipPath,
        long version,
        CancellationToken ct)
    {
        var manifest = new ServerSyncManifest
        {
            InstanceId = meta.Id,
            ServerName = meta.Name,
            Version = version,
            TimestampUtc = DateTimeOffset.UtcNow,
            MachineName = Environment.MachineName,
            IncludedFolders = new List<string>()
        };

        if (meta.CloudSync.SyncWorld) manifest.IncludedFolders.Add("world");
        if (meta.CloudSync.SyncConfig) manifest.IncludedFolders.Add("config");
        if (meta.CloudSync.SyncMods) manifest.IncludedFolders.Add("mods");
        if (meta.CloudSync.SyncPlugins) manifest.IncludedFolders.Add("plugins");

        var filesToPack = new List<(string FullPath, string RelativePath)>();

        // Always include server.properties if present
        string serverPropsPath = Path.Combine(serverDir, "server.properties");
        if (File.Exists(serverPropsPath))
        {
            filesToPack.Add((serverPropsPath, "server.properties"));
        }

        // Scan directory for included folders
        foreach (var dir in Directory.GetDirectories(serverDir))
        {
            var dirName = Path.GetFileName(dir);
            if (ExcludedTopLevelFolders.Contains(dirName)) continue;

            bool isWorld = dirName.StartsWith("world", StringComparison.OrdinalIgnoreCase);
            bool isConfig = string.Equals(dirName, "config", StringComparison.OrdinalIgnoreCase);
            bool isMods = string.Equals(dirName, "mods", StringComparison.OrdinalIgnoreCase);
            bool isPlugins = string.Equals(dirName, "plugins", StringComparison.OrdinalIgnoreCase);

            if ((isWorld && meta.CloudSync.SyncWorld) ||
                (isConfig && meta.CloudSync.SyncConfig) ||
                (isMods && meta.CloudSync.SyncMods) ||
                (isPlugins && meta.CloudSync.SyncPlugins) ||
                (!isWorld && !isConfig && !isMods && !isPlugins))
            {
                foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    string fileName = Path.GetFileName(file);
                    string ext = Path.GetExtension(file);

                    if (string.Equals(fileName, "session.lock", StringComparison.OrdinalIgnoreCase)) continue;
                    if (ExcludedFileExtensions.Contains(ext)) continue;

                    string relPath = Path.GetRelativePath(serverDir, file).Replace('\\', '/');
                    filesToPack.Add((file, relPath));
                }
            }
        }

        // Compute hashes and populate manifest
        long totalBytes = 0;
        using (var sha = SHA256.Create())
        {
            foreach (var (fullPath, relPath) in filesToPack)
            {
                if (!File.Exists(fullPath)) continue;
                var fi = new FileInfo(fullPath);
                totalBytes += fi.Length;

                using var stream = File.OpenRead(fullPath);
                var hashBytes = await sha.ComputeHashAsync(stream, ct);
                manifest.Entries[relPath] = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
        }

        manifest.TotalSizeBytes = totalBytes;

        // Build ZIP
        using (var zipFile = ZipFile.Open(destinationZipPath, ZipArchiveMode.Create))
        {
            foreach (var (fullPath, relPath) in filesToPack)
            {
                ct.ThrowIfCancellationRequested();
                if (File.Exists(fullPath))
                {
                    zipFile.CreateEntryFromFile(fullPath, relPath, CompressionLevel.Fastest);
                }
            }

            // Embed manifest into zip
            var manifestEntry = zipFile.CreateEntry("pocketmc-manifest.json", CompressionLevel.Fastest);
            using var entryStream = manifestEntry.Open();
            await JsonSerializer.SerializeAsync(entryStream, manifest, cancellationToken: ct);
        }

        return manifest;
    }

    public void Dispose()
    {
        foreach (var cts in _heartbeatCts.Values)
        {
            try { cts.Cancel(); cts.Dispose(); } catch { }
        }
        _heartbeatCts.Clear();
        _activeLocks.Clear();
    }
}
