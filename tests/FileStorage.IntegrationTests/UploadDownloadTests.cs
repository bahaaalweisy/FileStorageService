using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using FileStorage.Api.Contracts;
using FileStorage.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace FileStorage.IntegrationTests;

public class UploadDownloadTests : IntegrationTestBase
{
    [Fact]
    public async Task Upload_ThenDownload_ReturnsIdenticalBytes()
    {
        var client = Client;
        var token = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, token);

        var originalBytes = RandomBytes(64 * 1024);
        var content = TestHelpers.BuildUploadContent(originalBytes, "photo.png", "image/png", new[] { "vacation", "2026" });

        var uploadResponse = await client.PostAsync("/api/files", content);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<StoredObjectResponse>();
        uploaded.Should().NotBeNull();
        uploaded!.SizeBytes.Should().Be(originalBytes.Length);
        uploaded.Checksum.Should().Be(Convert.ToHexString(SHA256.HashData(originalBytes)).ToLowerInvariant());
        uploaded.Tags.Should().BeEquivalentTo(new[] { "vacation", "2026" });

        var downloadResponse = await client.GetAsync($"/api/files/{uploaded.Id}/download");
        downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var downloadedBytes = await downloadResponse.Content.ReadAsByteArrayAsync();
        downloadedBytes.Should().BeEquivalentTo(originalBytes);
    }

    [Fact]
    public async Task Download_SupportsRangeRequests()
    {
        var client = Client;
        var token = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, token);

        var originalBytes = RandomBytes(10_000);
        var content = TestHelpers.BuildUploadContent(originalBytes, "data.bin", "application/octet-stream");
        var uploadResponse = await client.PostAsync("/api/files", content);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<StoredObjectResponse>();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/files/{uploaded!.Id}/download");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(100, 199);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        var partial = await response.Content.ReadAsByteArrayAsync();
        partial.Should().HaveCount(100);
        partial.Should().BeEquivalentTo(originalBytes[100..200]);
    }

    [Fact]
    public async Task Download_UnsatisfiableRange_Returns416()
    {
        var client = Client;
        var token = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, token);

        var originalBytes = RandomBytes(1000);
        var content = TestHelpers.BuildUploadContent(originalBytes, "data.bin", "application/octet-stream");
        var uploadResponse = await client.PostAsync("/api/files", content);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<StoredObjectResponse>();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/files/{uploaded!.Id}/download");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(5000, 6000);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.RequestedRangeNotSatisfiable);
    }

    [Fact]
    public async Task Upload_WithTagsFieldBeforeFilePart_HonorsTags()
    {
        var client = Client;
        TestHelpers.AuthorizeAs(client, await TestHelpers.GetTokenAsync(client, "user"));

        var content = TestHelpers.BuildUploadContent(RandomBytes(50), "order-before.txt", "text/plain", new[] { "before-order" });
        var response = await client.PostAsync("/api/files", content);
        var uploaded = await response.Content.ReadFromJsonAsync<StoredObjectResponse>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        uploaded!.Tags.Should().BeEquivalentTo(new[] { "before-order" });
    }

    [Fact]
    public async Task Upload_WithTagsFieldAfterFilePart_HonorsTags()
    {
        var client = Client;
        TestHelpers.AuthorizeAs(client, await TestHelpers.GetTokenAsync(client, "user"));

        var content = TestHelpers.BuildUploadContentTagsAfterFile(RandomBytes(50), "order-after.txt", "text/plain", new[] { "after-order" });
        var response = await client.PostAsync("/api/files", content);
        var uploaded = await response.Content.ReadFromJsonAsync<StoredObjectResponse>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        uploaded!.Tags.Should().BeEquivalentTo(
            new[] { "after-order" },
            "the multipart field order must not be significant — tags after the file part must still be honored");
    }

    [Fact]
    public async Task Upload_WithoutFilePart_Returns400()
    {
        var client = Client;
        var token = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, token);

        var content = new MultipartFormDataContent();
        content.Add(new StringContent("just-tags"), "tags");

        var response = await client.PostAsync("/api/files", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_ExceedingConfiguredMaxSize_Returns413()
    {
        var client = Client;
        var token = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, token);

        var oversized = RandomBytes(6 * 1024 * 1024);
        var content = TestHelpers.BuildUploadContent(oversized, "big.bin", "application/octet-stream");

        var response = await client.PostAsync("/api/files", content);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Upload_WithoutAuthentication_Returns401()
    {
        var client = Client;
        var content = TestHelpers.BuildUploadContent(RandomBytes(10), "x.txt", "text/plain");

        var response = await client.PostAsync("/api/files", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }
}
