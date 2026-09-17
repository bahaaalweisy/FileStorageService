using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FileStorage.Api.Contracts;
using FileStorage.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace FileStorage.IntegrationTests;

public class ETagCachingTests : IntegrationTestBase
{

    private async Task<(HttpClient client, Guid fileId, string etag)> UploadAndGetETagAsync()
    {
        var client = Client;
        var token = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, token);

        using var content = TestHelpers.BuildUploadContent(
            System.Text.Encoding.UTF8.GetBytes("etag-test-content"), "etag-test.txt", "text/plain");
        var uploadResponse = await client.PostAsync("/api/files", content);
        uploadResponse.EnsureSuccessStatusCode();
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<StoredObjectResponse>();

        var firstDownload = await client.GetAsync($"/api/files/{uploaded!.Id}/download");
        firstDownload.EnsureSuccessStatusCode();
        var etag = firstDownload.Headers.ETag!.Tag;

        return (client, uploaded.Id, etag);
    }

    [Fact]
    public async Task Download_ReturnsETagAndPrivateNoCacheHeaders_OnNormalResponse()
    {
        var (client, fileId, etag) = await UploadAndGetETagAsync();

        var response = await client.GetAsync($"/api/files/{fileId}/download");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.ETag!.Tag.Should().Be(etag, "the ETag must be stable across requests for unchanged content");
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.Private.Should().BeTrue("private, per-user file content must never be cached by shared/proxy caches");
    }

    [Fact]
    public async Task Download_WithMatchingIfNoneMatch_Returns304WithNoBody()
    {
        var (client, fileId, etag) = await UploadAndGetETagAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/files/{fileId}/download");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
        var body = await response.Content.ReadAsByteArrayAsync();
        body.Should().BeEmpty("a 304 response must not include a body");
    }

    [Fact]
    public async Task Download_WithNonMatchingIfNoneMatch_ReturnsFullContent()
    {
        var (client, fileId, _) = await UploadAndGetETagAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/files/{fileId}/download");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"some-stale-or-wrong-etag\""));
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("etag-test-content");
    }

    [Fact]
    public async Task Head_WithMatchingIfNoneMatch_Returns304()
    {
        var (client, fileId, etag) = await UploadAndGetETagAsync();

        var request = new HttpRequestMessage(HttpMethod.Head, $"/api/files/{fileId}/download");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Download_RangeRequest_WithMatchingIfRange_ReturnsPartialContent()
    {
        var (client, fileId, etag) = await UploadAndGetETagAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/files/{fileId}/download");
        request.Headers.Range = new RangeHeaderValue(0, 3);
        request.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue(etag));
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("etag");
    }

    [Fact]
    public async Task Download_RangeRequest_WithStaleIfRange_ReturnsFullContentNotPartial()
    {
        var (client, fileId, _) = await UploadAndGetETagAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/files/{fileId}/download");
        request.Headers.Range = new RangeHeaderValue(0, 3);
        request.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue("\"stale-etag-from-before-a-hypothetical-change\""));
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a non-matching If-Range must cause the server to ignore the Range request and return the full, current representation");
    }

    [Fact]
    public async Task Preview_SupportedContentType_ReturnsETag()
    {
        var client = Client;
        var token = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, token);

        var pngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        using var content = TestHelpers.BuildUploadContent(pngBytes, "etag-preview.png", "image/png");
        var uploadResponse = await client.PostAsync("/api/files", content);
        uploadResponse.EnsureSuccessStatusCode();
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<StoredObjectResponse>();

        var previewResponse = await client.GetAsync($"/api/files/{uploaded!.Id}/preview");

        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        previewResponse.Headers.ETag.Should().NotBeNull();
    }

    [Fact]
    public async Task Download_AfterHardDelete_Returns404EvenWithPreviouslyValidETag_NeverA304()
    {
        var (client, fileId, etag) = await UploadAndGetETagAsync();

        (await client.DeleteAsync($"/api/files/{fileId}")).EnsureSuccessStatusCode();

        var adminToken = await TestHelpers.GetTokenAsync(client, "admin");
        var adminClient = Factory.CreateClient();
        TestHelpers.AuthorizeAs(adminClient, adminToken);
        (await adminClient.DeleteAsync($"/api/files/{fileId}/hard")).EnsureSuccessStatusCode();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/files/{fileId}/download");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a hard-deleted file must never be served from cache via a stale but structurally-valid ETag -- authorization/existence is checked before any conditional-request logic runs");
    }

    [Fact]
    public async Task Download_AuthorizationIsEnforcedBeforeConditionalCacheLogic()
    {

        var client = Client;
        var token = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, token);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/files/{Guid.NewGuid()}/download");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"anything\""));
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
