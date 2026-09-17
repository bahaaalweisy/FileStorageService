namespace FileStorage.Api.Contracts;

public sealed record StoredObjectResponse(
    Guid Id,
    string Key,
    string OriginalName,
    long SizeBytes,
    string ContentType,
    string Checksum,
    IReadOnlyList<string> Tags,
    DateTime CreatedAtUtc,
    DateTime? DeletedAtUtc,
    int Version,
    string CreatedByUserId);

public sealed record FileListResponse(
    IReadOnlyList<StoredObjectResponse> Items,
    int Page,
    int PageSize,
    long TotalCount);

public sealed record PreviewUnsupportedResponse(bool Supported, string Reason);
