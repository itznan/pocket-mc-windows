using System.IO;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Shell;

public class ApplicationState
{
    public AppSettings Settings { get; private set; } = new();

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Settings.AppRootPath);

    // ── Tunnel address cache (set once at server start, cleared on stop) ──
    private readonly Dictionary<Guid, string> _tunnelAddresses = new();
    private readonly Dictionary<Guid, string> _numericTunnelAddresses = new();
    private readonly Dictionary<Guid, string> _bedrockTunnelAddresses = new();
    private readonly Dictionary<Guid, string> _bedrockNumericTunnelAddresses = new();
    private readonly object _tunnelLock = new();

    public void SetTunnelAddress(Guid instanceId, string address)
    {
        lock (_tunnelLock) { _tunnelAddresses[instanceId] = address; }
    }

    public string? GetTunnelAddress(Guid instanceId)
    {
        lock (_tunnelLock) { return _tunnelAddresses.TryGetValue(instanceId, out var a) ? a : null; }
    }

    public void SetNumericTunnelAddress(Guid instanceId, string address)
    {
        lock (_tunnelLock) { _numericTunnelAddresses[instanceId] = address; }
    }

    public string? GetNumericTunnelAddress(Guid instanceId)
    {
        lock (_tunnelLock) { return _numericTunnelAddresses.TryGetValue(instanceId, out var a) ? a : null; }
    }

    public void SetBedrockTunnelAddress(Guid instanceId, string address)
    {
        lock (_tunnelLock) { _bedrockTunnelAddresses[instanceId] = address; }
    }

    public string? GetBedrockTunnelAddress(Guid instanceId)
    {
        lock (_tunnelLock) { return _bedrockTunnelAddresses.TryGetValue(instanceId, out var a) ? a : null; }
    }

    public void SetBedrockNumericTunnelAddress(Guid instanceId, string address)
    {
        lock (_tunnelLock) { _bedrockNumericTunnelAddresses[instanceId] = address; }
    }

    public string? GetBedrockNumericTunnelAddress(Guid instanceId)
    {
        lock (_tunnelLock) { return _bedrockNumericTunnelAddresses.TryGetValue(instanceId, out var a) ? a : null; }
    }

    public void ClearTunnelAddress(Guid instanceId)
    {
        lock (_tunnelLock)
        {
            _tunnelAddresses.Remove(instanceId);
            _numericTunnelAddresses.Remove(instanceId);
            _bedrockTunnelAddresses.Remove(instanceId);
            _bedrockNumericTunnelAddresses.Remove(instanceId);
        }
    }
    public void ApplySettings(AppSettings settings)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public string GetRequiredAppRootPath()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("PocketMC has not been configured with an application root path yet.");
        }

        return Settings.AppRootPath!;
    }

    public string GetServersDirectory() => Path.Combine(GetRequiredAppRootPath(), "servers");

    public string GetRuntimeDirectory() => Path.Combine(GetRequiredAppRootPath(), "runtime");

    public string GetPlayitExecutablePath() => Path.Combine(GetRequiredAppRootPath(), "tunnel", "playit.exe");
}
