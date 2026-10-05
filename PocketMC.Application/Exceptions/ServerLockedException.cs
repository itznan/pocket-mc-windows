using System;
using PocketMC.Domain.Models;

namespace PocketMC.Application.Exceptions;

/// <summary>
/// Exception thrown when attempting to start or acquire a lock on a server instance
/// that is currently held by another user or machine.
/// </summary>
public class ServerLockedException : Exception
{
    public ServerCloudLock LockInfo { get; }

    public ServerLockedException(ServerCloudLock lockInfo)
        : base($"Server '{lockInfo.ServerName}' is currently in use by '{lockInfo.LockedByUser}' on machine '{lockInfo.LockedByMachine}'.")
    {
        LockInfo = lockInfo;
    }

    public ServerLockedException(ServerCloudLock lockInfo, string message)
        : base(message)
    {
        LockInfo = lockInfo;
    }
}
