using FileStorage.Application.Exceptions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace FileStorage.Api.Files;

public static class MultipartHelpers
{
    public static bool IsMultipartContentType(string? contentType) =>
        !string.IsNullOrEmpty(contentType) &&
        contentType.Contains("multipart/", StringComparison.OrdinalIgnoreCase);

    public static string GetBoundary(MediaTypeHeaderValue contentType, int lengthLimit)
    {
        var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;

        if (string.IsNullOrWhiteSpace(boundary))
        {
            throw new ValidationAppException("contentType", "Missing multipart boundary.");
        }

        if (boundary.Length > lengthLimit)
        {
            throw new ValidationAppException("contentType", "Multipart boundary exceeds the configured length limit.");
        }

        return boundary;
    }

    public static async Task<string> ReadBoundedFormValueAsync(Stream sectionBody, int maxLength, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(sectionBody, leaveOpen: true);
        var buffer = new char[Math.Min(maxLength, 4096)];
        var sb = new System.Text.StringBuilder();
        int read;

        while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            sb.Append(buffer, 0, read);
            if (sb.Length > maxLength)
            {
                throw new ValidationAppException("tags", $"Field exceeds the maximum allowed length of {maxLength} characters.");
            }
        }

        return sb.ToString();
    }
}
