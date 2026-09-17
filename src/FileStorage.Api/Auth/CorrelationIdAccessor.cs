using FileStorage.Application.Abstractions;

namespace FileStorage.Api.Auth;

public sealed class CorrelationIdAccessor : ICorrelationIdAccessor
{
    public string CorrelationId { get; }

    public CorrelationIdAccessor(IHttpContextAccessor accessor)
    {
        CorrelationId = accessor.HttpContext?.Items[FileStorage.Api.Middleware.CorrelationId.ItemsKey] as string ?? string.Empty;
    }
}
