using PocketMC.Desktop.Features.Instances.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Java;
using PocketMC.Desktop.Infrastructure;

namespace PocketMC.Desktop.Features.Instances.Services;
    /// <summary>
    /// Encapsulates the logic for configuring a Minecraft server process launch.
    /// Extracts complex PSI construction from ServerProcess.
    /// </summary>
    public class ServerLaunchConfigurator
    {
        private static readonly Regex AdvancedJvmArgTokenRegex = new(
            "\"[^\"]*\"|\\S+",
            RegexOptions.Compiled,
            TimeSpan.FromSeconds(1));

        private readonly JavaProvisioningService _javaProvisioning;
        private readonly PhpProvisioningService _phpProvisioning;
        private readonly ILogger<ServerLaunchConfigurator> _logger;

        public ServerLaunchConfigurator(
            JavaProvisioningService javaProvisioning, 
            PhpProvisioningService phpProvisioning,
            ILogger<ServerLaunchConfigurator> logger)
        {
            _javaProvisioning = javaProvisioning;
            _phpProvisioning = phpProvisioning;
            _logger = logger;
        }

        public async Task<ProcessStartInfo> ConfigureAsync(InstanceMetadata meta, string workingDir, string appRootPath, Action<string> onLog)
        {
            if (string.IsNullOrWhiteSpace(workingDir))
            {
                throw new DirectoryNotFoundException($"Could not locate directory for instance {meta.Name}.");
            }

            if (meta.ServerType != null && meta.ServerType.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase))
            {
                return ConfigureBedrock(meta, workingDir, onLog);
            }

            if (meta.ServerType != null && meta.ServerType.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase))
            {
                return await ConfigurePocketmineAsync(meta, workingDir, appRootPath, onLog);
            }

            // Java servers
            int requiredJavaVersion = JavaRuntimeResolver.GetRequiredJavaVersion(meta.MinecraftVersion);
            string javaPath = await EnsureAndResolveJavaPathAsync(meta, requiredJavaVersion, appRootPath, onLog);

            // Forge/NeoForge auto-installation
            await HandleInstallerBasedSetupAsync(meta, workingDir, javaPath, onLog);

            var psi = new ProcessStartInfo
            {
                FileName = javaPath,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            AddRamArguments(psi, meta);
            AddPerformanceArguments(psi);
            AddAdvancedArguments(psi, meta.AdvancedJvmArgs);
            AddExecutableArguments(psi, meta, workingDir);

            psi.ArgumentList.Add("nogui");

            return psi;
        }

        private ProcessStartInfo ConfigureBedrock(InstanceMetadata meta, string workingDir, Action<string> onLog)
        {
            onLog("[PocketMC] Launching Bedrock Dedicated Server...");
            
            string executablePath = Path.Combine(workingDir, "bedrock_server.exe");
            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException($"Bedrock server executable not found at {executablePath}. Ensure the ZIP was extracted correctly.");
            }

            var psi = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            return psi;
        }

        private async Task<ProcessStartInfo> ConfigurePocketmineAsync(InstanceMetadata meta, string workingDir, string appRootPath, Action<string> onLog)
        {
            onLog("[PocketMC] Verifying PHP runtime for Pocketmine-MP...");
            await _phpProvisioning.EnsurePhpAsync(null);

            string phpExePath = Path.Combine(appRootPath, "runtimes", "php", "bin", "php", "php.exe");
            if (!File.Exists(phpExePath))
            {
                throw new FileNotFoundException($"PHP executable not found at {phpExePath}.");
            }

            string pharPath = Path.Combine(workingDir, "PocketMine-MP.phar");
            if (!File.Exists(pharPath))
            {
                throw new FileNotFoundException($"PocketMine-MP.phar not found at {pharPath}.");
            }

            // ── PocketMine server.properties sanity fixes ────────────────────────
            // PocketMine only accepts: DEFAULT, FLAT, NETHER, THE_END, HELL
            // Java-style values like "minecraft:normal" or "default" (lowercase) cause:
            //   [ERROR]: Could not generate world: Unknown generator "minecraft:normal"
            PatchPocketmineServerProperties(workingDir, onLog);

            var psi = new ProcessStartInfo
            {
                FileName = phpExePath,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            psi.ArgumentList.Add(pharPath);
            psi.ArgumentList.Add("--no-wizard");

            // Add pocketmine specific arguments, if any from advanced args
            AddAdvancedArguments(psi, meta.AdvancedJvmArgs);

            return psi;
        }

        /// <summary>
        /// Rewrites keys in server.properties that PocketMine-MP would otherwise reject.
        /// <list type="bullet">
        ///   <item><c>level-type</c>: Java namespaced values (e.g. <c>minecraft:normal</c>) → <c>DEFAULT</c></item>
        /// </list>
        /// </summary>
        private void PatchPocketmineServerProperties(string workingDir, Action<string> onLog)
        {
            string propsPath = Path.Combine(workingDir, "server.properties");
            if (!File.Exists(propsPath)) return;

            try
            {
                var lines = File.ReadAllLines(propsPath);
                bool changed = false;

                // Valid PocketMine generator names (case-sensitive in PM source).
                // Any other value for level-type causes an "Unknown generator" crash.
                var validPmGenerators = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "DEFAULT", "FLAT", "NETHER", "THE_END", "HELL"
                };

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (!line.StartsWith("level-type", StringComparison.OrdinalIgnoreCase)) continue;

                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;

                    string currentValue = line[(eq + 1)..].Trim();
                    if (!validPmGenerators.Contains(currentValue))
                    {
                        lines[i] = $"level-type=DEFAULT";
                        onLog($"[PocketMC] Patched server.properties: level-type={currentValue} → DEFAULT (PocketMine does not support Java generator names)");
                        changed = true;
                    }
                }

                if (changed)
                    File.WriteAllLines(propsPath, lines);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not patch PocketMine server.properties; server may crash on first boot.");
            }
        }


        private async Task<string> EnsureAndResolveJavaPathAsync(InstanceMetadata meta, int requiredVersion, string appRootPath, Action<string> onLog)
        {
            // Architecture: Ensure required Java runtime is present and healthy (Auto-Repair)
            bool expectsBundled = string.IsNullOrWhiteSpace(meta.CustomJavaPath) ||
                                  JavaRuntimeResolver.IsBundledJavaPath(meta.CustomJavaPath, requiredVersion, appRootPath);

            bool missingCustom = !string.IsNullOrWhiteSpace(meta.CustomJavaPath) && !File.Exists(meta.CustomJavaPath);

            if (expectsBundled || missingCustom)
            {
                if (!_javaProvisioning.IsJavaVersionPresent(requiredVersion))
                {
                    onLog($"[PocketMC] Required Java {requiredVersion} is missing or corrupt. Starting auto-repair...");
                    try
                    {
                        await _javaProvisioning.EnsureJavaAsync(requiredVersion);
                        onLog($"[PocketMC] Java {requiredVersion} repaired successfully.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Java auto-repair failed for instance {InstanceName}.", meta.Name);
                        onLog($"[PocketMC] CRITICAL: Java auto-repair failed: {ex.Message}");
                        throw;
                    }
                }
            }

            string javaPath = JavaRuntimeResolver.ResolveJavaPath(meta, appRootPath);
            string? bundledJavaPath = JavaRuntimeResolver.GetBundledJavaPath(appRootPath, requiredVersion);

            if (javaPath == "java")
            {
                _logger.LogWarning("Bundled Java {Version} not found for {Name}. Falling back to system java.", requiredVersion, meta.Name);
            }
            else if (bundledJavaPath != null && string.Equals(javaPath, bundledJavaPath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Using bundled Java {Version} for {Name} at {Path}.", requiredVersion, meta.Name, javaPath);
            }

            return javaPath;
        }

        private async Task HandleInstallerBasedSetupAsync(InstanceMetadata meta, string workingDir, string javaPath, Action<string> onLog)
        {
            string installerPath = Path.Combine(workingDir, "installer.jar");
            bool isForgeOrNeo = meta.ServerType == "Forge" || meta.ServerType == "NeoForge";

            if (isForgeOrNeo && File.Exists(installerPath) && !Directory.Exists(Path.Combine(workingDir, "libraries")))
            {
                onLog($"[PocketMC] First-time {meta.ServerType} setup detected. Running installer...");

                var installerPsi = new ProcessStartInfo
                {
                    FileName = javaPath,
                    WorkingDirectory = workingDir,
                    Arguments = "-Djava.awt.headless=true -Dforge.stdout=true -jar installer.jar --installServer",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(installerPsi);
                if (proc != null)
                {
                    // consume streams asynchronously to prevent deadlock
                    var outputTask = Task.Run(() => {
                        while (!proc.StandardOutput.EndOfStream)
                        {
                            var line = proc.StandardOutput.ReadLine();
                            if (line != null) onLog?.Invoke(line);
                        }
                    });

                    var errorTask = Task.Run(() => {
                        while (!proc.StandardError.EndOfStream)
                        {
                            var line = proc.StandardError.ReadLine();
                            if (line != null) onLog?.Invoke($"[Error] {line}");
                        }
                    });

                    await proc.WaitForExitAsync();
                    await Task.WhenAll(outputTask, errorTask);

                    if (proc.ExitCode == 0) onLog?.Invoke($"[PocketMC] {meta.ServerType} installation successful.");
                    else throw new Exception($"{meta.ServerType} installer failed with exit code {proc.ExitCode}");
                }
            }
        }

        private void AddRamArguments(ProcessStartInfo psi, InstanceMetadata meta)
        {
            var minRamMb = Math.Max(128, meta.MinRamMb);
            var maxRamMb = Math.Max(minRamMb, meta.MaxRamMb);
            psi.ArgumentList.Add($"-Xms{minRamMb}M");
            psi.ArgumentList.Add($"-Xmx{maxRamMb}M");
        }

        private void AddPerformanceArguments(ProcessStartInfo psi)
        {
            psi.ArgumentList.Add("-XX:+UseG1GC");
            psi.ArgumentList.Add("-XX:+ParallelRefProcEnabled");
            psi.ArgumentList.Add("-XX:MaxGCPauseMillis=200");
            psi.ArgumentList.Add("-XX:+UnlockExperimentalVMOptions");
            psi.ArgumentList.Add("-XX:+DisableExplicitGC");
            psi.ArgumentList.Add("-XX:+AlwaysPreTouch");
        }

        private void AddAdvancedArguments(ProcessStartInfo psi, string? advancedArgs)
        {
            foreach (var argument in TokenizeAdvancedJvmArgs(advancedArgs))
            {
                psi.ArgumentList.Add(argument);
            }
        }

        private void AddExecutableArguments(ProcessStartInfo psi, InstanceMetadata meta, string workingDir)
        {
            string serverJar = Path.Combine(workingDir, "server.jar");

            bool isForgeOrNeo = meta.ServerType == "Forge" || meta.ServerType == "NeoForge";
            if (isForgeOrNeo && !File.Exists(serverJar))
            {
                var winArgs = Directory.GetFiles(workingDir, "win_args.txt", SearchOption.AllDirectories).FirstOrDefault();
                if (winArgs != null)
                {
                    string relativeArgs = Path.GetRelativePath(workingDir, winArgs);
                    psi.ArgumentList.Add($"@{relativeArgs}");
                    return;
                }
            }

            // Fallback for non-Forge or old Forge
            psi.ArgumentList.Add("-jar");
            psi.ArgumentList.Add("server.jar");
        }

        private static IEnumerable<string> TokenizeAdvancedJvmArgs(string? advancedJvmArgs)
        {
            if (string.IsNullOrWhiteSpace(advancedJvmArgs)) yield break;

            foreach (Match match in AdvancedJvmArgTokenRegex.Matches(advancedJvmArgs))
            {
                var token = match.Value.Trim();
                if (string.IsNullOrWhiteSpace(token)) continue;

                if (token.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                    throw new InvalidOperationException("Advanced JVM arguments cannot contain control characters.");

                if (token.Length >= 2 && token.StartsWith('"') && token.EndsWith('"'))
                    token = token[1..^1];

                yield return token;
            }
        }
    }
