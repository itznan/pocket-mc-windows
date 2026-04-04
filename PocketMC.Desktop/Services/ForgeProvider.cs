using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Services
{
    public class ForgeProvider : IServerJarProvider
    {
        private readonly HttpClient _httpClient;
        private readonly DownloaderService _downloader;

        public string DisplayName => "Forge";

        public ForgeProvider(HttpClient httpClient, DownloaderService downloader)
        {
            _httpClient = httpClient;
            _downloader = downloader;
        }

        public async Task<List<MinecraftVersion>> GetAvailableVersionsAsync()
        {
            // Fetch the official Forge versions JSON
            var response = await _httpClient.GetFromJsonAsync<JsonObject>("https://files.minecraftforge.net/maven/net/minecraftforge/forge/json");
            var versions = new List<MinecraftVersion>();
            
            if (response != null && response.TryGetPropertyValue("releases", out var releasesNode) && releasesNode is JsonObject releases)
            {
                // Forge's JSON structure for releases is basically { "1.20.1": [ "47.2.1", ... ], ... }
                foreach (var mcVersion in releases)
                {
                    versions.Add(new MinecraftVersion
                    {
                        Id = mcVersion.Key,
                        Type = "release",
                        ReleaseTime = DateTime.MinValue
                    });
                }
            }
            
            return versions.OrderByDescending(v => v.Id).ToList();
        }

        public async Task DownloadJarAsync(string mcVersion, string destinationPath, IProgress<DownloadProgress>? progress = null)
        {
            // 1. Get the latest recommended/latest Forge version for this MC version
            string forgeVersion = await GetLatestForgeVersionAsync(mcVersion);
            
            // 2. Build the download URL for the installer
            // Official: https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.2.20/forge-1.20.1-47.2.20-installer.jar
            string url = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{mcVersion}-{forgeVersion}/forge-{mcVersion}-{forgeVersion}-installer.jar";
            
            // NOTE: Forge installers need to be RUN to generate the server. 
            // For now, we download the installer. The instance launch logic will need to handle the "installation" step.
            await _downloader.DownloadFileAsync(url, destinationPath, progress);
        }

        private async Task<string> GetLatestForgeVersionAsync(string mcVersion)
        {
            var response = await _httpClient.GetFromJsonAsync<JsonObject>("https://files.minecraftforge.net/maven/net/minecraftforge/forge/json");
            if (response != null && response.TryGetPropertyValue("promotions", out var promosNode) && promosNode is JsonObject promos)
            {
                // Format: "1.20.1-recommended": "47.2.0"
                if (promos.TryGetPropertyValue($"{mcVersion}-recommended", out var rec))
                    return rec?.ToString() ?? "0";
                
                if (promos.TryGetPropertyValue($"{mcVersion}-latest", out var lat))
                    return lat?.ToString() ?? "0";
            }
            
            // Fallback to highest in releases
            if (response != null && response.TryGetPropertyValue("releases", out var releasesNode) && releasesNode is JsonObject releases)
            {
                if (releases.TryGetPropertyValue(mcVersion, out var rels) && rels is JsonArray relList)
                {
                    return relList.Last()?.ToString() ?? "0";
                }
            }

            return "latest";
        }
    }
}
