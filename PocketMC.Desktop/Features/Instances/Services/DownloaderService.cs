using PocketMC.Desktop.Features.Instances.Models;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
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

namespace PocketMC.Desktop.Features.Instances.Services
{
    public class DownloaderService
    {
        private const string DownloadClientName = "PocketMC.Downloads";
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<DownloaderService> _logger;

        public DownloaderService(IHttpClientFactory httpClientFactory, ILogger<DownloaderService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task DownloadFileAsync(string url, string destinationPath, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            string partialPath = destinationPath + ".partial";
            string? directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            const int maxAttempts = 4;
            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using HttpClient client = _httpClientFactory.CreateClient(DownloadClientName);
                    long existingBytes = GetExistingPartialLength(partialPath);
                    using HttpRequestMessage request = new(HttpMethod.Get, url);
                    if (existingBytes > 0)
                    {
                        request.Headers.Range = new RangeHeaderValue(existingBytes, null);
                    }

                    using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                    if (existingBytes > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                    {
                        _logger.LogInformation("Server rejected resume for {Url}; restarting download from scratch.", url);
                        TryDeleteFile(partialPath);
                        existingBytes = 0;
                        using HttpRequestMessage restartRequest = new(HttpMethod.Get, url);
                        using HttpResponseMessage restartResponse = await client.SendAsync(restartRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        restartResponse.EnsureSuccessStatusCode();
                        await DownloadToPartialAsync(restartResponse, partialPath, existingBytes, progress, cancellationToken);
                        await PromoteCompletedDownloadAsync(partialPath, destinationPath, cancellationToken);
                        return;
                    }

                    response.EnsureSuccessStatusCode();

                    bool isResuming = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                    if (existingBytes > 0 && !isResuming)
                    {
                        _logger.LogInformation("Resume is not supported for {Url}; restarting download from scratch.", url);
                        TryDeleteFile(partialPath);
                        existingBytes = 0;
                    }

                    await DownloadToPartialAsync(response, partialPath, existingBytes, progress, cancellationToken);
                    await PromoteCompletedDownloadAsync(partialPath, destinationPath, cancellationToken);
                    return;
                }
                catch (Exception ex) when (IsRetryable(ex) && attempt < maxAttempts)
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "Download attempt {Attempt}/{MaxAttempts} failed for {Url}. Retrying...", attempt, maxAttempts, url);
                    await Task.Delay(GetRetryDelay(attempt), cancellationToken);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    break;
                }
            }

            throw new InvalidOperationException($"Failed to download '{url}' after {maxAttempts} attempts.", lastException);
        }

        /// <summary>
        /// Downloads playit.exe into <appRoot>/tunnel/playit.exe if not already present.
        /// Called during app startup alongside JRE downloads (NET-01).
        /// </summary>
        public async Task EnsurePlayitDownloadedAsync(string appRootPath, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            const string playitDownloadUrl = "https://github.com/playit-cloud/playit-agent/releases/latest/download/playit-windows-x86_64.exe";

            string tunnelDir = Path.Combine(appRootPath, "tunnel");
            string playitPath = Path.Combine(tunnelDir, "playit.exe");

            if (File.Exists(playitPath))
            {
                return;
            }

            Directory.CreateDirectory(tunnelDir);
            await DownloadFileAsync(playitDownloadUrl, playitPath, progress, cancellationToken);
        }

        public Task ExtractZipAsync(string zipPath, string extractPath, IProgress<DownloadProgress>? progress = null)
        {
            return SafeZipExtractor.ExtractAsync(
                zipPath,
                extractPath,
                (entriesExtracted, totalEntries) =>
                {
                    progress?.Report(new DownloadProgress
                    {
                        BytesRead = entriesExtracted,
                        TotalBytes = totalEntries
                    });
                });
        }

        private static bool IsRetryable(Exception ex) =>
            ex is HttpRequestException
            or IOException
            or TaskCanceledException;

        private static long GetExistingPartialLength(string partialPath)
        {
            try
            {
                return File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static long GetTotalBytes(HttpContentHeaders headers, long existingBytes, bool isResuming)
        {
            if (isResuming && headers.ContentRange?.Length is long rangedLength && rangedLength > 0)
            {
                return rangedLength;
            }

            if (headers.ContentLength is long contentLength && contentLength > 0)
            {
                return isResuming ? existingBytes + contentLength : contentLength;
            }

            return -1;
        }

        private static async Task DownloadToPartialAsync(
            HttpResponseMessage response,
            string partialPath,
            long existingBytes,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            bool isResuming = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            long totalBytes = GetTotalBytes(response.Content.Headers, existingBytes, isResuming);
            FileMode fileMode = isResuming ? FileMode.Append : FileMode.Create;

            progress?.Report(new DownloadProgress
            {
                BytesRead = existingBytes,
                TotalBytes = totalBytes
            });

            await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream fileStream = new(partialPath, fileMode, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            byte[] buffer = new byte[81920];
            long totalRead = existingBytes;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;

                progress?.Report(new DownloadProgress
                {
                    BytesRead = totalRead,
                    TotalBytes = totalBytes
                });
            }

            await fileStream.FlushAsync(cancellationToken);
        }

        private static async Task PromoteCompletedDownloadAsync(string partialPath, string destinationPath, CancellationToken cancellationToken)
        {
            const int maxPromotionAttempts = 5;

            for (int attempt = 1; attempt <= maxPromotionAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (File.Exists(destinationPath))
                    {
                        File.Delete(destinationPath);
                    }

                    File.Move(partialPath, destinationPath, overwrite: true);
                    return;
                }
                catch (Exception ex) when (attempt < maxPromotionAttempts && ex is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                }
            }

            File.Move(partialPath, destinationPath, overwrite: true);
        }

        private static TimeSpan GetRetryDelay(int attempt) => attempt switch
        {
            1 => TimeSpan.FromSeconds(2),
            2 => TimeSpan.FromSeconds(5),
            3 => TimeSpan.FromSeconds(10),
            _ => TimeSpan.FromSeconds(15)
        };

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }
}
