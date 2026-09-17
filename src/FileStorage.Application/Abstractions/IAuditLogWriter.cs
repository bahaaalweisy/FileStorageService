namespace FileStorage.Application.Abstractions;

public static class AuditOutcome
{
    public const string Success = "Success";
    public const string Failure = "Failure";
}

public interface IAuditLogWriter
{
    Task RecordAsync(
        string operation,
        string? resourceId,
        string? resourceType,
        string outcome,
        string? detail,
        CancellationToken cancellationToken);
}

public interface ICorrelationIdAccessor
{
    string CorrelationId { get; }
}
