using System.Net;
using PocketMC.Desktop.Features.Networking;
using PocketMC.Desktop.Features.Tunnel;

namespace PocketMC.Desktop.Tests;

public sealed class TunnelServiceTests
{
    [Fact]
    public async Task ResolveTunnelAsync_WhenAgentIsOffline_ReturnsPlayitAgentOffline()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient();
        PlayitAgentHarness harness = workspace.CreatePlayitAgentHarness();
        TunnelService service = workspace.CreateTunnelService(apiClient, harness.Service);

        TunnelResolutionResult result = await service.ResolveTunnelAsync(new PortCheckRequest(
            25565,
            PortProtocol.Tcp,
            PortIpMode.IPv4,
            displayName: "Java server"));
        PortCheckResult? portResult = result.ToPortCheckResult(new PortCheckRequest(25565));

        Assert.Equal(TunnelResolutionResult.TunnelStatus.AgentOffline, result.Status);
        Assert.Equal(PortFailureCode.PlayitAgentOffline, result.FailureCode);
        Assert.NotNull(portResult);
        Assert.Equal(PortFailureCode.PlayitAgentOffline, portResult.FailureCode);
    }

    [Fact]
    public async Task ResolveTunnelAsync_WhenTokenIsInvalid_ReturnsStructuredPlayitFailure()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        workspace.WritePlayitSecret();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized));
        PlayitAgentHarness harness = workspace.CreatePlayitAgentHarness();
        harness.StateMachine.TransitionTo(PlayitAgentState.Connected);
        TunnelService service = workspace.CreateTunnelService(apiClient, harness.Service);

        TunnelResolutionResult result = await service.ResolveTunnelAsync(new PortCheckRequest(
            25565,
            PortProtocol.Tcp,
            PortIpMode.IPv4,
            displayName: "Java server"));

        Assert.Equal(TunnelResolutionResult.TunnelStatus.Error, result.Status);
        Assert.True(result.IsTokenInvalid);
        Assert.Equal(PortFailureCode.PlayitTokenInvalid, result.FailureCode);
    }

    [Fact]
    public async Task ResolveTunnelAsync_WhenNoSecretIsAvailable_ReturnsClaimRequired()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient();
        PlayitAgentHarness harness = workspace.CreatePlayitAgentHarness();
        harness.StateMachine.TransitionTo(PlayitAgentState.Connected);
        TunnelService service = workspace.CreateTunnelService(apiClient, harness.Service);

        TunnelResolutionResult result = await service.ResolveTunnelAsync(new PortCheckRequest(
            19132,
            PortProtocol.Udp,
            PortIpMode.IPv4,
            displayName: "Bedrock server"));

        Assert.Equal(TunnelResolutionResult.TunnelStatus.Error, result.Status);
        Assert.True(result.RequiresClaim);
        Assert.Equal(PortFailureCode.PlayitClaimRequired, result.FailureCode);
    }

    [Fact]
    public async Task ResolveTunnelAsync_WhenTunnelLimitIsReached_ReturnsTunnelLimitFailure()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        workspace.WritePlayitSecret();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "status": "success",
                      "data": {
                        "tunnels": [
                          { "id": "1", "name": "a", "tunnel_type": "minecraft-java", "alloc": { "status": "ok", "data": { "ip_hostname": "a.example.com", "port_start": 1 } }, "origin": { "type": "tcp", "data": { "local_port": 25565 } } },
                          { "id": "2", "name": "b", "tunnel_type": "minecraft-bedrock", "alloc": { "status": "ok", "data": { "ip_hostname": "b.example.com", "port_start": 2 } }, "origin": { "type": "udp", "data": { "local_port": 19132 } } },
                          { "id": "3", "name": "c", "tunnel_type": "tcp", "alloc": { "status": "ok", "data": { "ip_hostname": "c.example.com", "port_start": 3 } }, "origin": { "type": "tcp", "data": { "local_port": 25566 } } },
                          { "id": "4", "name": "d", "tunnel_type": "udp", "alloc": { "status": "ok", "data": { "ip_hostname": "d.example.com", "port_start": 4 } }, "origin": { "type": "udp", "data": { "local_port": 19133 } } }
                        ]
                      }
                    }
                    """)
            });
        PlayitAgentHarness harness = workspace.CreatePlayitAgentHarness();
        harness.StateMachine.TransitionTo(PlayitAgentState.Connected);
        TunnelService service = workspace.CreateTunnelService(apiClient, harness.Service);

        TunnelResolutionResult result = await service.ResolveTunnelAsync(new PortCheckRequest(
            25570,
            PortProtocol.Tcp,
            PortIpMode.IPv4,
            displayName: "Java server"));

        Assert.Equal(TunnelResolutionResult.TunnelStatus.LimitReached, result.Status);
        Assert.Equal(PortFailureCode.TunnelLimitReached, result.FailureCode);
        Assert.Equal(4, result.ExistingTunnels.Count);
    }

    [Fact]
    public async Task PollForNewTunnelResultAsync_WhenItTimesOut_ReturnsPublicReachabilityFailure()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient();
        PlayitAgentHarness harness = workspace.CreatePlayitAgentHarness();
        TunnelService service = workspace.CreateTunnelService(apiClient, harness.Service);

        TunnelResolutionResult result = await service.PollForNewTunnelResultAsync(
            new PortCheckRequest(19140, PortProtocol.Udp, PortIpMode.IPv4, displayName: "Geyser Bedrock"),
            CancellationToken.None,
            timeout: TimeSpan.Zero);

        Assert.Equal(TunnelResolutionResult.TunnelStatus.Error, result.Status);
        Assert.Equal(PortFailureCode.PublicReachabilityFailure, result.FailureCode);
    }

    [Fact]
    public void FindTunnelForRequest_UsesProtocolAwareMatching()
    {
        var request = new PortCheckRequest(19132, PortProtocol.Udp, PortIpMode.IPv4, displayName: "Bedrock server");
        var tunnels = new List<TunnelData>
        {
            new() { Id = "java", Port = 19132, PublicAddress = "java.example.com", Protocol = PortProtocol.Tcp },
            new() { Id = "bedrock", Port = 19132, PublicAddress = "bedrock.example.com", Protocol = PortProtocol.Udp }
        };

        TunnelData? match = PlayitApiClient.FindTunnelForRequest(tunnels, request);

        Assert.NotNull(match);
        Assert.Equal("bedrock", match.Id);
    }
}
