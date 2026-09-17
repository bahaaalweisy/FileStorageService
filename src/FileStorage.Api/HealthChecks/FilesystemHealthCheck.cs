using FileStorage.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FileStorage.Api.HealthChecks;

public sealed class FilesystemHealthCheck : IHealthCheck
{
    private readonly IFileStorage _fileStorage;

    public FilesystemHealthCheck(IFileStorage fileStorage) => _fileStorage = fileStorage;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var probe = await _fileStorage.CheckReadWriteAsync(cancellationToken);
        return probe.CanReadWrite
            ? HealthCheckResult.Healthy("Storage filesystem is read/write capable.")
            : HealthCheckResult.Unhealthy(probe.Detail ?? "Storage filesystem probe failed.");
    }
}
