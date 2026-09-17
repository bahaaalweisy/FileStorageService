namespace FileStorage.Application.Files.Dtos;

public sealed record StoredObjectDto(
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

public sealed record ListFilesQuery(
    string? Name,
    string? Tag,
    string? ContentType,
    DateTime? CreatedFromUtc,
    DateTime? CreatedToUtc,
    int Page,
    int PageSize,
    string RequestingUserId,
    bool IsAdmin,
    bool OnlyDeleted = false);

public sealed record DownloadInfoDto(
    string PhysicalPath,
    string ContentType,
    string SanitizedFileName,
    long SizeBytes,
    string Checksum,
    DateTime CreatedAtUtc);

public sealed record PreviewInfoDto(
    bool Supported,
    string? PhysicalPath,
    string? ContentType,
    long SizeBytes,
    string Reason,
    string? Checksum = null,
    DateTime? CreatedAtUtc = null);
