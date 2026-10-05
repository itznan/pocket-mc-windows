using System;
using System.Collections.Generic;

namespace PocketMC.Domain.Models;

/// <summary>
/// Manifest recording the synchronization version, metadata, and per-file SHA-256 hashes
/// for a synchronized PocketMC server instance.
/// </summary>
public class ServerSyncManifest
{
    public int ManifestVersion { get; set; } = 1;
    public Guid InstanceId { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public long Version { get; set; } = 1;
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public string MachineName { get; set; } = Environment.MachineName;
    public long TotalSizeBytes { get; set; }
    public List<string> IncludedFolders { get; set; } = new() { "world", "config", "mods", "plugins" };
    public Dictionary<string, string> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Computes file differences between this manifest and a target remote manifest.
    /// </summary>
    public List<string> FindConflictingFiles(ServerSyncManifest other)
    {
        var conflicts = new List<string>();
        if (other?.Entries == null) return conflicts;

        foreach (var (path, hash) in Entries)
        {
            if (other.Entries.TryGetValue(path, out var otherHash) &&
                !string.Equals(hash, otherHash, StringComparison.OrdinalIgnoreCase))
            {
                conflicts.Add(path);
            }
        }

        return conflicts;
    }
}
