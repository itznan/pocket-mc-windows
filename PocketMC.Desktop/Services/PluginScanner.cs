using System;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace PocketMC.Desktop.Services
{
    /// <summary>
    /// Reads plugin.yml from inside a plugin JAR (which is a ZIP) to extract
    /// metadata for compatibility checking against the server version.
    /// </summary>
    public static class PluginScanner
    {
        /// <summary>
        /// Attempts to read the api-version from plugin.yml inside a JAR file.
        /// Returns null if not found or not a valid plugin JAR.
        /// </summary>
        public static string? TryGetApiVersion(string jarPath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(jarPath);
                var entry = archive.GetEntry("plugin.yml");
                if (entry == null) return null;

                using var reader = new StreamReader(entry.Open());
                string yaml = reader.ReadToEnd();

                var match = Regex.Match(yaml, @"api-version:\s*['""]?([^\s'""]+)['""]?");
                return match.Success ? match.Groups[1].Value : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Attempts to read the plugin name from plugin.yml.
        /// </summary>
        public static string? TryGetPluginName(string jarPath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(jarPath);
                var entry = archive.GetEntry("plugin.yml");
                if (entry == null) return null;

                using var reader = new StreamReader(entry.Open());
                string yaml = reader.ReadToEnd();

                var match = Regex.Match(yaml, @"^name:\s*['""]?([^\s'""]+)['""]?", RegexOptions.Multiline);
                return match.Success ? match.Groups[1].Value : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Determines if a plugin's api-version is incompatible with the server's Minecraft version.
        /// 
        /// Spigot/Paper maintains BACKWARD COMPATIBILITY: a plugin built for api-version 1.14
        /// works on 1.20.4. A mismatch only occurs when the plugin requires a NEWER api-version
        /// than the server provides (e.g., plugin needs 1.21 but server is 1.20.4).
        /// </summary>
        /// <returns>true if incompatible (plugin too new for server), false if compatible or unknown</returns>
        public static bool IsIncompatible(string? pluginApiVersion, string? serverMinecraftVersion)
        {
            if (string.IsNullOrEmpty(pluginApiVersion) || string.IsNullOrEmpty(serverMinecraftVersion))
                return false; // Can't determine — assume compatible

            try
            {
                var pluginVer = ParseMajorMinor(pluginApiVersion);
                var serverVer = ParseMajorMinor(serverMinecraftVersion);

                if (pluginVer == null || serverVer == null)
                    return false;

                // Plugin requires a NEWER API than the server provides
                // e.g. plugin api-version 1.21 > server 1.20 → incompatible
                // e.g. plugin api-version 1.14 <= server 1.20 → compatible (backward compat)
                return pluginVer.Value.major > serverVer.Value.major ||
                       (pluginVer.Value.major == serverVer.Value.major && pluginVer.Value.minor > serverVer.Value.minor);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Parses "1.20.4" or "1.20" into (major=1, minor=20).
        /// </summary>
        private static (int major, int minor)? ParseMajorMinor(string version)
        {
            // Strip quotes, whitespace
            version = version.Trim().Trim('\'', '"');
            
            var parts = version.Split('.');
            if (parts.Length < 2) return null;

            if (int.TryParse(parts[0], out int major) && int.TryParse(parts[1], out int minor))
                return (major, minor);

            return null;
        }
    }
}
