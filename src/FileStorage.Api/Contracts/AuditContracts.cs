namespace FileStorage.Api.Contracts;

public sealed record AuditLogEntryResponse(
    Guid Id,
    string ActorUserId,
    string ActorRole,
    string Operation,
    string? ResourceId,
    string? ResourceType,
    string Outcome,
    string? Detail,
    string CorrelationId,
    DateTime TimestampUtc);

public sealed record AuditLogListResponse(
    IReadOnlyList<AuditLogEntryResponse> Items,
    int Page,
    int PageSize,
    long TotalCount);
