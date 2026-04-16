using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

namespace PocketMC.Desktop.Features.Marketplace
{
    public class PoggitService
    {
        private readonly HttpClient _http;

        public PoggitService(IHttpClientFactory httpClientFactory)
        {
            _http = httpClientFactory.CreateClient("PocketMC.Poggit");
        }

        public async Task<List<ModrinthHit>> SearchAsync(string query, int offset)
        {
            // Poggit doesn't really have pagination exactly like modrinth or curseforge in the simple API
            string url = "https://poggit.pmmp.io/releases.json";
            if (!string.IsNullOrWhiteSpace(query))
            {
                url += $"?name=*{Uri.EscapeDataString(query)}*";
            }

            var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var items = JsonSerializer.Deserialize<JsonElement>(content);

            var hits = new List<ModrinthHit>();

            if (items.ValueKind == JsonValueKind.Array)
            {
                int skip = offset;
                int taken = 0;
                foreach (var item in items.EnumerateArray())
                {
                    if (skip > 0)
                    {
                        skip--;
                        continue;
                    }

                    if (taken >= 20) break;

                    string name = item.GetProperty("name").GetString() ?? "Unknown";
                    string slug = item.GetProperty("name").GetString() ?? ""; // Poggit uses name as slug effectively
                    string desc = item.TryGetProperty("tagline", out var t) ? t.GetString() ?? "" : "";
                    string icon = item.TryGetProperty("icon_url", out var i) && i.ValueKind != JsonValueKind.Null ? i.GetString() ?? "" : "";
                    string author = item.GetProperty("repo_name").GetString()?.Split('/')[0] ?? "Unknown";
                    int downloads = item.TryGetProperty("downloads", out var d) ? d.GetInt32() : 0;

                    hits.Add(new ModrinthHit
                    {
                        Slug = slug,
                        Title = name,
                        Description = desc,
                        IconUrl = icon,
                        Downloads = downloads
                    });
                    taken++;
                }
            }

            return hits;
        }

        public async Task<ModrinthVersion?> GetLatestVersionAsync(string name)
        {
            string url = $"https://poggit.pmmp.io/releases.json?name={Uri.EscapeDataString(name)}";
            var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var items = JsonSerializer.Deserialize<JsonElement>(content);

            if (items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
            {
                var latest = items[0]; // Poggit usually sorts by newest release or we grab the first match
                string vUrl = latest.TryGetProperty("artifact_url", out var a) ? a.GetString() ?? "" : "";
                string versionNum = latest.TryGetProperty("version", out var v) ? v.GetString() ?? "1.0.0" : "1.0.0";

                if (!string.IsNullOrEmpty(vUrl))
                {
                    return new ModrinthVersion
                    {
                        Id = versionNum,
                        Name = versionNum,
                        Files = new List<ModrinthFile>
                        {
                            new ModrinthFile
                            {
                                Url = vUrl,
                                FileName = $"{name}.phar",
                                IsPrimary = true
                            }
                        }
                    };
                }
            }

            return null;
        }
    }
}
