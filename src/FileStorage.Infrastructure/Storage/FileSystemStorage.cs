using System.Security.Cryptography;
using FileStorage.Application.Abstractions;
using FileStorage.Application.Exceptions;
using FileStorage.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileStorage.Infrastructure.Storage;

public sealed class FileSystemStorage : IFileStorage
{
    private readonly StoragePathResolver _paths;
    private readonly UploadPolicyOptions _policy;
    private readonly ILogger<FileSystemStorage> _logger;

    public FileSystemStorage(IOptions<StorageRootOptions> rootOptions, IOptions<UploadPolicyOptions> policy, ILogger<FileSystemStorage> logger)
    {
        _paths = new StoragePathResolver(rootOptions.Value.RootPath);
        _policy = policy.Value;
        _logger = logger;
    }

    public async Task<StagedFile> StageAsync(Stream source, long maxSizeBytes, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(_paths.GetTempDirectory(), $"{Guid.NewGuid():N}.tmp");
        var buffer = new byte[_policy.CopyBufferSizeBytes];
        long totalBytes = 0;

        try
        {
            using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var tempStream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 1, useAsync: true))
            {
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    totalBytes += bytesRead;

                    if (totalBytes > maxSizeBytes)
                    {
                        throw new PayloadTooLargeAppException(
                            $"Upload exceeds the configured maximum of {maxSizeBytes} bytes.");
                    }

                    incrementalHash.AppendData(buffer, 0, bytesRead);
                    await tempStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }

                await tempStream.FlushAsync(cancellationToken);
            }

            if (totalBytes == 0)
            {
                throw new ValidationAppException("file", "Uploaded file is empty.");
            }

            var checksum = Convert.ToHexString(incrementalHash.GetHashAndReset()).ToLowerInvariant();
            return new StagedFile(tempPath, totalBytes, checksum);
        }
        catch (Exception ex)
        {
            CleanupTempFile(tempPath, ex);
            throw;
        }
    }

    public Task CommitAsync(string tempPath, string key, DateTime createdAtUtc, CancellationToken cancellationToken)
    {
        var finalPath = _paths.GetContentPath(key, createdAtUtc);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

        if (File.Exists(finalPath))
        {
            throw new StorageKeyCollisionException(key);
        }

        try
        {
            File.Move(tempPath, finalPath);
        }
        catch (IOException) when (File.Exists(finalPath))
        {
            throw new StorageKeyCollisionException(key);
        }

        return Task.CompletedTask;
    }

    public Task DiscardStagedAsync(string tempPath, CancellationToken cancellationToken)
    {
        CleanupTempFile(tempPath, cause: null);
        return Task.CompletedTask;
    }

    private void CleanupTempFile(string tempPath, Exception? cause)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception cleanupEx)
        {
            _logger.LogError(cleanupEx, "Failed to clean up temp file {TempPath} after {CauseType}", tempPath, cause?.GetType().Name ?? "explicit discard");
        }
    }

    public string? ResolveExistingContentPath(string key, DateTime createdAtUtc)
    {
        var path = _paths.GetContentPath(key, createdAtUtc);
        return File.Exists(path) ? path : null;
    }

    public Task<bool> DeleteContentAsync(string key, DateTime createdAtUtc, CancellationToken cancellationToken)
    {
        var path = _paths.GetContentPath(key, createdAtUtc);

        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);

        TryRemoveEmptyParents(Path.GetDirectoryName(path));

        return Task.FromResult(true);
    }

    private void TryRemoveEmptyParents(string? directory)
    {
        var current = directory;
        for (var i = 0; i < 4 && current is not null && current.StartsWith(_paths.RootFullPath, StringComparison.OrdinalIgnoreCase); i++)
        {
            try
            {
                if (Directory.Exists(current) && !Directory.EnumerateFileSystemEntries(current).Any())
                {
                    Directory.Delete(current);
                    current = Path.GetDirectoryName(current);
                    continue;
                }
            }
            catch (IOException)
            {
            }

            break;
        }
    }

    public async Task<FileSystemProbeResult> CheckReadWriteAsync(CancellationToken cancellationToken)
    {
        var probeDir = _paths.GetHealthProbeDirectory();
        var probePath = Path.Combine(probeDir, $"probe-{Guid.NewGuid():N}.tmp");

        try
        {
            var payload = "health-probe"u8.ToArray();
            await File.WriteAllBytesAsync(probePath, payload, cancellationToken);
            var readBack = await File.ReadAllBytesAsync(probePath, cancellationToken);

            if (!payload.AsSpan().SequenceEqual(readBack))
            {
                return new FileSystemProbeResult(false, "Probe file content mismatch on readback.");
            }

            return new FileSystemProbeResult(true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Filesystem readiness probe failed");
            return new FileSystemProbeResult(false, "Filesystem is not writable.");
        }
        finally
        {
            try
            {
                if (File.Exists(probePath))
                {
                    File.Delete(probePath);
                }
            }
            catch
            {
            }
        }
    }
}
