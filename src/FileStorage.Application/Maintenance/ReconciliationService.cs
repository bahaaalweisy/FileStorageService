using FileStorage.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace FileStorage.Application.Maintenance;

public sealed record OrphanFileEntry(string Key, DateTime CreatedAtUtc, DateTime LastWriteTimeUtc, long SizeBytes);

public sealed record MissingContentEntry(Guid Id, string Key, DateTime CreatedAtUtc, bool IsSoftDeleted);

public sealed record ReconciliationReport(
    IReadOnlyList<OrphanFileEntry> OrphanFiles,
    IReadOnlyList<MissingContentEntry> MissingContent,
    int TotalDatabaseRows,
    int TotalFilesScanned);

public sealed class ReconciliationService
{
    private static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromMinutes(15);

    private readonly IStoredObjectRepository _repository;
    private readonly IStorageInventory _inventory;
    private readonly IFileStorage _fileStorage;
    private readonly IClock _clock;
    private readonly ILogger<ReconciliationService> _logger;

    public ReconciliationService(
        IStoredObjectRepository repository,
        IStorageInventory inventory,
        IFileStorage fileStorage,
        IClock clock,
        ILogger<ReconciliationService> logger)
    {
        _repository = repository;
        _inventory = inventory;
        _fileStorage = fileStorage;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ReconciliationReport> ScanAsync(TimeSpan? gracePeriod, CancellationToken cancellationToken)
    {
        var grace = gracePeriod ?? DefaultGracePeriod;
        var now = _clock.UtcNow;

        var databaseByKey = new Dictionary<string, Application.Abstractions.StoredObjectKeyInfo>(StringComparer.Ordinal);
        await foreach (var row in _repository.StreamAllKeysAsync(cancellationToken))
        {
            databaseByKey[row.Key] = row;
        }

        var seenOnDisk = new HashSet<string>(StringComparer.Ordinal);
        var orphans = new List<OrphanFileEntry>();
        var filesScanned = 0;

        await foreach (var entry in _inventory.EnumerateContentAsync(cancellationToken))
        {
            filesScanned++;

            if (databaseByKey.ContainsKey(entry.Key))
            {
                seenOnDisk.Add(entry.Key);
                continue;
            }

            var age = now - entry.LastWriteTimeUtc;
            if (age < grace)
            {
                continue;
            }

            orphans.Add(new OrphanFileEntry(entry.Key, entry.CreatedAtUtc, entry.LastWriteTimeUtc, entry.SizeBytes));
        }

        var missing = databaseByKey.Values
            .Where(row => !seenOnDisk.Contains(row.Key))
            .Select(row => new MissingContentEntry(row.Id, row.Key, row.CreatedAtUtc, row.IsSoftDeleted))
            .ToList();

        _logger.LogInformation(
            "Reconciliation scan complete. DatabaseRows={DbRows} FilesScanned={Files} Orphans={Orphans} MissingContent={Missing}",
            databaseByKey.Count, filesScanned, orphans.Count, missing.Count);

        return new ReconciliationReport(orphans, missing, databaseByKey.Count, filesScanned);
    }

    public async Task<int> DeleteOrphanFilesAsync(IEnumerable<OrphanFileEntry> orphans, CancellationToken cancellationToken)
    {
        var deleted = 0;
        foreach (var orphan in orphans)
        {
            var removed = await _fileStorage.DeleteContentAsync(orphan.Key, orphan.CreatedAtUtc, cancellationToken);
            if (removed)
            {
                deleted++;
                _logger.LogWarning("Reconciliation deleted orphan file. Key={Key} CreatedAtUtc={CreatedAtUtc}", orphan.Key, orphan.CreatedAtUtc);
            }
        }

        return deleted;
    }
}
