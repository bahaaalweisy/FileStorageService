using FileStorage.Application.Files.Dtos;
using FileStorage.Domain.Entities;

namespace FileStorage.Application.Abstractions;

public interface IStoredObjectRepository
{
    Task AddAsync(StoredObject storedObject, CancellationToken cancellationToken);

    Task<StoredObject?> FindActiveAsync(Guid id, CancellationToken cancellationToken);

    Task<StoredObject?> FindAnyAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<StoredObject>> ListAsync(ListFilesQuery query, CancellationToken cancellationToken);

    void Remove(StoredObject storedObject);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<StoredObjectKeyInfo> StreamAllKeysAsync(CancellationToken cancellationToken);
}

public sealed record StoredObjectKeyInfo(Guid Id, string Key, DateTime CreatedAtUtc, bool IsSoftDeleted);
