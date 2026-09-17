using FileStorage.Api.Contracts;
using FileStorage.Application.Abstractions;
using FileStorage.Application.Exceptions;
using FileStorage.Application.Files.UseCases;
using FileStorage.Application.Options;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace FileStorage.Api.Files;

public static class FileUploadEndpoint
{
    private const long MultipartOverheadBytes = 1024 * 1024;

    public static IEndpointRouteBuilder MapFileUploadEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/files", HandleAsync)
            .RequireAuthorization()
            .WithName("UploadFile");

        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        UploadFileService uploadFileService,
        ICurrentUser currentUser,
        IOptions<UploadPolicyOptions> policyOptions,
        CancellationToken cancellationToken)
    {
        var policy = policyOptions.Value;
        var request = context.Request;

        if (!MultipartHelpers.IsMultipartContentType(request.ContentType))
        {
            throw new UnsupportedMediaTypeAppException("Request must be multipart/form-data.");
        }

        var maxBodyFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxBodyFeature is { IsReadOnly: false })
        {
            maxBodyFeature.MaxRequestBodySize = policy.MaxFileSizeBytes + MultipartOverheadBytes;
        }

        var mediaType = MediaTypeHeaderValue.Parse(request.ContentType);
        var boundary = MultipartHelpers.GetBoundary(mediaType, policy.MultipartHeaderLengthLimit);

        var reader = new MultipartReader(boundary, request.Body)
        {
            HeadersCountLimit = policy.MultipartHeaderCountLimit,
            HeadersLengthLimit = policy.MultipartHeaderLengthLimit,
        };

        var rawTags = new List<string>();
        StagedUpload? staged = null;

        try
        {
            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(cancellationToken)) is not null)
            {
                var disposition = section.GetContentDispositionHeader();
                if (disposition is null)
                {
                    continue;
                }

                if (disposition.IsFileDisposition())
                {
                    if (staged is not null)
                    {
                        throw new ValidationAppException("file", "Multipart payload must contain exactly one file part.");
                    }

                    var originalFileName = disposition.FileNameStar.HasValue
                        ? disposition.FileNameStar.Value
                        : disposition.FileName.Value;

                    staged = await uploadFileService.StageAsync(
                        originalFileName ?? string.Empty,
                        section.ContentType ?? string.Empty,
                        section.Body,
                        cancellationToken);
                }
                else if (disposition.IsFormDisposition() &&
                         string.Equals(disposition.Name.Value, "tags", StringComparison.OrdinalIgnoreCase))
                {
                    var value = await MultipartHelpers.ReadBoundedFormValueAsync(section.Body, policy.MaxTagsFieldLength, cancellationToken);
                    rawTags.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }
            }

            if (staged is null)
            {
                throw new ValidationAppException("file", "Multipart payload must contain a file part.");
            }

            var uploaded = await uploadFileService.FinalizeAsync(staged, rawTags, currentUser.UserId, cancellationToken);

            var response = FileResponseMapper.ToResponse(uploaded);
            return Results.Created($"/api/files/{response.Id}", response);
        }
        catch
        {
            if (staged is not null)
            {
                await uploadFileService.DiscardStagedAsync(staged, CancellationToken.None);
            }

            throw;
        }
    }
}
