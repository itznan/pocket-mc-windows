using System;

namespace PocketMC.Domain.Models;

/// <summary>
/// Represents a distributed cloud lock/lease descriptor for a shared Minecraft server instance.
/// Stored remotely as pocketmc-lock.json in the instance's cloud folder.
/// </summary>
public class ServerCloudLock
{
    public Guid InstanceId { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public string LockedByMachine { get; set; } = string.Empty;
    public string LockedByUser { get; set; } = string.Empty;
    public DateTimeOffset LockedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset HeartbeatUtc { get; set; } = DateTimeOffset.UtcNow;
    public int LeaseDurationSeconds { get; set; } = 60;
    public string LockToken { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Checks whether this lease has expired based on the heartbeat timestamp and lease duration.
    /// </summary>
    public bool IsExpired(DateTimeOffset? nowUtc = null)
    {
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        return now > HeartbeatUtc.AddSeconds(LeaseDurationSeconds);
    }

    /// <summary>
    /// Checks whether this lock belongs to the specified machine and lock token.
    /// </summary>
    public bool IsOwnedBy(string machineName, string? lockToken = null)
    {
        if (!string.Equals(LockedByMachine, machineName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (lockToken != null && !string.Equals(LockToken, lockToken, StringComparison.Ordinal))
            return false;

        return true;
    }
}
