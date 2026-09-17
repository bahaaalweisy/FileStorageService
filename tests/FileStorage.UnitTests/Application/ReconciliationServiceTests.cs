using FileStorage.Application.Abstractions;
using FileStorage.Application.Files.Dtos;
using FileStorage.Application.Maintenance;
using FileStorage.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FileStorage.UnitTests.Application;

public class ReconciliationServiceTests
{
    [Fact]
    public async Task ScanAsync_ReportsOrphanFile_WhenNoMatchingDatabaseRow()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var repository = new FakeRepository();
        var inventory = new FakeInventory(new[]
        {
            new StoredContentEntry("orphan-key", clock.UtcNow.AddDays(-1), clock.UtcNow.AddHours(-1), 100),
        });

        var service = new ReconciliationService(repository, inventory, new FakeFileStorage(), clock, NullLogger<ReconciliationService>.Instance);

        var report = await service.ScanAsync(null, CancellationToken.None);

        report.OrphanFiles.Should().ContainSingle(x => x.Key == "orphan-key");
        report.MissingContent.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanAsync_DoesNotReportRecentlyWrittenFile_AsOrphan_WithinGracePeriod()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var repository = new FakeRepository();
        var inventory = new FakeInventory(new[]
        {
            new StoredContentEntry("fresh-key", clock.UtcNow, clock.UtcNow.AddMinutes(-2), 100),
        });

        var service = new ReconciliationService(repository, inventory, new FakeFileStorage(), clock, NullLogger<ReconciliationService>.Instance);

        var report = await service.ScanAsync(TimeSpan.FromMinutes(15), CancellationToken.None);

        report.OrphanFiles.Should().BeEmpty("a file written within the grace period must never be reported (or deleted) as an orphan");
    }

    [Fact]
    public async Task ScanAsync_ReportsMissingContent_WhenDatabaseRowHasNoFile()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var id = Guid.NewGuid();
        var repository = new FakeRepository(new StoredObjectKeyInfo(id, "missing-key", clock.UtcNow.AddDays(-1), IsSoftDeleted: false));
        var inventory = new FakeInventory(Array.Empty<StoredContentEntry>());

        var service = new ReconciliationService(repository, inventory, new FakeFileStorage(), clock, NullLogger<ReconciliationService>.Instance);

        var report = await service.ScanAsync(null, CancellationToken.None);

        report.MissingContent.Should().ContainSingle(x => x.Id == id && x.Key == "missing-key");
        report.OrphanFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanAsync_ReportsNothing_WhenEveryRowHasMatchingContent()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var id = Guid.NewGuid();
        var repository = new FakeRepository(new StoredObjectKeyInfo(id, "healthy-key", clock.UtcNow.AddDays(-1), IsSoftDeleted: false));
        var inventory = new FakeInventory(new[]
        {
            new StoredContentEntry("healthy-key", clock.UtcNow.AddDays(-1), clock.UtcNow.AddHours(-2), 100),
        });

        var service = new ReconciliationService(repository, inventory, new FakeFileStorage(), clock, NullLogger<ReconciliationService>.Instance);

        var report = await service.ScanAsync(null, CancellationToken.None);

        report.OrphanFiles.Should().BeEmpty();
        report.MissingContent.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteOrphanFilesAsync_OnlyDeletesPassedOrphans_NeverTouchesDatabase()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var fileStorage = new FakeFileStorage();
        var repository = new FakeRepository();
        var service = new ReconciliationService(repository, new FakeInventory(Array.Empty<StoredContentEntry>()), fileStorage, clock, NullLogger<ReconciliationService>.Instance);

        var orphans = new[] { new OrphanFileEntry("orphan-key", clock.UtcNow.AddDays(-1), clock.UtcNow.AddHours(-1), 10) };

        var deletedCount = await service.DeleteOrphanFilesAsync(orphans, CancellationToken.None);

        deletedCount.Should().Be(1);
        fileStorage.DeletedKeys.Should().ContainSingle().Which.Should().Be("orphan-key");
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTime utcNow) => UtcNow = utcNow;
        public DateTime UtcNow { get; }
    }

    private sealed class FakeInventory : IStorageInventory
    {
        private readonly IReadOnlyList<StoredContentEntry> _entries;
        public FakeInventory(IReadOnlyList<StoredContentEntry> entries) => _entries = entries;

        public async IAsyncEnumerable<StoredContentEntry> EnumerateContentAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var entry in _entries)
            {
                yield return entry;
            }
            await Task.CompletedTask;
        }
    }

    private sealed class FakeRepository : IStoredObjectRepository
    {
        private readonly List<StoredObjectKeyInfo> _rows;
        public FakeRepository(params StoredObjectKeyInfo[] rows) => _rows = rows.ToList();

        public async IAsyncEnumerable<StoredObjectKeyInfo> StreamAllKeysAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var row in _rows)
            {
                yield return row;
            }
            await Task.CompletedTask;
        }

        public Task AddAsync(StoredObject storedObject, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<StoredObject?> FindActiveAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<StoredObject?> FindAnyAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<PagedResult<StoredObject>> ListAsync(ListFilesQuery query, CancellationToken cancellationToken) => throw new NotImplementedException();
        public void Remove(StoredObject storedObject) => throw new NotImplementedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class FakeFileStorage : IFileStorage
    {
        public List<string> DeletedKeys { get; } = new();

        public Task<StagedFile> StageAsync(Stream source, long maxSizeBytes, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task CommitAsync(string tempPath, string key, DateTime createdAtUtc, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task DiscardStagedAsync(string tempPath, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public string? ResolveExistingContentPath(string key, DateTime createdAtUtc) => throw new NotImplementedException();

        public Task<bool> DeleteContentAsync(string key, DateTime createdAtUtc, CancellationToken cancellationToken)
        {
            DeletedKeys.Add(key);
            return Task.FromResult(true);
        }

        public Task<FileSystemProbeResult> CheckReadWriteAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
