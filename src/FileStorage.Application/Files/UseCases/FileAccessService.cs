using FileStorage.Application.Abstractions;
using FileStorage.Application.Common;
using FileStorage.Application.Exceptions;
using FileStorage.Application.Files.Dtos;
using FileStorage.Application.Options;
using FileStorage.Domain.Entities;
using Microsoft.Extensions.Options;

namespace FileStorage.Application.Files.UseCases;

public sealed class FileAccessService
{
    private readonly IStoredObjectRepository _repository;
    private readonly IFileStorage _fileStorage;
    private readonly UploadPolicyOptions _policy;

    public FileAccessService(IStoredObjectRepository repository, IFileStorage fileStorage, IOptions<UploadPolicyOptions> policy)
    {
        _repository = repository;
        _fileStorage = fileStorage;
        _policy = policy.Value;
    }

    public async Task<DownloadInfoDto> GetDownloadInfoAsync(Guid id, string userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var entity = await GetAccessibleOrThrowAsync(id, userId, isAdmin, cancellationToken);
        var path = ResolvePathOrThrow(entity);

        var sanitizedName = FilenameSanitizer.Sanitize(entity.OriginalName, _policy.MaxOriginalFilenameLength);
        return new DownloadInfoDto(path, entity.ContentType, sanitizedName, entity.SizeBytes, entity.Checksum, entity.CreatedAtUtc);
    }

    public async Task<StoredObjectDto> GetMetadataAsync(Guid id, string userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var entity = await GetAccessibleOrThrowAsync(id, userId, isAdmin, cancellationToken);
        return UploadFileService.Map(entity);
    }

    public async Task<PreviewInfoDto> GetPreviewInfoAsync(Guid id, string userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var entity = await GetAccessibleOrThrowAsync(id, userId, isAdmin, cancellationToken);

        if (!_policy.PreviewAllowedContentTypes.Contains(entity.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            return new PreviewInfoDto(false, null, null, entity.SizeBytes, "Preview is not supported for this content type. Use download instead.");
        }

        var path = ResolvePathOrThrow(entity);
        return new PreviewInfoDto(true, path, entity.ContentType, entity.SizeBytes, "OK", entity.Checksum, entity.CreatedAtUtc);
    }

    private async Task<StoredObject> GetAccessibleOrThrowAsync(Guid id, string userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var entity = await _repository.FindActiveAsync(id, cancellationToken);

        if (entity is null || !entity.CanBeAccessedBy(userId, isAdmin))
        {
            throw new NotFoundAppException(nameof(StoredObject), id);
        }

        return entity;
    }

    private string ResolvePathOrThrow(StoredObject entity)
    {
        var path = _fileStorage.ResolveExistingContentPath(entity.Key, entity.CreatedAtUtc);
        if (path is null)
        {
            throw new NotFoundAppException("Content", entity.Key);
        }

        return path;
    }
}
