namespace FileStorage.Application.Abstractions;

public sealed record StoredContentEntry(string Key, DateTime CreatedAtUtc, DateTime LastWriteTimeUtc, long SizeBytes);

public interface IStorageInventory
{
    IAsyncEnumerable<StoredContentEntry> EnumerateContentAsync(CancellationToken cancellationToken);
}
