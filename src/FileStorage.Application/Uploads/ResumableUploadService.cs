using FileStorage.Application.Abstractions;
using FileStorage.Application.Common;
using FileStorage.Application.Exceptions;
using FileStorage.Application.Files.Dtos;
using FileStorage.Application.Files.UseCases;
using FileStorage.Application.Options;
using FileStorage.Domain.Entities;
using FileStorage.Domain.Enums;
using FileStorage.Domain.Exceptions;
using FileStorage.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileStorage.Application.Uploads;

public sealed record CreateUploadSessionRequest(string FileName, string ContentType, long TotalSizeBytes, IReadOnlyList<string>? Tags);

public sealed record UploadSessionStatusDto(
    Guid SessionId,
    string Status,
    long TotalSizeBytes,
    long ReceivedBytes,
    long NextExpectedOffset,
    DateTime ExpiresAtUtc,
    Guid? FinalizedFileId);

public sealed class ResumableUploadService
{
    private readonly IUploadSessionRepository _sessions;
    private readonly IStoredObjectRepository _storedObjects;
    private readonly IFileStorage _fileStorage;
    private readonly IStorageKeyGenerator _keyGenerator;
    private readonly IClock _clock;
    private readonly UploadPolicyOptions _uploadPolicy;
    private readonly ResumableUploadOptions _resumablePolicy;
    private readonly ILogger<ResumableUploadService> _logger;
    private readonly IAuditLogWriter _auditLog;

    public ResumableUploadService(
        IUploadSessionRepository sessions,
        IStoredObjectRepository storedObjects,
        IFileStorage fileStorage,
        IStorageKeyGenerator keyGenerator,
        IClock clock,
        IOptions<UploadPolicyOptions> uploadPolicy,
        IOptions<ResumableUploadOptions> resumablePolicy,
        ILogger<ResumableUploadService> logger,
        IAuditLogWriter auditLog)
    {
        _sessions = sessions;
        _storedObjects = storedObjects;
        _fileStorage = fileStorage;
        _keyGenerator = keyGenerator;
        _clock = clock;
        _uploadPolicy = uploadPolicy.Value;
        _resumablePolicy = resumablePolicy.Value;
        _logger = logger;
        _auditLog = auditLog;
    }

    public async Task<UploadSessionStatusDto> CreateSessionAsync(CreateUploadSessionRequest request, string userId, CancellationToken cancellationToken)
    {
        var sanitizedName = Common.FilenameSanitizer.Sanitize(request.FileName, _uploadPolicy.MaxOriginalFilenameLength);
        ValidateExtension(sanitizedName);

        if (request.TotalSizeBytes <= 0)
        {
            throw new ValidationAppException("totalSizeBytes", "Must be a positive number of bytes.");
        }

        if (request.TotalSizeBytes > _uploadPolicy.MaxFileSizeBytes)
        {
            throw new PayloadTooLargeAppException(
                $"Declared total size {request.TotalSizeBytes} exceeds the configured maximum of {_uploadPolicy.MaxFileSizeBytes} bytes.");
        }

        var tagSet = TagSet.FromInput(request.Tags);
        var nowUtc = _clock.UtcNow;

        var session = UploadSession.Create(
            userId,
            sanitizedName,
            string.IsNullOrWhiteSpace(request.ContentType) ? "application/octet-stream" : request.ContentType,
            tagSet.ToStorageString(),
            request.TotalSizeBytes,
            nowUtc,
            TimeSpan.FromHours(_resumablePolicy.SessionTimeToLiveHours));

        await _sessions.AddAsync(session, cancellationToken);
        await _sessions.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Upload session created. SessionId={SessionId} TotalSizeBytes={TotalSizeBytes}", session.Id, session.TotalSizeBytes);

        return ToStatusDto(session);
    }

    public async Task<UploadSessionStatusDto> AppendChunkAsync(Guid sessionId, long offset, Stream chunkData, long chunkLength, string userId, CancellationToken cancellationToken)
    {
        var session = await GetOwnedUsableSessionAsync(sessionId, userId, cancellationToken);

        if (chunkLength > _resumablePolicy.MaxChunkSizeBytes)
        {
            throw new PayloadTooLargeAppException(
                $"Chunk of {chunkLength} bytes exceeds the configured maximum chunk size of {_resumablePolicy.MaxChunkSizeBytes} bytes.");
        }

        if (offset > session.TotalSizeBytes)
        {
            throw new ValidationAppException("offset", "Offset cannot exceed the session's declared total size.");
        }

        var tempPath = _fileStorage.GetUploadSessionTempPath(sessionId);
        long newReceivedBytes;
        try
        {
            newReceivedBytes = await _fileStorage.AppendChunkAsync(tempPath, offset, chunkData, session.TotalSizeBytes, cancellationToken);
        }
        catch (ChunkOffsetMismatchException ex)
        {
            throw new ConflictAppException(
                $"Chunk offset mismatch: expected {ex.ExpectedOffset}, got {offset}. Query GET the session and resume from nextExpectedOffset.");
        }

        session.RecordChunkAppended(newReceivedBytes, _clock.UtcNow);
        await _sessions.SaveChangesAsync(cancellationToken);

        return ToStatusDto(session);
    }

    public async Task<UploadSessionStatusDto> GetStatusAsync(Guid sessionId, string userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var session = await _sessions.FindAsync(sessionId, cancellationToken)
            ?? throw new NotFoundAppException(nameof(UploadSession), sessionId);

        if (!session.IsOwnedBy(userId) && !isAdmin)
        {
            throw new NotFoundAppException(nameof(UploadSession), sessionId);
        }

        return ToStatusDto(session);
    }

    public async Task<StoredObjectDto> FinalizeAsync(Guid sessionId, string userId, CancellationToken cancellationToken)
    {
        var session = await GetOwnedUsableSessionAsync(sessionId, userId, cancellationToken);

        if (!session.IsComplete)
        {
            throw new ConflictAppException(
                $"Session has {session.ReceivedBytes}/{session.TotalSizeBytes} bytes; finalize requires all declared bytes to be received.");
        }

        var tempPath = _fileStorage.GetUploadSessionTempPath(sessionId);
        var checksum = await _fileStorage.ComputeChecksumAsync(tempPath, cancellationToken);
        var nowUtc = _clock.UtcNow;

        var key = await CommitWithCollisionRetryAsync(tempPath, nowUtc, cancellationToken);

        var entity = StoredObject.Create(
            key,
            session.OriginalFileName,
            session.TotalSizeBytes,
            session.ContentType,
            checksum,
            TagSet.FromStorage(session.Tags),
            userId,
            nowUtc);

        try
        {
            await _storedObjects.AddAsync(entity, cancellationToken);
            await _storedObjects.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Metadata persistence failed after committing resumable-upload content for key {Key}; deleting orphaned file", key);
            await _fileStorage.DeleteContentAsync(key, nowUtc, CancellationToken.None);
            throw;
        }

        session.MarkCompleted(entity.Id, nowUtc);
        await _sessions.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Upload session finalized. SessionId={SessionId} FileId={FileId} Bytes={Bytes}", sessionId, entity.Id, entity.SizeBytes);

        await _auditLog.RecordAsync(
            operation: "ResumableUploadFinalize",
            resourceId: entity.Id.ToString(),
            resourceType: "StoredObject",
            outcome: AuditOutcome.Success,
            detail: $"SessionId={sessionId}, {entity.SizeBytes} bytes",
            cancellationToken);

        return UploadFileService.Map(entity);
    }

    public async Task AbortAsync(Guid sessionId, string userId, CancellationToken cancellationToken)
    {
        var session = await _sessions.FindAsync(sessionId, cancellationToken)
            ?? throw new NotFoundAppException(nameof(UploadSession), sessionId);

        if (!session.IsOwnedBy(userId))
        {
            throw new NotFoundAppException(nameof(UploadSession), sessionId);
        }

        if (session.Status != UploadSessionStatus.InProgress)
        {

            return;
        }

        var tempPath = _fileStorage.GetUploadSessionTempPath(sessionId);
        await _fileStorage.DiscardStagedAsync(tempPath, cancellationToken);

        session.MarkAborted(_clock.UtcNow);
        await _sessions.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Upload session aborted. SessionId={SessionId}", sessionId);
    }

    public async Task<int> CleanupExpiredSessionsAsync(int maxBatchSize, CancellationToken cancellationToken)
    {
        var nowUtc = _clock.UtcNow;
        var expired = await _sessions.FindExpiredInProgressAsync(nowUtc, maxBatchSize, cancellationToken);

        foreach (var session in expired)
        {
            var tempPath = _fileStorage.GetUploadSessionTempPath(session.Id);
            await _fileStorage.DiscardStagedAsync(tempPath, cancellationToken);
            session.MarkExpired(nowUtc);
        }

        if (expired.Count > 0)
        {
            await _sessions.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Expired {Count} stale upload session(s).", expired.Count);
        }

        return expired.Count;
    }

    private async Task<UploadSession> GetOwnedUsableSessionAsync(Guid sessionId, string userId, CancellationToken cancellationToken)
    {
        var session = await _sessions.FindAsync(sessionId, cancellationToken)
            ?? throw new NotFoundAppException(nameof(UploadSession), sessionId);

        if (!session.IsOwnedBy(userId))
        {
            throw new NotFoundAppException(nameof(UploadSession), sessionId);
        }

        try
        {
            session.EnsureUsable(_clock.UtcNow);
        }
        catch (UploadSessionNotUsableException ex)
        {
            throw new ConflictAppException(ex.Message);
        }

        return session;
    }

    private async Task<string> CommitWithCollisionRetryAsync(string tempPath, DateTime nowUtc, CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var key = _keyGenerator.Generate();
            try
            {
                await _fileStorage.CommitAsync(tempPath, key, nowUtc, cancellationToken);
                return key;
            }
            catch (StorageKeyCollisionException) when (attempt < maxAttempts)
            {
                _logger.LogWarning("Storage key collision on attempt {Attempt}/{MaxAttempts} finalizing resumable upload; retrying", attempt, maxAttempts);
            }
        }

        throw new InvalidOperationException($"Unable to allocate a unique storage key after {maxAttempts} attempts.");
    }

    private void ValidateExtension(string sanitizedName)
    {
        if (_uploadPolicy.AllowedExtensions.Length == 0)
        {
            return;
        }

        var ext = Path.GetExtension(sanitizedName).ToLowerInvariant();
        if (!_uploadPolicy.AllowedExtensions.Contains(ext))
        {
            throw new UnsupportedMediaTypeAppException($"File extension '{ext}' is not permitted by upload policy.");
        }
    }

    private static UploadSessionStatusDto ToStatusDto(UploadSession session) => new(
        session.Id,
        session.Status,
        session.TotalSizeBytes,
        session.ReceivedBytes,
        session.ReceivedBytes,
        session.ExpiresAtUtc,
        session.FinalizedFileId);
}
