using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FileStorage.IntegrationTests.Infrastructure;

public static class TestHelpers
{
    public static async Task<string> GetTokenAsync(HttpClient client, string role)
    {
        var response = await client.PostAsJsonAsync("/api/auth/mock-token", new { role });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return payload!.AccessToken;
    }

    public static void AuthorizeAs(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public static MultipartFormDataContent BuildUploadContent(byte[] bytes, string fileName, string contentType, string[]? tags = null)
    {
        var content = new MultipartFormDataContent("test-boundary-" + Guid.NewGuid().ToString("N"));

        if (tags is { Length: > 0 })
        {
            content.Add(new StringContent(string.Join(",", tags)), "tags");
        }

        AddFilePart(content, bytes, fileName, contentType);

        return content;
    }

    public static MultipartFormDataContent BuildUploadContentTagsAfterFile(byte[] bytes, string fileName, string contentType, string[]? tags = null)
    {
        var content = new MultipartFormDataContent("test-boundary-" + Guid.NewGuid().ToString("N"));

        AddFilePart(content, bytes, fileName, contentType);

        if (tags is { Length: > 0 })
        {
            content.Add(new StringContent(string.Join(",", tags)), "tags");
        }

        return content;
    }

    private static void AddFilePart(MultipartFormDataContent content, byte[] bytes, string fileName, string contentType)
    {
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
    }

    private sealed record TokenResponse(string AccessToken, DateTime ExpiresAtUtc, string UserId, string Role, string DisplayName);
}
