using System.Text.RegularExpressions;

namespace FileStorage.Api.Middleware;

public static partial class CorrelationId
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemsKey = "CorrelationId";
    private const int MaxLength = 128;

    [GeneratedRegex("^[A-Za-z0-9_-]{1,128}$")]
    private static partial Regex ValidPattern();

    public static bool IsValid(string value) => value.Length <= MaxLength && ValidPattern().IsMatch(value);
}

public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[CorrelationId.HeaderName].FirstOrDefault();
        var correlationId = !string.IsNullOrWhiteSpace(incoming) && CorrelationId.IsValid(incoming)
            ? incoming
            : Guid.NewGuid().ToString("N");

        context.Items[CorrelationId.ItemsKey] = correlationId;
        context.Response.Headers[CorrelationId.HeaderName] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }
}
