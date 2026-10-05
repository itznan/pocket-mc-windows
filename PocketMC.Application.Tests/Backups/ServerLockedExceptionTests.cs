using System;
using PocketMC.Application.Exceptions;
using PocketMC.Domain.Models;
using Xunit;

namespace PocketMC.Application.Tests.Backups;

public sealed class ServerLockedExceptionTests
{
    [Fact]
    public void Exception_ContainsLockDetailsInMessage()
    {
        var cloudLock = new ServerCloudLock
        {
            ServerName = "Survival World",
            LockedByUser = "Alice",
            LockedByMachine = "ALICE-DESKTOP"
        };

        var ex = new ServerLockedException(cloudLock);

        Assert.Same(cloudLock, ex.LockInfo);
        Assert.Contains("Survival World", ex.Message);
        Assert.Contains("Alice", ex.Message);
        Assert.Contains("ALICE-DESKTOP", ex.Message);
    }
}
