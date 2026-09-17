using FileStorage.Application.Audit;
using FileStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FileStorage.Infrastructure.Audit;

public sealed class AuditLogReader : IAuditLogReader
{
    private readonly AppDbContext _db;

    public AuditLogReader(AppDbContext db)
    {
        _db = db;
    }

    public async Task<AuditLogPage> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, 200);

        var q = _db.AuditLogEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.ActorUserId))
        {
            q = q.Where(x => x.ActorUserId == query.ActorUserId);
        }

        if (!string.IsNullOrWhiteSpace(query.Operation))
        {
            q = q.Where(x => x.Operation == query.Operation);
        }

        if (!string.IsNullOrWhiteSpace(query.ResourceId))
        {
            q = q.Where(x => x.ResourceId == query.ResourceId);
        }

        if (query.FromUtc.HasValue)
        {
            q = q.Where(x => x.TimestampUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            q = q.Where(x => x.TimestampUtc <= query.ToUtc.Value);
        }

        var total = await q.LongCountAsync(cancellationToken);

        var items = await q
            .OrderByDescending(x => x.TimestampUtc)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new AuditLogPage(items, page, pageSize, total);
    }
}
