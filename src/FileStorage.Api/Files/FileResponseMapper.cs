using FileStorage.Api.Contracts;
using FileStorage.Application.Files.Dtos;

namespace FileStorage.Api.Files;

public static class FileResponseMapper
{
    public static StoredObjectResponse ToResponse(StoredObjectDto dto) => new(
        dto.Id, dto.Key, dto.OriginalName, dto.SizeBytes, dto.ContentType, dto.Checksum,
        dto.Tags, dto.CreatedAtUtc, dto.DeletedAtUtc, dto.Version, dto.CreatedByUserId);
}
