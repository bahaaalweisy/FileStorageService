using FileStorage.Application.Abstractions;
using FileStorage.Application.Common;
using FileStorage.Application.Exceptions;
using FileStorage.Application.Files.Dtos;
using FileStorage.Application.Options;
using FileStorage.Domain.Entities;
using FileStorage.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileStorage.Application.Files.UseCases;

public sealed record StagedUpload(
    string TempPath,
    string SanitizedFileName,
    string ContentType,
    long SizeBytes,
    string ChecksumSha256Hex,
    DateTime StagedAtUtc);

public sealed class UploadFileService
{
    private const int MaxKeyCollisionAttempts = 5;

    private readonly IFileStorage _fileStorage;
    private readonly IStoredObjectRepository _repository;
    private readonly IStorageKeyGenerator _keyGenerator;
    private readonly IClock _clock;
    private readonly UploadPolicyOptions _policy;
    private readonly ILogger<UploadFileService> _logger;

    public UploadFileService(
        IFileStorage fileStorage,
        IStoredObjectRepository repository,
        IStorageKeyGenerator keyGenerator,
        IClock clock,
        IOptions<UploadPolicyOptions> policy,
        ILogger<UploadFileService> logger)
    {
        _fileStorage = fileStorage;
        _repository = repository;
        _keyGenerator = keyGenerator;
        _clock = clock;
        _policy = policy.Value;
        _logger = logger;
    }

    public async Task<StagedUpload> StageAsync(string originalFileName, string contentType, Stream content, CancellationToken cancellationToken)
    {
        var sanitizedName = FilenameSanitizer.Sanitize(originalFileName, _policy.MaxOriginalFilenameLength);
        ValidateExtension(sanitizedName);

        var nowUtc = _clock.UtcNow;

        StagedFile staged;
        try
        {
            staged = await _fileStorage.StageAsync(content, _policy.MaxFileSizeBytes, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Upload cancelled by client while staging {FileName}", sanitizedName);
            throw;
        }
        catch (PayloadTooLargeAppException)
        {
            _logger.LogWarning("Upload rejected for exceeding size policy: {FileName}", sanitizedName);
            throw;
        }

        return new StagedUpload(
            staged.TempPath,
            sanitizedName,
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            staged.SizeBytes,
            staged.ChecksumSha256Hex,
            nowUtc);
    }

    public async Task<StoredObjectDto> FinalizeAsync(StagedUpload staged, IReadOnlyList<string> rawTags, string userId, CancellationToken cancellationToken)
    {
        var tagSet = TagSet.FromInput(rawTags);
        var startedAt = _clock.UtcNow;

        var key = await CommitWithCollisionRetryAsync(staged, cancellationToken);

        var entity = StoredObject.Create(
            key,
            staged.SanitizedFileName,
            staged.SizeBytes,
            staged.ContentType,
            staged.ChecksumSha256Hex,
            tagSet,
            userId,
            staged.StagedAtUtc);

        try
        {
            await _repository.AddAsync(entity, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Upload cancelled by client after committing content for key {Key}; deleting orphaned file", key);
            await _fileStorage.DeleteContentAsync(key, staged.StagedAtUtc, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Metadata persistence failed after committing content for key {Key}; deleting orphaned file", key);
            await _fileStorage.DeleteContentAsync(key, staged.StagedAtUtc, CancellationToken.None);
            throw;
        }

        var durationMs = (_clock.UtcNow - startedAt).TotalMilliseconds;
        _logger.LogInformation(
            "Upload completed. FileId={FileId} Bytes={Bytes} DurationMs={DurationMs}",
            entity.Id, entity.SizeBytes, durationMs);

        return Map(entity);
    }

    public Task DiscardStagedAsync(StagedUpload staged, CancellationToken cancellationToken) =>
        _fileStorage.DiscardStagedAsync(staged.TempPath, cancellationToken);

    private async Task<string> CommitWithCollisionRetryAsync(StagedUpload staged, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxKeyCollisionAttempts; attempt++)
        {
            var key = _keyGenerator.Generate();
            try
            {
                await _fileStorage.CommitAsync(staged.TempPath, key, staged.StagedAtUtc, cancellationToken);
                return key;
            }
            catch (StorageKeyCollisionException) when (attempt < MaxKeyCollisionAttempts)
            {
                _logger.LogWarning(
                    "Storage key collision on attempt {Attempt}/{MaxAttempts} for key {Key}; retrying with a new key",
                    attempt, MaxKeyCollisionAttempts, key);
            }
        }

        throw new InvalidOperationException($"Unable to allocate a unique storage key after {MaxKeyCollisionAttempts} attempts.");
    }

    private void ValidateExtension(string sanitizedName)
    {
        if (_policy.AllowedExtensions.Length == 0)
        {
            return;
        }

        var ext = Path.GetExtension(sanitizedName).ToLowerInvariant();
        if (!_policy.AllowedExtensions.Contains(ext))
        {
            throw new UnsupportedMediaTypeAppException($"File extension '{ext}' is not permitted by upload policy.");
        }
    }

    internal static StoredObjectDto Map(StoredObject entity) => new(
        entity.Id,
        entity.Key,
        entity.OriginalName,
        entity.SizeBytes,
        entity.ContentType,
        entity.Checksum,
        TagSet.FromStorage(entity.Tags).Values,
        entity.CreatedAtUtc,
        entity.DeletedAtUtc,
        entity.Version,
        entity.CreatedByUserId);
}
