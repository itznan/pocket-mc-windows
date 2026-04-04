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
    public class FabricProvider : IServerJarProvider
    {
        private readonly HttpClient _httpClient;
        private readonly DownloaderService _downloader;

        public string DisplayName => "Fabric";

        public FabricProvider(HttpClient httpClient, DownloaderService downloader)
        {
            _httpClient = httpClient;
            _downloader = downloader;
        }

        public async Task<List<MinecraftVersion>> GetAvailableVersionsAsync()
        {
            // For general selection, we probably want to list Minecraft versions that Fabric supports.
            // But Fabric usually attaches to a specific Minecraft version.
            // To simplify, let's just return the list of Minecraft versions from Mojang manifest for now,
            // as Fabric supports almost all of them.
            // Alternatively, fetch from Fabric meta: https://meta.fabricmc.net/v2/versions/game
            
            var response = await _httpClient.GetFromJsonAsync<JsonArray>("https://meta.fabricmc.net/v2/versions/game");
            var versions = new List<MinecraftVersion>();
            if (response != null)
            {
                foreach (var node in response)
                {
                    if (node == null) continue;
                    versions.Add(new MinecraftVersion
                    {
                        Id = node["version"]?.ToString() ?? "",
                        Type = (bool)(node["stable"] ?? false) ? "release" : "snapshot",
                        ReleaseTime = DateTime.MinValue
                    });
                }
            }
            return versions;
        }

        public async Task DownloadJarAsync(string mcVersion, string destinationPath, IProgress<DownloadProgress>? progress = null)
        {
            // Get latest loader and installer versions
            string loaderVersion = await GetLatestLoaderVersionAsync();
            string installerVersion = await GetLatestInstallerVersionAsync();
            await DownloadFabricJarAsync(mcVersion, loaderVersion, installerVersion, destinationPath, progress);
        }

        public async Task DownloadFabricJarAsync(string mcVersion, string loaderVersion, string destinationPath, IProgress<DownloadProgress>? progress = null)
        {
            string installerVersion = await GetLatestInstallerVersionAsync();
            await DownloadFabricJarAsync(mcVersion, loaderVersion, installerVersion, destinationPath, progress);
        }

        public async Task DownloadFabricJarAsync(string mcVersion, string loaderVersion, string installerVersion, string destinationPath, IProgress<DownloadProgress>? progress = null)
        {
            // Official structure: v2/versions/loader/:game_version/:loader_version/:installer_version/server/jar
            string url = $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}/{loaderVersion}/{installerVersion}/server/jar";
            await _downloader.DownloadFileAsync(url, destinationPath, progress);
        }

        private async Task<string> GetLatestLoaderVersionAsync()
        {
            var loaders = await _httpClient.GetFromJsonAsync<JsonArray>("https://meta.fabricmc.net/v2/versions/loader");
            var latest = loaders?.FirstOrDefault(l => (bool)(l?["stable"] ?? false));
            return latest?["version"]?.ToString() ?? "0.15.7"; 
        }

        private async Task<string> GetLatestInstallerVersionAsync()
        {
            var installers = await _httpClient.GetFromJsonAsync<JsonArray>("https://meta.fabricmc.net/v2/versions/installer");
            var latest = installers?.FirstOrDefault(l => (bool)(l?["stable"] ?? false));
            return latest?["version"]?.ToString() ?? "1.0.1";
        }
    }
}
