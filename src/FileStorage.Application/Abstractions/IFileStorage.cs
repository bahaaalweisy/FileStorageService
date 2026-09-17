namespace FileStorage.Application.Abstractions;

public sealed record StagedFile(string TempPath, long SizeBytes, string ChecksumSha256Hex);

public sealed record FileSystemProbeResult(bool CanReadWrite, string? Detail);

public sealed class StorageKeyCollisionException : Exception
{
    public StorageKeyCollisionException(string key) : base($"Storage key '{key}' is already occupied.")
    {
    }
}

public sealed class ChunkOffsetMismatchException : Exception
{
    public long ExpectedOffset { get; }

    public ChunkOffsetMismatchException(long expectedOffset)
        : base($"Chunk must start at offset {expectedOffset} (the next byte after what has already been durably written).")
    {
        ExpectedOffset = expectedOffset;
    }
}

public interface IFileStorage
{
    Task<StagedFile> StageAsync(Stream source, long maxSizeBytes, CancellationToken cancellationToken);

    Task CommitAsync(string tempPath, string key, DateTime createdAtUtc, CancellationToken cancellationToken);

    Task DiscardStagedAsync(string tempPath, CancellationToken cancellationToken);

    string? ResolveExistingContentPath(string key, DateTime createdAtUtc);

    Task<bool> DeleteContentAsync(string key, DateTime createdAtUtc, CancellationToken cancellationToken);

    Task<FileSystemProbeResult> CheckReadWriteAsync(CancellationToken cancellationToken);

    string GetUploadSessionTempPath(Guid sessionId);

    Task<long> AppendChunkAsync(string tempPath, long expectedOffset, Stream chunkData, long maxTotalBytes, CancellationToken cancellationToken);

    Task<string> ComputeChecksumAsync(string tempPath, CancellationToken cancellationToken);
}
