using FileStorage.Application.Abstractions;
using FileStorage.Application.Exceptions;
using FileStorage.Domain.Entities;
using FileStorage.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace FileStorage.Application.Files.UseCases;

public sealed class DeleteFileService
{
    private readonly IStoredObjectRepository _repository;
    private readonly IFileStorage _fileStorage;
    private readonly IClock _clock;
    private readonly ILogger<DeleteFileService> _logger;
    private readonly IAuditLogWriter _auditLog;

    public DeleteFileService(IStoredObjectRepository repository, IFileStorage fileStorage, IClock clock, ILogger<DeleteFileService> logger, IAuditLogWriter auditLog)
    {
        _repository = repository;
        _fileStorage = fileStorage;
        _clock = clock;
        _logger = logger;
        _auditLog = auditLog;
    }

    public async Task SoftDeleteAsync(Guid id, string userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var entity = await _repository.FindAnyAsync(id, cancellationToken);
        if (entity is null || !entity.CanBeAccessedBy(userId, isAdmin))
        {
            throw new NotFoundAppException(nameof(StoredObject), id);
        }

        try
        {
            entity.SoftDelete(_clock.UtcNow);
        }
        catch (ObjectAlreadyDeletedException ex)
        {
            throw new ConflictAppException(ex.Message);
        }

        await _repository.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Soft delete completed. FileId={FileId} By={UserId}", id, userId);

        await _auditLog.RecordAsync("FileSoftDelete", id.ToString(), "StoredObject", AuditOutcome.Success, null, cancellationToken);
    }

    public async Task HardDeleteAsync(Guid id, string userId, bool isAdmin, CancellationToken cancellationToken)
    {
        if (!isAdmin)
        {
            throw new ForbiddenAppException("Hard delete is restricted to administrators.");
        }

        var entity = await _repository.FindAnyAsync(id, cancellationToken);
        if (entity is null)
        {
            throw new NotFoundAppException(nameof(StoredObject), id);
        }

        if (!entity.CanBeHardDeleted)
        {
            throw new ConflictAppException("File must be soft-deleted before it can be hard-deleted.");
        }

        await _fileStorage.DeleteContentAsync(entity.Key, entity.CreatedAtUtc, cancellationToken);

        _repository.Remove(entity);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Hard delete completed. FileId={FileId} By={UserId}", id, userId);

        await _auditLog.RecordAsync("FileHardDelete", id.ToString(), "StoredObject", AuditOutcome.Success, null, cancellationToken);
    }
}
