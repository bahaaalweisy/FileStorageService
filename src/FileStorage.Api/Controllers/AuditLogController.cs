using FileStorage.Api.Contracts;
using FileStorage.Application.Audit;
using FileStorage.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FileStorage.Api.Controllers;

[ApiController]
[Route("api/admin/audit-log")]
[Authorize(Roles = UserRole.Admin)]
public sealed class AuditLogController : ControllerBase
{
    private readonly IAuditLogReader _reader;

    public AuditLogController(IAuditLogReader reader)
    {
        _reader = reader;
    }

    [HttpGet]
    [ProducesResponseType(typeof(AuditLogListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuditLogListResponse>> List(
        [FromQuery] string? actorUserId,
        [FromQuery] string? operation,
        [FromQuery] string? resourceId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new AuditLogQuery(
            actorUserId,
            operation,
            resourceId,
            ToUtcOrNull(from),
            ToUtcOrNull(to),
            page,
            pageSize);

        var result = await _reader.QueryAsync(query, cancellationToken);

        var items = result.Items.Select(x => new AuditLogEntryResponse(
            x.Id, x.ActorUserId, x.ActorRole, x.Operation, x.ResourceId, x.ResourceType,
            x.Outcome, x.Detail, x.CorrelationId, x.TimestampUtc)).ToList();

        return Ok(new AuditLogListResponse(items, result.Page, result.PageSize, result.TotalCount));
    }

    private static DateTime? ToUtcOrNull(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
}
