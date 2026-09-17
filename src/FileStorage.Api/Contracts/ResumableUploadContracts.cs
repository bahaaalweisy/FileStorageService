namespace FileStorage.Api.Contracts;

public sealed record CreateUploadSessionRequest(string FileName, string ContentType, long TotalSizeBytes, string[]? Tags);

public sealed record UploadSessionStatusResponse(
    Guid SessionId,
    string Status,
    long TotalSizeBytes,
    long ReceivedBytes,
    long NextExpectedOffset,
    DateTime ExpiresAtUtc,
    Guid? FinalizedFileId);
