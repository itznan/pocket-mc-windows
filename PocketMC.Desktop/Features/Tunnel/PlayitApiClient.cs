using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Networking;

namespace PocketMC.Desktop.Features.Tunnel
{
    // --- Playit API Response Models ---
    public class PlayitApiResponse { [JsonPropertyName("status")] public string Status { get; set; } = string.Empty; [JsonPropertyName("data")] public PlayitApiData? Data { get; set; } }
    public class PlayitApiData { [JsonPropertyName("tunnels")] public List<PlayitTunnelConfig> Tunnels { get; set; } = new(); }
    public class PlayitTunnelConfig { [JsonPropertyName("id")] public string Id { get; set; } = string.Empty; [JsonPropertyName("name")] public string? Name { get; set; } [JsonPropertyName("tunnel_type")] public string? TunnelType { get; set; } [JsonPropertyName("alloc")] public PlayitAllocWrapper? Alloc { get; set; } [JsonPropertyName("origin")] public PlayitOriginWrapper? Origin { get; set; } }
    public class PlayitAllocWrapper { [JsonPropertyName("status")] public string Status { get; set; } = string.Empty; [JsonPropertyName("data")] public PlayitAllocData? Data { get; set; } }
    public class PlayitAllocData { [JsonPropertyName("ip_hostname")] public string IpHostname { get; set; } = string.Empty; [JsonPropertyName("port_start")] public int PortStart { get; set; } [JsonPropertyName("assigned_srv")] public string? AssignedSrv { get; set; } }
    public class PlayitOriginWrapper { [JsonPropertyName("type")] public string Type { get; set; } = string.Empty; [JsonPropertyName("data")] public PlayitOriginData? Data { get; set; } }
    public class PlayitOriginData { [JsonPropertyName("local_port")] public int LocalPort { get; set; } }

    // --- Custom UI Models ---
    public class TunnelData { public string Id { get; set; } = string.Empty; public string? Name { get; set; } public int Port { get; set; } public string PublicAddress { get; set; } = string.Empty; public string? TunnelType { get; set; } public PortProtocol? Protocol { get; set; } }
    public class TunnelListResult { public bool Success { get; set; } public List<TunnelData> Tunnels { get; set; } = new(); public string? ErrorMessage { get; set; } public bool IsTokenInvalid { get; set; } public bool RequiresClaim { get; set; } }

    /// <summary>
    /// HTTP client for the Playit.gg tunnel API.
    /// Reads the agent secret from %APPDATA%/playit/playit.toml.
    /// </summary>
    public class PlayitApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly ApplicationState _applicationState;
        private readonly SettingsManager _settingsManager;
        private readonly ILogger<PlayitApiClient> _logger;
        private static readonly Regex SecretRegex = new(@"secret_key\s*=\s*""([^""]+)""", RegexOptions.Compiled);
        private const string TunnelApiUrl = "https://api.playit.gg/tunnels/list";

        public PlayitApiClient(ApplicationState applicationState, SettingsManager settingsManager, ILogger<PlayitApiClient> logger, HttpClient? httpClient = null)
        {
            _applicationState = applicationState;
            _settingsManager = settingsManager;
            _logger = logger;
            _httpClient = httpClient ?? new HttpClient();
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent")) _httpClient.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        }

        public string? GetSecretKey()
        {
            var tomlPath = _settingsManager.GetPlayitTomlPath(_applicationState.Settings);
            if (!File.Exists(tomlPath)) return null;
            try { string content = File.ReadAllText(tomlPath); var match = SecretRegex.Match(content); return match.Success ? match.Groups[1].Value : null; }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to read playit secret."); return null; }
        }

        public async Task<TunnelListResult> GetTunnelsAsync()
        {
            string? secretKey = GetSecretKey();
            if (string.IsNullOrEmpty(secretKey)) return new TunnelListResult { Success = false, ErrorMessage = "No Playit agent secret available.", RequiresClaim = true };

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, TunnelApiUrl);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Agent-Key", secretKey);
                request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                using var response = await _httpClient.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    return new TunnelListResult { Success = false, ErrorMessage = "Token invalid or revoked.", IsTokenInvalid = true };

                response.EnsureSuccessStatusCode();
                var apiResponse = JsonSerializer.Deserialize<PlayitApiResponse>(await response.Content.ReadAsStringAsync());
                var normalizedTunnels = new List<TunnelData>();

                if (apiResponse?.Data?.Tunnels != null)
                {
                    foreach (var pt in apiResponse.Data.Tunnels)
                    {
                        if (pt.Alloc?.Data == null || pt.Origin?.Data == null) continue;
                        string publicAddress = !string.IsNullOrEmpty(pt.Alloc.Data.AssignedSrv) ? pt.Alloc.Data.AssignedSrv : $"{pt.Alloc.Data.IpHostname}:{pt.Alloc.Data.PortStart}";
                        normalizedTunnels.Add(new TunnelData { Id = pt.Id, Name = pt.Name, Port = pt.Origin.Data.LocalPort, PublicAddress = publicAddress, TunnelType = pt.TunnelType, Protocol = InferProtocol(pt.TunnelType) });
                    }
                }
                return new TunnelListResult { Success = true, Tunnels = normalizedTunnels };
            }
            catch (Exception ex) { return new TunnelListResult { Success = false, ErrorMessage = ex.Message }; }
        }

        /// <summary>
        /// Finds the best matching Playit tunnel for a structured local port request.
        /// </summary>
        public static TunnelData? FindTunnelForRequest(IEnumerable<TunnelData> tunnels, PortCheckRequest request)
        {
            return tunnels.FirstOrDefault(t =>
                t.Port == request.Port &&
                (!t.Protocol.HasValue || ProtocolsOverlap(t.Protocol.Value, request.Protocol)));
        }

        private static PortProtocol? InferProtocol(string? tunnelType)
        {
            if (string.IsNullOrWhiteSpace(tunnelType))
            {
                return null;
            }

            if (tunnelType.Contains("bedrock", StringComparison.OrdinalIgnoreCase) ||
                tunnelType.Contains("udp", StringComparison.OrdinalIgnoreCase))
            {
                return PortProtocol.Udp;
            }

            if (tunnelType.Contains("java", StringComparison.OrdinalIgnoreCase) ||
                tunnelType.Contains("tcp", StringComparison.OrdinalIgnoreCase))
            {
                return PortProtocol.Tcp;
            }

            return null;
        }

        private static bool ProtocolsOverlap(PortProtocol left, PortProtocol right)
        {
            return left == PortProtocol.TcpAndUdp ||
                   right == PortProtocol.TcpAndUdp ||
                   left == right;
        }
    }
}
