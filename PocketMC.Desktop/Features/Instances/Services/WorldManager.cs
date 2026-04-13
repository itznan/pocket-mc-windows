using PocketMC.Desktop.Features.Instances.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Infrastructure.Process;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Core.Presentation;

namespace PocketMC.Desktop.Features.Instances.Services;

/// <summary>
/// Handles intelligent world ZIP extraction by hunting for level.dat
/// to find the true world root, regardless of how the ZIP was packaged.
/// </summary>
public class WorldManager
{
    private readonly ILogger<WorldManager> _logger;

    public WorldManager(ILogger<WorldManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extracts a world ZIP to the target path, intelligently finding the real world root.
    /// </summary>
    public async Task ImportWorldZipAsync(string zipPath, string targetWorldPath, Action<string>? onProgress = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PocketMC", $"Extraction-{Guid.NewGuid()}");

        try
        {
            onProgress?.Invoke("Extracting ZIP...");
            await SafeZipExtractor.ExtractAsync(zipPath, tempDir);

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
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to clean up temporary world extraction directory {TempDir}.", tempDir);
                }
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
