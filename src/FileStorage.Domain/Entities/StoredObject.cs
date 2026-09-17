using FileStorage.Domain.Enums;
using FileStorage.Domain.Exceptions;
using FileStorage.Domain.ValueObjects;

namespace FileStorage.Domain.Entities;

public class StoredObject
{
    public Guid Id { get; private set; }

    public string Key { get; private set; } = default!;

    public string OriginalName { get; private set; } = default!;

    public long SizeBytes { get; private set; }

    public string ContentType { get; private set; } = default!;

    public string Checksum { get; private set; } = default!;

    public string? Tags { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? DeletedAtUtc { get; private set; }

    public int Version { get; private set; }

    public string CreatedByUserId { get; private set; } = default!;

    private StoredObject()
    {
    }

    public static StoredObject Create(
        string key,
        string originalName,
        long sizeBytes,
        string contentType,
        string checksum,
        TagSet tags,
        string createdByUserId,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key is required.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(originalName))
        {
            throw new ArgumentException("OriginalName is required.", nameof(originalName));
        }

        if (sizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        }

        if (string.IsNullOrWhiteSpace(createdByUserId))
        {
            throw new ArgumentException("CreatedByUserId is required.", nameof(createdByUserId));
        }

        return new StoredObject
        {
            Id = Guid.NewGuid(),
            Key = key,
            OriginalName = originalName,
            SizeBytes = sizeBytes,
            ContentType = contentType,
            Checksum = checksum,
            Tags = tags.ToStorageString(),
            CreatedAtUtc = nowUtc,
            DeletedAtUtc = null,
            Version = 1,
            CreatedByUserId = createdByUserId,
        };
    }

    public bool IsDeleted => DeletedAtUtc.HasValue;

    public bool IsOwnedBy(string userId) => string.Equals(CreatedByUserId, userId, StringComparison.Ordinal);

    public bool CanBeAccessedBy(string userId, bool isAdmin) => isAdmin || IsOwnedBy(userId);

    public void SoftDelete(DateTime nowUtc)
    {
        if (IsDeleted)
        {
            throw new ObjectAlreadyDeletedException(Id);
        }

        DeletedAtUtc = nowUtc;
    }

    public bool CanBeHardDeleted => IsDeleted;
}
