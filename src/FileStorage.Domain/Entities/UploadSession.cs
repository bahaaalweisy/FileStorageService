using FileStorage.Domain.Enums;
using FileStorage.Domain.Exceptions;

namespace FileStorage.Domain.Entities;

public sealed class UploadSession
{
    public Guid Id { get; private set; }

    public string OwnerUserId { get; private set; } = default!;

    public string OriginalFileName { get; private set; } = default!;

    public string ContentType { get; private set; } = default!;

    public string? Tags { get; private set; }

    public long TotalSizeBytes { get; private set; }

    public long ReceivedBytes { get; private set; }

    public string Status { get; private set; } = default!;

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public Guid? FinalizedFileId { get; private set; }

    private UploadSession()
    {
    }

    public static UploadSession Create(
        string ownerUserId,
        string originalFileName,
        string contentType,
        string? tags,
        long totalSizeBytes,
        DateTime nowUtc,
        TimeSpan timeToLive)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            throw new ArgumentException("OwnerUserId is required.", nameof(ownerUserId));
        }

        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            throw new ArgumentException("OriginalFileName is required.", nameof(originalFileName));
        }

        if (totalSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalSizeBytes), "TotalSizeBytes must be positive.");
        }

        return new UploadSession
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            OriginalFileName = originalFileName,
            ContentType = contentType,
            Tags = tags,
            TotalSizeBytes = totalSizeBytes,
            ReceivedBytes = 0,
            Status = UploadSessionStatus.InProgress,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.Add(timeToLive),
        };
    }

    public bool IsOwnedBy(string userId) => string.Equals(OwnerUserId, userId, StringComparison.Ordinal);

    public bool IsExpired(DateTime nowUtc) => Status == UploadSessionStatus.InProgress && nowUtc >= ExpiresAtUtc;

    public void EnsureUsable(DateTime nowUtc)
    {
        if (Status != UploadSessionStatus.InProgress)
        {
            throw new UploadSessionNotUsableException(Id, Status);
        }

        if (IsExpired(nowUtc))
        {
            throw new UploadSessionNotUsableException(Id, UploadSessionStatus.Expired);
        }
    }

    public void RecordChunkAppended(long newReceivedBytes, DateTime nowUtc)
    {
        if (newReceivedBytes < ReceivedBytes)
        {
            throw new InvalidOperationException("ReceivedBytes must never move backwards.");
        }

        ReceivedBytes = newReceivedBytes;
        UpdatedAtUtc = nowUtc;
    }

    public bool IsComplete => ReceivedBytes == TotalSizeBytes;

    public void MarkCompleted(Guid finalizedFileId, DateTime nowUtc)
    {
        Status = UploadSessionStatus.Completed;
        FinalizedFileId = finalizedFileId;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkAborted(DateTime nowUtc)
    {
        Status = UploadSessionStatus.Aborted;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkExpired(DateTime nowUtc)
    {
        Status = UploadSessionStatus.Expired;
        UpdatedAtUtc = nowUtc;
    }
}
