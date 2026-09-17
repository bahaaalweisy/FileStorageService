using FileStorage.Application.Abstractions;
using FileStorage.Application.Exceptions;
using FileStorage.Application.Files.Dtos;
using FileStorage.Application.Options;
using FileStorage.Application.Uploads;
using FileStorage.Domain.Entities;
using FileStorage.Domain.Enums;
using FileStorage.Domain.Exceptions;
using FileStorage.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FileStorage.UnitTests.Application;

public class ResumableUploadServiceTests
{
    [Fact]
    public async Task CleanupExpiredSessionsAsync_ExpiresOnlyPastDeadlineSessions_AndDiscardsTheirTempFiles()
    {
        var clock = new FakeClock(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        var fileStorage = new FakeFileStorage();

        var expired = UploadSession.Create("user-1", "old.bin", "application/octet-stream", null, 100,
            nowUtc: new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc), timeToLive: TimeSpan.FromHours(1));
        var stillActive = UploadSession.Create("user-1", "new.bin", "application/octet-stream", null, 100,
            nowUtc: clock.UtcNow, timeToLive: TimeSpan.FromHours(24));

        var repo = new FakeSessionRepository(new[] { expired, stillActive });
        var service = CreateService(repo, fileStorage, clock);

        var count = await service.CleanupExpiredSessionsAsync(maxBatchSize: 10, CancellationToken.None);

        count.Should().Be(1);
        expired.Status.Should().Be(UploadSessionStatus.Expired);
        stillActive.Status.Should().Be(UploadSessionStatus.InProgress, "a session still within its TTL must never be touched by cleanup");
        fileStorage.DiscardedPaths.Should().ContainSingle(p => p.Contains(expired.Id.ToString("N")));
        fileStorage.DiscardedPaths.Should().NotContain(p => p.Contains(stillActive.Id.ToString("N")));
    }

    [Fact]
    public async Task AppendChunkAsync_ConcurrentAppendsToSameSession_SerializeWithoutCorruptionOrLostBytes()
    {

        var root = Path.Combine(Path.GetTempPath(), "filestorage-resumable-concurrency-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new FileSystemStorage(
                Options.Create(new StorageRootOptions { RootPath = root }),
                Options.Create(new UploadPolicyOptions()),
                NullLogger<FileSystemStorage>.Instance);

            var sessionId = Guid.NewGuid();
            var tempPath = storage.GetUploadSessionTempPath(sessionId);
            const int chunkSize = 1000;
            const int chunkCount = 8;

            var tasks = new List<Task>();
            for (var i = 0; i < chunkCount; i++)
            {
                var chunkIndex = i;
                tasks.Add(Task.Run(async () =>
                {
                    var data = new byte[chunkSize];
                    Array.Fill(data, (byte)(chunkIndex % 256));
                    var expectedOffset = (long)chunkIndex * chunkSize;

                    for (var attempt = 0; attempt < chunkCount * 4; attempt++)
                    {
                        var currentLength = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0;
                        if (currentLength > expectedOffset)
                        {
                            return;
                        }

                        using var content = new MemoryStream(data);
                        try
                        {
                            await storage.AppendChunkAsync(tempPath, expectedOffset, content, chunkCount * chunkSize, CancellationToken.None);
                            return;
                        }
                        catch (ChunkOffsetMismatchException)
                        {
                            await Task.Delay(5);
                        }
                    }

                    throw new TimeoutException($"Chunk {chunkIndex} never became appendable.");
                }));
            }

            await Task.WhenAll(tasks);

            var finalBytes = await File.ReadAllBytesAsync(tempPath);
            finalBytes.Length.Should().Be(chunkCount * chunkSize, "no bytes may be lost or duplicated despite concurrent, racing append attempts");

            for (var i = 0; i < chunkCount; i++)
            {
                var segment = finalBytes[(i * chunkSize)..((i + 1) * chunkSize)];
                segment.Should().OnlyContain(b => b == (byte)(i % 256), $"chunk {i}'s bytes must not be mixed with another chunk's bytes");
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ResumableUploadService CreateService(IUploadSessionRepository repo, IFileStorage storage, IClock clock) =>
        new(repo,
            new FakeStoredObjectRepository(),
            storage,
            new FakeKeyGenerator(),
            clock,
            Options.Create(new UploadPolicyOptions()),
            Options.Create(new ResumableUploadOptions()),
            NullLogger<ResumableUploadService>.Instance,
            new NoOpAuditLogWriter());

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTime utcNow) => UtcNow = utcNow;
        public DateTime UtcNow { get; }
    }

    private sealed class NoOpAuditLogWriter : IAuditLogWriter
    {
        public Task RecordAsync(string operation, string? resourceId, string? resourceType, string outcome, string? detail, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeKeyGenerator : IStorageKeyGenerator
    {
        private int _n;
        public string Generate() => $"FakeKey{Interlocked.Increment(ref _n):D16}";
    }

    private sealed class FakeSessionRepository : IUploadSessionRepository
    {
        private readonly List<UploadSession> _sessions;
        public FakeSessionRepository(IEnumerable<UploadSession> sessions) => _sessions = sessions.ToList();

        public Task AddAsync(UploadSession session, CancellationToken cancellationToken)
        {
            _sessions.Add(session);
            return Task.CompletedTask;
        }

        public Task<UploadSession?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_sessions.FirstOrDefault(x => x.Id == id));

        public Task<IReadOnlyList<UploadSession>> FindExpiredInProgressAsync(DateTime nowUtc, int maxCount, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UploadSession>>(
                _sessions.Where(x => x.Status == UploadSessionStatus.InProgress && x.IsExpired(nowUtc)).Take(maxCount).ToList());

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeStoredObjectRepository : IStoredObjectRepository
    {
        public Task AddAsync(StoredObject storedObject, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<StoredObject?> FindActiveAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<StoredObject?> FindAnyAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<PagedResult<StoredObject>> ListAsync(ListFilesQuery query, CancellationToken cancellationToken) => throw new NotImplementedException();
        public void Remove(StoredObject storedObject) => throw new NotImplementedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public async IAsyncEnumerable<StoredObjectKeyInfo> StreamAllKeysAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FakeFileStorage : IFileStorage
    {
        public List<string> DiscardedPaths { get; } = new();

        public Task<StagedFile> StageAsync(Stream source, long maxSizeBytes, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task CommitAsync(string tempPath, string key, DateTime createdAtUtc, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task DiscardStagedAsync(string tempPath, CancellationToken cancellationToken)
        {
            DiscardedPaths.Add(tempPath);
            return Task.CompletedTask;
        }
        public string? ResolveExistingContentPath(string key, DateTime createdAtUtc) => throw new NotImplementedException();
        public Task<bool> DeleteContentAsync(string key, DateTime createdAtUtc, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<FileSystemProbeResult> CheckReadWriteAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public string GetUploadSessionTempPath(Guid sessionId) => $"/fake/{sessionId:N}.part";
        public Task<long> AppendChunkAsync(string tempPath, long expectedOffset, Stream chunkData, long maxTotalBytes, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<string> ComputeChecksumAsync(string tempPath, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
