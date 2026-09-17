using FileStorage.Application.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace FileStorage.Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation("Request cancelled by client: {Path}", context.Request.Path);
        }
        catch (Exception ex)
        {
            await WriteProblemAsync(context, ex);
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception ex)
    {
        var (status, title) = Classify(ex);
        var correlationId = context.Items.TryGetValue(CorrelationId.ItemsKey, out var cid) ? cid?.ToString() : null;

        if (status >= 500)
        {
            _logger.LogError(ex, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning(ex, "Request failed with {Status}: {Method} {Path}", status, context.Request.Method, context.Request.Path);
        }

        var problem = new ProblemDetails
        {
            Type = $"https://httpstatuses.io/{status}",
            Title = title,
            Status = status,
            Instance = context.Request.Path,
            Detail = status < 500 ? ex.Message : "An unexpected error occurred.",
        };

        problem.Extensions["traceId"] = context.TraceIdentifier;
        problem.Extensions["correlationId"] = correlationId;

        if (ex is ValidationAppException validation)
        {
            problem.Extensions["errors"] = validation.Errors;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    }

    private static (int Status, string Title) Classify(Exception ex) => ex switch
    {
        ValidationAppException => (StatusCodes.Status400BadRequest, "Validation failed"),
        NotFoundAppException => (StatusCodes.Status404NotFound, "Resource not found"),
        ForbiddenAppException => (StatusCodes.Status403Forbidden, "Forbidden"),
        ConflictAppException => (StatusCodes.Status409Conflict, "Conflict"),
        PayloadTooLargeAppException => (StatusCodes.Status413PayloadTooLarge, "Payload too large"),
        UnsupportedMediaTypeAppException => (StatusCodes.Status415UnsupportedMediaType, "Unsupported media type"),
        BadHttpRequestException badHttp => (badHttp.StatusCode, "Bad request"),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred"),
    };
}
