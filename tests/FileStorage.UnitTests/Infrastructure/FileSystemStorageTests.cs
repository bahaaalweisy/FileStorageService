using System.Security.Cryptography;
using System.Text;
using FileStorage.Application.Abstractions;
using FileStorage.Application.Exceptions;
using FileStorage.Application.Options;
using FileStorage.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FileStorage.UnitTests.Infrastructure;

public class FileSystemStorageTests : IDisposable
{
    private readonly string _root;
    private readonly FileSystemStorage _storage;

    public FileSystemStorageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "filestorage-fs-tests-" + Guid.NewGuid().ToString("N"));
        _storage = new FileSystemStorage(
            Options.Create(new StorageRootOptions { RootPath = _root }),
            Options.Create(new UploadPolicyOptions()),
            NullLogger<FileSystemStorage>.Instance);
    }

    [Fact]
    public async Task StageAsync_ComputesCorrectSha256()
    {
        var content = Encoding.UTF8.GetBytes("the quick brown fox jumps over the lazy dog");
        var expected = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        using var stream = new MemoryStream(content);
        var staged = await _storage.StageAsync(stream, maxSizeBytes: 1_000_000, CancellationToken.None);

        staged.ChecksumSha256Hex.Should().Be(expected);
        staged.SizeBytes.Should().Be(content.Length);

        await _storage.DiscardStagedAsync(staged.TempPath, CancellationToken.None);
    }

    [Fact]
    public async Task CommitAsync_ContentIsReadableAtResolvedPath()
    {
        var content = Encoding.UTF8.GetBytes("hello atomic world");
        var key = "Abcdefgh12345678ijkl";
        var createdAt = DateTime.UtcNow;

        using (var stream = new MemoryStream(content))
        {
            var staged = await _storage.StageAsync(stream, 1_000_000, CancellationToken.None);
            await _storage.CommitAsync(staged.TempPath, key, createdAt, CancellationToken.None);
        }

        var path = _storage.ResolveExistingContentPath(key, createdAt);
        path.Should().NotBeNull();
        (await File.ReadAllBytesAsync(path!)).Should().BeEquivalentTo(content);
    }

    [Fact]
    public async Task StageAsync_ExceedingMaxSize_ThrowsAndLeavesNoTempFile()
    {
        var content = new byte[1000];
        using var stream = new MemoryStream(content);

        var act = async () => await _storage.StageAsync(stream, maxSizeBytes: 100, CancellationToken.None);

        await act.Should().ThrowAsync<PayloadTooLargeAppException>();

        var tempDir = Path.Combine(_root, "_tmp");
        Directory.Exists(tempDir).Should().BeTrue();
        Directory.GetFiles(tempDir).Should().BeEmpty("the temp file must be cleaned up after a policy rejection");
    }

    [Fact]
    public async Task StageAsync_EmptyStream_ThrowsValidationException()
    {
        using var stream = new MemoryStream(Array.Empty<byte>());

        var act = async () => await _storage.StageAsync(stream, 1_000_000, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationAppException>();
    }

    [Fact]
    public async Task CommitAsync_WhenDestinationAlreadyOccupied_ThrowsCollisionException_AndDoesNotOverwrite()
    {
        var key = "Abcdefgh12345678ijkl";
        var createdAt = DateTime.UtcNow;
        var originalContent = Encoding.UTF8.GetBytes("the original, must-not-be-overwritten content");

        using (var firstStream = new MemoryStream(originalContent))
        {
            var firstStaged = await _storage.StageAsync(firstStream, 1_000_000, CancellationToken.None);
            await _storage.CommitAsync(firstStaged.TempPath, key, createdAt, CancellationToken.None);
        }

        using var secondStream = new MemoryStream(Encoding.UTF8.GetBytes("a different file that collided on the same key"));
        var secondStaged = await _storage.StageAsync(secondStream, 1_000_000, CancellationToken.None);

        var act = async () => await _storage.CommitAsync(secondStaged.TempPath, key, createdAt, CancellationToken.None);

        await act.Should().ThrowAsync<StorageKeyCollisionException>();

        var path = _storage.ResolveExistingContentPath(key, createdAt);
        (await File.ReadAllBytesAsync(path!)).Should().BeEquivalentTo(originalContent);

        File.Exists(secondStaged.TempPath).Should().BeTrue();
        await _storage.DiscardStagedAsync(secondStaged.TempPath, CancellationToken.None);
    }

    [Fact]
    public async Task DiscardStagedAsync_IsSafe_WhenTempFileAlreadyGone()
    {
        var act = async () => await _storage.DiscardStagedAsync(Path.Combine(_root, "_tmp", "does-not-exist.tmp"), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteContentAsync_ReturnsFalse_WhenFileDoesNotExist()
    {
        var deleted = await _storage.DeleteContentAsync("Abcdefgh12345678ijkl", DateTime.UtcNow, CancellationToken.None);

        deleted.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteContentAsync_RemovesFile_WhenPresent()
    {
        var key = "Abcdefgh12345678ijkl";
        var createdAt = DateTime.UtcNow;
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("data")))
        {
            var staged = await _storage.StageAsync(stream, 1_000_000, CancellationToken.None);
            await _storage.CommitAsync(staged.TempPath, key, createdAt, CancellationToken.None);
        }

        var deleted = await _storage.DeleteContentAsync(key, createdAt, CancellationToken.None);

        deleted.Should().BeTrue();
        _storage.ResolveExistingContentPath(key, createdAt).Should().BeNull();
    }

    [Fact]
    public async Task CheckReadWriteAsync_ReportsHealthy_AndLeavesNoProbeFilesBehind()
    {
        var result = await _storage.CheckReadWriteAsync(CancellationToken.None);

        result.CanReadWrite.Should().BeTrue();
        var healthDir = Path.Combine(_root, "_health");
        Directory.GetFiles(healthDir).Should().BeEmpty();
    }

    // Note: a readiness-probe-failure test belongs here in principle, but FileSystemStorage's
    // constructor eagerly calls Directory.CreateDirectory(rootPath) (StoragePathResolver.cs:14),
    // so any attempt to construct it with an unusable root throws before CheckReadWriteAsync can
    // even run. The genuine failure-mode test lives instead in
    // FileStorage.IntegrationTests/HealthCheckTests.cs, which blocks the probe's "_health"
    // subdirectory (created lazily, inside CheckReadWriteAsync) and asserts /health/ready fails
    // closed through the real HTTP pipeline.

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
