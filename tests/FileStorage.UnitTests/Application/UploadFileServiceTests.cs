using System.Text;
using FileStorage.Application.Abstractions;
using FileStorage.Application.Exceptions;
using FileStorage.Application.Files.Dtos;
using FileStorage.Application.Files.UseCases;
using FileStorage.Application.Options;
using FileStorage.Domain.Entities;
using FileStorage.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FileStorage.UnitTests.Application;

public class UploadFileServiceTests : IDisposable
{
    private readonly string _root;
    private readonly FileSystemStorage _fileStorage;
    private readonly FakeRepository _repository;
    private readonly FakeClock _clock;

    public UploadFileServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "filestorage-uploadsvc-tests-" + Guid.NewGuid().ToString("N"));
        _fileStorage = new FileSystemStorage(
            Options.Create(new StorageRootOptions { RootPath = _root }),
            Options.Create(new UploadPolicyOptions()),
            NullLogger<FileSystemStorage>.Instance);
        _repository = new FakeRepository();
        _clock = new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private UploadFileService CreateService(IStorageKeyGenerator keyGenerator) =>
        new(_fileStorage, _repository, keyGenerator, _clock, Options.Create(new UploadPolicyOptions()), NullLogger<UploadFileService>.Instance, new NoOpAuditLogWriter());

    [Fact]
    public async Task FinalizeAsync_RetriesWithNewKey_WhenFirstGeneratedKeyCollides()
    {
        const string collidingKey = "CollideKeyAAAAAAAAAAAA";
        const string uniqueKey = "UniqueKeyBBBBBBBBBBBBB";

        using (var pre = new MemoryStream(Encoding.UTF8.GetBytes("pre-existing content")))
        {
            var staged = await _fileStorage.StageAsync(pre, 10_000, CancellationToken.None);
            await _fileStorage.CommitAsync(staged.TempPath, collidingKey, _clock.UtcNow, CancellationToken.None);
        }

        var service = CreateService(new SequenceKeyGenerator(collidingKey, uniqueKey));

        using var content = new MemoryStream(Encoding.UTF8.GetBytes("new upload content"));
        var stagedUpload = await service.StageAsync("report.txt", "text/plain", content, CancellationToken.None);

        var result = await service.FinalizeAsync(stagedUpload, Array.Empty<string>(), "user-1", CancellationToken.None);

        result.Key.Should().Be(uniqueKey, "the service must retry with a fresh key rather than fail or overwrite on a collision");
        _repository.Added.Should().ContainSingle();

        var preExistingPath = _fileStorage.ResolveExistingContentPath(collidingKey, _clock.UtcNow);
        (await File.ReadAllBytesAsync(preExistingPath!)).Should().BeEquivalentTo(Encoding.UTF8.GetBytes("pre-existing content"));
    }

    [Fact]
    public async Task StageAsync_RejectsDisallowedExtension_WithoutWritingAnyTempFile()
    {
        var policy = new UploadPolicyOptions { AllowedExtensions = new[] { ".txt" } };
        var service = new UploadFileService(_fileStorage, _repository, new SequenceKeyGenerator("k1"), _clock, Options.Create(policy), NullLogger<UploadFileService>.Instance, new NoOpAuditLogWriter());

        using var content = new MemoryStream(Encoding.UTF8.GetBytes("won't be read"));

        var act = async () => await service.StageAsync("malware.exe", "application/octet-stream", content, CancellationToken.None);

        await act.Should().ThrowAsync<UnsupportedMediaTypeAppException>();

        var tempDir = Path.Combine(_root, "_tmp");
        (!Directory.Exists(tempDir) || Directory.GetFiles(tempDir).Length == 0).Should().BeTrue(
            "rejecting the extension must happen before any bytes are streamed to disk");
    }

    [Fact]
    public async Task FinalizeAsync_CompensatesCommittedFile_WhenPersistenceIsCancelled()
    {
        var repository = new FakeRepository { ThrowCancellationOnSave = true };
        var service = new UploadFileService(_fileStorage, repository, new SequenceKeyGenerator("cancel-key-AAAAAAAAAAAA"), _clock, Options.Create(new UploadPolicyOptions()), NullLogger<UploadFileService>.Instance, new NoOpAuditLogWriter());

        using var content = new MemoryStream(Encoding.UTF8.GetBytes("will be cancelled after commit"));
        var staged = await service.StageAsync("cancelled.txt", "text/plain", content, CancellationToken.None);

        var act = async () => await service.FinalizeAsync(staged, Array.Empty<string>(), "user-1", CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();

        _fileStorage.ResolveExistingContentPath("cancel-key-AAAAAAAAAAAA", _clock.UtcNow).Should().BeNull(
            "the committed file must be deleted as compensation even when the failure is a cancellation, not just a generic exception");
    }

    [Fact]
    public async Task DiscardStagedAsync_RemovesTheStagedTempFile()
    {
        var service = CreateService(new SequenceKeyGenerator("unused-key"));
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("abandoned upload"));

        var staged = await service.StageAsync("draft.txt", "text/plain", content, CancellationToken.None);
        File.Exists(staged.TempPath).Should().BeTrue();

        await service.DiscardStagedAsync(staged, CancellationToken.None);

        File.Exists(staged.TempPath).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class SequenceKeyGenerator : IStorageKeyGenerator
    {
        private readonly Queue<string> _keys;
        public SequenceKeyGenerator(params string[] keys) => _keys = new Queue<string>(keys);
        public string Generate() => _keys.Count > 0 ? _keys.Dequeue() : throw new InvalidOperationException("No more fake keys queued.");
    }

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

    private sealed class FakeRepository : IStoredObjectRepository
    {
        public List<StoredObject> Added { get; } = new();
        public bool ThrowCancellationOnSave { get; set; }

        public Task AddAsync(StoredObject storedObject, CancellationToken cancellationToken)
        {
            Added.Add(storedObject);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (ThrowCancellationOnSave)
            {
                throw new OperationCanceledException("Simulated client disconnect during metadata persistence.");
            }

            return Task.CompletedTask;
        }

        public Task<StoredObject?> FindActiveAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<StoredObject?> FindAnyAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<PagedResult<StoredObject>> ListAsync(ListFilesQuery query, CancellationToken cancellationToken) => throw new NotImplementedException();
        public void Remove(StoredObject storedObject) => throw new NotImplementedException();

        public async IAsyncEnumerable<StoredObjectKeyInfo> StreamAllKeysAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
