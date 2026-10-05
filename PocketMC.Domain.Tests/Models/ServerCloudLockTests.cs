using System;
using System.Collections.Generic;
using PocketMC.Domain.Models;
using Xunit;

namespace PocketMC.Domain.Tests.Models;

public sealed class ServerCloudLockTests
{
    [Fact]
    public void IsExpired_ReturnsFalse_WhenWithinLeaseDuration()
    {
        var now = DateTimeOffset.UtcNow;
        var cloudLock = new ServerCloudLock
        {
            HeartbeatUtc = now,
            LeaseDurationSeconds = 60
        };

        var checkTime = now.AddSeconds(30);
        Assert.False(cloudLock.IsExpired(checkTime));
    }

    [Fact]
    public void IsExpired_ReturnsTrue_WhenLeaseDurationExceeded()
    {
        var now = DateTimeOffset.UtcNow;
        var cloudLock = new ServerCloudLock
        {
            HeartbeatUtc = now,
            LeaseDurationSeconds = 60
        };

        var checkTime = now.AddSeconds(61);
        Assert.True(cloudLock.IsExpired(checkTime));
    }

    [Fact]
    public void IsOwnedBy_MatchesMachineAndToken()
    {
        var token = Guid.NewGuid().ToString("N");
        var cloudLock = new ServerCloudLock
        {
            LockedByMachine = "PC-ALPHA",
            LockToken = token
        };

        Assert.True(cloudLock.IsOwnedBy("pc-alpha", token));
        Assert.False(cloudLock.IsOwnedBy("pc-beta", token));
        Assert.False(cloudLock.IsOwnedBy("pc-alpha", "wrong-token"));
    }

    [Fact]
    public void ServerSyncManifest_FindConflictingFiles_DetectsModifications()
    {
        var manifestA = new ServerSyncManifest
        {
            Entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["world/level.dat"] = "hash1",
                ["server.properties"] = "hash2"
            }
        };

        var manifestB = new ServerSyncManifest
        {
            Entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["world/level.dat"] = "hash1",
                ["server.properties"] = "hash2_modified"
            }
        };

        var conflicts = manifestA.FindConflictingFiles(manifestB);
        Assert.Single(conflicts);
        Assert.Contains("server.properties", conflicts);
    }
}
