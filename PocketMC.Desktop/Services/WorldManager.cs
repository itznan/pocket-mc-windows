using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using PocketMC.Desktop.Utils;

namespace PocketMC.Desktop.Services
{
    /// <summary>
    /// Handles intelligent world ZIP extraction by hunting for level.dat
    /// to find the true world root, regardless of how the ZIP was packaged.
    /// </summary>
    public class WorldManager
    {
        /// <summary>
        /// Extracts a world ZIP to the target path, intelligently finding the real world root.
        /// </summary>
        public async Task ImportWorldZipAsync(string zipPath, string targetWorldPath, Action<string>? onProgress = null)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "PocketMC", $"Extraction-{Guid.NewGuid()}");

            try
            {
                onProgress?.Invoke("Extracting ZIP...");
                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, tempDir));

                onProgress?.Invoke("Scanning for level.dat...");
                string? worldRoot = await Task.Run(() => FindWorldRoot(tempDir));

                if (worldRoot == null)
                {
                    throw new InvalidOperationException(
                        "Could not find level.dat in the ZIP. This doesn't appear to be a valid Minecraft world.");
                }

                // Clean existing world directory
                if (Directory.Exists(targetWorldPath))
                {
                    onProgress?.Invoke("Removing existing world...");
                    await FileUtils.CleanDirectoryAsync(targetWorldPath);
                }

                // Copy the true world root to the target
                onProgress?.Invoke("Installing world...");
                await FileUtils.CopyDirectoryAsync(worldRoot, targetWorldPath);

                onProgress?.Invoke("World imported successfully!");
            }
            finally
            {
                // Always clean up temp directory
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); }
                    catch { /* best effort cleanup */ }
                }
            }
        }

        /// <summary>
        /// Recursively searches for level.dat and returns its parent directory (the true world root).
        /// </summary>
        private string? FindWorldRoot(string searchDir)
        {
            // Check current directory
            if (File.Exists(Path.Combine(searchDir, "level.dat")))
                return searchDir;

            // Search subdirectories
            foreach (var subDir in Directory.GetDirectories(searchDir))
            {
                var result = FindWorldRoot(subDir);
                if (result != null) return result;
            }

            return null;
        }
    }
}
