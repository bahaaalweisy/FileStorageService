using FileStorage.Application.Abstractions;
using FileStorage.Domain.Entities;
using FileStorage.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace FileStorage.Infrastructure.Audit;

public sealed class AuditLogWriter : IAuditLogWriter
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;
    private readonly IClock _clock;
    private readonly ILogger<AuditLogWriter> _logger;

    public AuditLogWriter(
        AppDbContext db,
        ICurrentUser currentUser,
        ICorrelationIdAccessor correlationIdAccessor,
        IClock clock,
        ILogger<AuditLogWriter> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _correlationIdAccessor = correlationIdAccessor;
        _clock = clock;
        _logger = logger;
    }

    public async Task RecordAsync(
        string operation,
        string? resourceId,
        string? resourceType,
        string outcome,
        string? detail,
        CancellationToken cancellationToken)
    {
        try
        {
            var entry = AuditLogEntry.Create(
                actorUserId: _currentUser.UserId,
                actorRole: _currentUser.Role,
                operation: operation,
                resourceId: resourceId,
                resourceType: resourceType,
                outcome: outcome,
                detail: detail,
                correlationId: _correlationIdAccessor.CorrelationId,
                nowUtc: _clock.UtcNow);

            _db.AuditLogEntries.Add(entry);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to write audit log entry for operation {Operation} on resource {ResourceId}. " +
                "The underlying operation is NOT affected by this failure.", operation, resourceId);
        }
    }
}
