using FileStorage.Application.Abstractions;
using FileStorage.Application.Files.Dtos;
using FileStorage.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FileStorage.Infrastructure.Persistence;

public class StoredObjectRepository : IStoredObjectRepository
{
    private readonly AppDbContext _db;

    public StoredObjectRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(StoredObject storedObject, CancellationToken cancellationToken)
        => await _db.StoredObjects.AddAsync(storedObject, cancellationToken);

    public Task<StoredObject?> FindActiveAsync(Guid id, CancellationToken cancellationToken)
        => _db.StoredObjects.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAtUtc == null, cancellationToken);

    public Task<StoredObject?> FindAnyAsync(Guid id, CancellationToken cancellationToken)
        => _db.StoredObjects.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<PagedResult<StoredObject>> ListAsync(ListFilesQuery query, CancellationToken cancellationToken)
    {
        IQueryable<StoredObject> q = _db.StoredObjects.AsNoTracking();
        q = query.OnlyDeleted ? q.Where(x => x.DeletedAtUtc != null) : q.Where(x => x.DeletedAtUtc == null);

        if (!query.IsAdmin)
        {
            q = q.Where(x => x.CreatedByUserId == query.RequestingUserId);
        }

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            q = q.Where(x => EF.Functions.Like(x.OriginalName, $"%{query.Name}%"));
        }

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            q = q.Where(x => x.Tags != null && EF.Functions.Like(x.Tags, $"%{query.Tag}%"));
        }

        if (!string.IsNullOrWhiteSpace(query.ContentType))
        {
            q = q.Where(x => x.ContentType == query.ContentType);
        }

        if (query.CreatedFromUtc.HasValue)
        {
            q = q.Where(x => x.CreatedAtUtc >= query.CreatedFromUtc.Value);
        }

        if (query.CreatedToUtc.HasValue)
        {
            q = q.Where(x => x.CreatedAtUtc < query.CreatedToUtc.Value);
        }

        var totalCount = await q.LongCountAsync(cancellationToken);

        q = query.OnlyDeleted
            ? q.OrderByDescending(x => x.DeletedAtUtc).ThenByDescending(x => x.Id)
            : q.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id);

        var items = await q
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<StoredObject>(items, query.Page, query.PageSize, totalCount);
    }

    public void Remove(StoredObject storedObject) => _db.StoredObjects.Remove(storedObject);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _db.SaveChangesAsync(cancellationToken);

    public async IAsyncEnumerable<StoredObjectKeyInfo> StreamAllKeysAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var query = _db.StoredObjects
            .AsNoTracking()
            .Select(x => new StoredObjectKeyInfo(x.Id, x.Key, x.CreatedAtUtc, x.DeletedAtUtc != null))
            .AsAsyncEnumerable();

        await foreach (var item in query.WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }
}
