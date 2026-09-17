using FileStorage.Domain.Entities;

namespace FileStorage.Application.Audit;

public sealed record AuditLogQuery(
    string? ActorUserId,
    string? Operation,
    string? ResourceId,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int Page,
    int PageSize);

public sealed record AuditLogPage(IReadOnlyList<AuditLogEntry> Items, int Page, int PageSize, long TotalCount);

public interface IAuditLogReader
{
    Task<AuditLogPage> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken);
}
