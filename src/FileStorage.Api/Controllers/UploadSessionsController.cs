using FileStorage.Api.Contracts;
using FileStorage.Api.Files;
using FileStorage.Application.Abstractions;
using FileStorage.Application.Uploads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FileStorage.Api.Controllers;

[ApiController]
[Route("api/upload-sessions")]
[Authorize]
public sealed class UploadSessionsController : ControllerBase
{
    private readonly ResumableUploadService _service;
    private readonly ICurrentUser _currentUser;

    public UploadSessionsController(ResumableUploadService service, ICurrentUser currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpPost]
    [ProducesResponseType(typeof(UploadSessionStatusResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<UploadSessionStatusResponse>> Create([FromBody] FileStorage.Api.Contracts.CreateUploadSessionRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateSessionAsync(
            new FileStorage.Application.Uploads.CreateUploadSessionRequest(request.FileName, request.ContentType, request.TotalSizeBytes, request.Tags),
            _currentUser.UserId,
            cancellationToken);

        var response = ToResponse(result);
        return CreatedAtAction(nameof(GetStatus), new { id = response.SessionId }, response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(UploadSessionStatusResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<UploadSessionStatusResponse>> GetStatus(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.GetStatusAsync(id, _currentUser.UserId, _currentUser.IsAdmin, cancellationToken);
        return Ok(ToResponse(result));
    }

    [HttpPut("{id:guid}/chunks")]
    [ProducesResponseType(typeof(UploadSessionStatusResponse), StatusCodes.Status200OK)]
    [RequestSizeLimit(MultipartHeaderLimits.MaxChunkRequestBodyBytes)]
    public async Task<ActionResult<UploadSessionStatusResponse>> AppendChunk(Guid id, [FromQuery] long offset, CancellationToken cancellationToken)
    {
        if (offset < 0)
        {
            return ValidationProblem(detail: "offset must be zero or positive.");
        }

        var declaredLength = Request.ContentLength ?? 0;
        var result = await _service.AppendChunkAsync(id, offset, Request.Body, declaredLength, _currentUser.UserId, cancellationToken);
        return Ok(ToResponse(result));
    }

    [HttpPost("{id:guid}/finalize")]
    [ProducesResponseType(typeof(StoredObjectResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StoredObjectResponse>> Finalize(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.FinalizeAsync(id, _currentUser.UserId, cancellationToken);
        return Ok(FileResponseMapper.ToResponse(result));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Abort(Guid id, CancellationToken cancellationToken)
    {
        await _service.AbortAsync(id, _currentUser.UserId, cancellationToken);
        return NoContent();
    }

    private static UploadSessionStatusResponse ToResponse(UploadSessionStatusDto dto) => new(
        dto.SessionId, dto.Status, dto.TotalSizeBytes, dto.ReceivedBytes, dto.NextExpectedOffset, dto.ExpiresAtUtc, dto.FinalizedFileId);
}

internal static class MultipartHeaderLimits
{

    public const long MaxChunkRequestBodyBytes = 32L * 1024 * 1024;
}
