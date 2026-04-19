using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PocketMC.Desktop.Features.Marketplace
{
    public class ModrinthSearchResult
    {
        [JsonPropertyName("hits")]
        public List<ModrinthHit> Hits { get; set; } = new();
    }

    public class ModrinthHit
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("downloads")]
        public int Downloads { get; set; }

        [JsonPropertyName("icon_url")]
        public string? IconUrl { get; set; }

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = "";
    }

    public class ModrinthVersion
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("files")]
        public List<ModrinthFile> Files { get; set; } = new();

        [JsonPropertyName("loaders")]
        public List<string> Loaders { get; set; } = new();
    }

    public class ModrinthFile
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("filename")]
        public string FileName { get; set; } = "";

        [JsonPropertyName("primary")]
        public bool IsPrimary { get; set; }
    }

    public class ModrinthService
    {
        private readonly HttpClient _httpClient;

        public ModrinthService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<List<ModrinthHit>> SearchAsync(string type, string mcVersion, string loader, string sort = "relevance", string query = "", int offset = 0)
        {
            try
            {
                // type is expected as "project_type:plugin" or "project_type:mod" or "project_type:modpack"
                string facetsStr = $"[\"{type}\"]";
                if (!string.IsNullOrEmpty(mcVersion) && mcVersion != "*")
                {
                    facetsStr += $",[\"versions:{mcVersion}\"]";
                }
                if (type == "project_type:mod" && !string.IsNullOrWhiteSpace(loader))
                {
                    facetsStr += $",[\"categories:{loader}\"]";
                }
                string facets = $"[{facetsStr}]";

                string url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&facets={Uri.EscapeDataString(facets)}&limit=20&offset={offset}&index={sort}";

                var result = await _httpClient.GetFromJsonAsync<ModrinthSearchResult>(url);
                return result?.Hits ?? new();
            }
            catch
            {
                return new();
            }
        }

        public async Task<ModrinthVersion?> GetLatestVersionAsync(string slug, string mcVersion, string? loader = null)
        {
            try
            {
                string baseUrl = $"https://api.modrinth.com/v2/project/{slug}/version";
                var queryParams = new List<string>();

                if (!string.IsNullOrEmpty(mcVersion) && mcVersion != "*")
                    queryParams.Add($"game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { mcVersion }))}");

                if (!string.IsNullOrEmpty(loader))
                    queryParams.Add($"loaders={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader }))}");

                string url = queryParams.Count > 0 ? $"{baseUrl}?{string.Join("&", queryParams)}" : baseUrl;
                var versions = await _httpClient.GetFromJsonAsync<List<ModrinthVersion>>(url);

                if (versions?.Count > 0)
                    return versions[0];

                if (!string.IsNullOrWhiteSpace(loader))
                {
                    // Fallback: some projects have inconsistent loader metadata in indexed filters.
                    string relaxedUrl = !string.IsNullOrEmpty(mcVersion) && mcVersion != "*"
                        ? $"{baseUrl}?game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { mcVersion }))}"
                        : baseUrl;

                    var relaxedVersions = await _httpClient.GetFromJsonAsync<List<ModrinthVersion>>(relaxedUrl) ?? new();
                    var loaderMatch = relaxedVersions.Find(v => v.Loaders.Any(l => l.Equals(loader, StringComparison.OrdinalIgnoreCase)));
                    if (loaderMatch != null) return loaderMatch;
                    return relaxedVersions.Count > 0 ? relaxedVersions[0] : null;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
