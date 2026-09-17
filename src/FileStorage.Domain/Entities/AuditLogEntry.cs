namespace FileStorage.Domain.Entities;

public sealed class AuditLogEntry
{
    public Guid Id { get; private set; }

    public string ActorUserId { get; private set; } = default!;

    public string ActorRole { get; private set; } = default!;

    public string Operation { get; private set; } = default!;

    public string? ResourceId { get; private set; }

    public string? ResourceType { get; private set; }

    public string Outcome { get; private set; } = default!;

    public string? Detail { get; private set; }

    public string CorrelationId { get; private set; } = default!;

    public DateTime TimestampUtc { get; private set; }

    private AuditLogEntry()
    {
    }

    public static AuditLogEntry Create(
        string actorUserId,
        string actorRole,
        string operation,
        string? resourceId,
        string? resourceType,
        string outcome,
        string? detail,
        string correlationId,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
        }

        if (string.IsNullOrWhiteSpace(operation))
        {
            throw new ArgumentException("Operation is required.", nameof(operation));
        }

        if (string.IsNullOrWhiteSpace(outcome))
        {
            throw new ArgumentException("Outcome is required.", nameof(outcome));
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("CorrelationId is required.", nameof(correlationId));
        }

        return new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            ActorRole = actorRole,
            Operation = operation,
            ResourceId = resourceId,
            ResourceType = resourceType,
            Outcome = outcome,
            Detail = detail,
            CorrelationId = correlationId,
            TimestampUtc = nowUtc,
        };
    }
}
