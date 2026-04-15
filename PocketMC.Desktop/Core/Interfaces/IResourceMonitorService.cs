using System;
using System.Collections.Concurrent;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Core.Interfaces
{
    public interface IResourceMonitorService
    {
        ConcurrentDictionary<Guid, InstanceMetrics> Metrics { get; }
        GlobalResourceSummary? CurrentSummary { get; }
        event EventHandler<InstanceMetricsUpdatedEventArgs>? InstanceMetricsUpdated;
        event EventHandler? GlobalMetricsUpdated;
        
        double GetTotalCommittedRamMb();
    }

    public class InstanceMetricsUpdatedEventArgs : EventArgs
    {
        public Guid InstanceId { get; }
        public InstanceMetrics Metrics { get; }

        public InstanceMetricsUpdatedEventArgs(Guid instanceId, InstanceMetrics metrics)
        {
            InstanceId = instanceId;
            Metrics = metrics;
        }
    }
}
