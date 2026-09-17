using FileStorage.Api.Auth;
using FileStorage.Api.Contracts;
using FileStorage.Api.Files;
using FileStorage.Application.Abstractions;
using FileStorage.Application.Files.Dtos;
using FileStorage.Application.Files.UseCases;
using FileStorage.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace FileStorage.Api.Controllers;

[ApiController]
[Route("api/files")]
[Authorize]
public sealed class FilesController : ControllerBase
{
    private readonly ListFilesService _listFilesService;
    private readonly FileAccessService _fileAccessService;
    private readonly DeleteFileService _deleteFileService;
    private readonly ICurrentUser _currentUser;

    public FilesController(
        ListFilesService listFilesService,
        FileAccessService fileAccessService,
        DeleteFileService deleteFileService,
        ICurrentUser currentUser)
    {
        _listFilesService = listFilesService;
        _fileAccessService = fileAccessService;
        _deleteFileService = deleteFileService;
        _currentUser = currentUser;
    }

    private static DateTime? ToUtcOrNull(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;

    [HttpGet]
    [ProducesResponseType(typeof(FileListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<FileListResponse>> List(
        [FromQuery] string? name,
        [FromQuery] string? tag,
        [FromQuery] string? contentType,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new ListFilesQuery(
            name, tag, contentType,
            ToUtcOrNull(from), ToUtcOrNull(to),
            page, pageSize,
            _currentUser.UserId, _currentUser.IsAdmin,
            OnlyDeleted: false);

        var result = await _listFilesService.ListAsync(query, cancellationToken);
        return Ok(new FileListResponse(result.Items.Select(FileResponseMapper.ToResponse).ToList(), result.Page, result.PageSize, result.TotalCount));
    }

    [HttpGet("deleted")]
    [Authorize(Roles = UserRole.Admin)]
    [ProducesResponseType(typeof(FileListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<FileListResponse>> ListDeleted(
        [FromQuery] string? name,
        [FromQuery] string? tag,
        [FromQuery] string? contentType,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new ListFilesQuery(
            name, tag, contentType,
            ToUtcOrNull(from), ToUtcOrNull(to),
            page, pageSize,
            _currentUser.UserId, _currentUser.IsAdmin,
            OnlyDeleted: true);

        var result = await _listFilesService.ListAsync(query, cancellationToken);
        return Ok(new FileListResponse(result.Items.Select(FileResponseMapper.ToResponse).ToList(), result.Page, result.PageSize, result.TotalCount));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(StoredObjectResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StoredObjectResponse>> GetMetadata(Guid id, CancellationToken cancellationToken)
    {
        var dto = await _fileAccessService.GetMetadataAsync(id, _currentUser.UserId, _currentUser.IsAdmin, cancellationToken);
        return Ok(FileResponseMapper.ToResponse(dto));
    }

    [HttpGet("{id:guid}/download")]
    [HttpHead("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var info = await _fileAccessService.GetDownloadInfoAsync(id, _currentUser.UserId, _currentUser.IsAdmin, cancellationToken);

        Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";

        Response.Headers[HeaderNames.CacheControl] = "private, no-cache";

        return new PhysicalFileResult(info.PhysicalPath, info.ContentType)
        {
            FileDownloadName = info.SanitizedFileName,
            EnableRangeProcessing = true,

            EntityTag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{info.Checksum}\""),
            LastModified = new DateTimeOffset(DateTime.SpecifyKind(info.CreatedAtUtc, DateTimeKind.Utc)),
        };
    }

    [HttpGet("{id:guid}/preview")]
    public async Task<IActionResult> Preview(Guid id, CancellationToken cancellationToken)
    {
        var info = await _fileAccessService.GetPreviewInfoAsync(id, _currentUser.UserId, _currentUser.IsAdmin, cancellationToken);

        if (!info.Supported)
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new PreviewUnsupportedResponse(false, info.Reason));
        }

        Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        Response.Headers[HeaderNames.ContentDisposition] = "inline";
        Response.Headers[HeaderNames.CacheControl] = "private, no-cache";

        return new PhysicalFileResult(info.PhysicalPath!, info.ContentType!)
        {
            EnableRangeProcessing = true,
            EntityTag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{info.Checksum}\""),
            LastModified = new DateTimeOffset(DateTime.SpecifyKind(info.CreatedAtUtc!.Value, DateTimeKind.Utc)),
        };
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken cancellationToken)
    {
        await _deleteFileService.SoftDeleteAsync(id, _currentUser.UserId, _currentUser.IsAdmin, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}/hard")]
    [Authorize(Roles = UserRole.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> HardDelete(Guid id, CancellationToken cancellationToken)
    {
        await _deleteFileService.HardDeleteAsync(id, _currentUser.UserId, _currentUser.IsAdmin, cancellationToken);
        return NoContent();
    }
}
