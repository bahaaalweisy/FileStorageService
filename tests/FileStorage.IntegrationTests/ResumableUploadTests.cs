using System.Net;
using System.Net.Http.Json;
using System.Text;
using FileStorage.Api.Contracts;
using FileStorage.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace FileStorage.IntegrationTests;

public class ResumableUploadTests : IntegrationTestBase
{
    private static async Task<UploadSessionStatusResponse> CreateSessionAsync(HttpClient client, long totalSizeBytes, string fileName = "resumable-test.bin")
    {
        var response = await client.PostAsJsonAsync("/api/upload-sessions", new
        {
            fileName,
            contentType = "application/octet-stream",
            totalSizeBytes,
            tags = new[] { "qa-run", "resumable" },
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UploadSessionStatusResponse>())!;
    }

    [Fact]
    public async Task FullSession_ChunkedInOrder_FinalizesWithMatchingChecksumAndByteCount()
    {
        var client = Client;
        var token = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, token);

        var fullContent = Encoding.UTF8.GetBytes(new string('Q', 300_000));
        var expectedChecksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fullContent)).ToLowerInvariant();

        var session = await CreateSessionAsync(client, fullContent.Length);
        session.NextExpectedOffset.Should().Be(0);

        const int chunkSize = 100_000;
        long offset = 0;
        while (offset < fullContent.Length)
        {
            var length = Math.Min(chunkSize, fullContent.Length - offset);
            var chunk = fullContent[(int)offset..(int)(offset + length)];

            var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/upload-sessions/{session.SessionId}/chunks?offset={offset}")
            {
                Content = new ByteArrayContent(chunk),
            };
            var putResponse = await client.SendAsync(putRequest);
            putResponse.EnsureSuccessStatusCode();
            var status = await putResponse.Content.ReadFromJsonAsync<UploadSessionStatusResponse>();
            status!.ReceivedBytes.Should().Be(offset + length);

            offset += length;
        }

        var finalizeResponse = await client.PostAsync($"/api/upload-sessions/{session.SessionId}/finalize", null);
        finalizeResponse.EnsureSuccessStatusCode();
        var finalized = await finalizeResponse.Content.ReadFromJsonAsync<StoredObjectResponse>();

        finalized!.SizeBytes.Should().Be(fullContent.Length);
        finalized.Checksum.Should().Be(expectedChecksum);

        var downloadResponse = await client.GetAsync($"/api/files/{finalized.Id}/download");
        downloadResponse.EnsureSuccessStatusCode();
        var downloaded = await downloadResponse.Content.ReadAsByteArrayAsync();
        downloaded.Should().BeEquivalentTo(fullContent, "the assembled, finalized file must be byte-identical to the original");
    }

    [Fact]
    public async Task Chunk_AtWrongOffset_Returns409WithExpectedOffsetGuidance()
    {
        var client = Client;
        var token = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, token);

        var session = await CreateSessionAsync(client, 100);

        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/upload-sessions/{session.SessionId}/chunks?offset=50")
        {
            Content = new ByteArrayContent(new byte[50]),
        };
        var response = await client.SendAsync(putRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DuplicateRetriedChunk_AtAlreadyDurableOffset_IsAcceptedIdempotently()
    {
        var client = Client;
        var token = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, token);

        var session = await CreateSessionAsync(client, 100);
        var chunk = new byte[50];

        var first = new HttpRequestMessage(HttpMethod.Put, $"/api/upload-sessions/{session.SessionId}/chunks?offset=0") { Content = new ByteArrayContent(chunk) };
        (await client.SendAsync(first)).EnsureSuccessStatusCode();

        var retry = new HttpRequestMessage(HttpMethod.Put, $"/api/upload-sessions/{session.SessionId}/chunks?offset=0") { Content = new ByteArrayContent(chunk) };
        var retryResponse = await client.SendAsync(retry);

        retryResponse.StatusCode.Should().Be(HttpStatusCode.OK, "a replayed chunk of already-durable bytes must be accepted idempotently, not rejected");
        var status = await retryResponse.Content.ReadFromJsonAsync<UploadSessionStatusResponse>();
        status!.ReceivedBytes.Should().Be(50, "the replay must not duplicate/double-count already-written bytes");
    }

    [Fact]
    public async Task Finalize_BeforeAllBytesReceived_Returns409()
    {
        var client = Client;
        var token = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, token);

        var session = await CreateSessionAsync(client, 100);
        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/upload-sessions/{session.SessionId}/chunks?offset=0") { Content = new ByteArrayContent(new byte[50]) };
        (await client.SendAsync(putRequest)).EnsureSuccessStatusCode();

        var finalizeResponse = await client.PostAsync($"/api/upload-sessions/{session.SessionId}/finalize", null);

        finalizeResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RepeatedFinalize_DoesNotCreateADuplicateFile()
    {
        var client = Client;
        var token = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, token);

        var content = new byte[100];
        var session = await CreateSessionAsync(client, content.Length);
        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/upload-sessions/{session.SessionId}/chunks?offset=0") { Content = new ByteArrayContent(content) };
        (await client.SendAsync(putRequest)).EnsureSuccessStatusCode();

        var firstFinalize = await client.PostAsync($"/api/upload-sessions/{session.SessionId}/finalize", null);
        firstFinalize.EnsureSuccessStatusCode();

        var secondFinalize = await client.PostAsync($"/api/upload-sessions/{session.SessionId}/finalize", null);
        secondFinalize.StatusCode.Should().Be(HttpStatusCode.Conflict, "finalizing an already-completed session must not silently create a second StoredObjects row");
    }

    [Fact]
    public async Task DifferentUser_CannotAppendChunksOrFinalizeAnotherUsersSession()
    {
        var client = Client;
        var ownerToken = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, ownerToken);
        var session = await CreateSessionAsync(client, 100);

        var otherToken = await TestHelpers.GetTokenAsync(client, "admin");
        var otherClient = Factory.CreateClient();
        TestHelpers.AuthorizeAs(otherClient, otherToken);

        var chunkRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/upload-sessions/{session.SessionId}/chunks?offset=0") { Content = new ByteArrayContent(new byte[50]) };
        var chunkResponse = await otherClient.SendAsync(chunkRequest);
        chunkResponse.StatusCode.Should().Be(HttpStatusCode.NotFound, "a non-owner (even an admin, by this app's ownership model for sessions) must not be able to write another user's in-progress session");

        var statusResponse = await otherClient.GetAsync($"/api/upload-sessions/{session.SessionId}");

        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Abort_DiscardsSessionAndPreventsFurtherChunksOrFinalize()
    {
        var client = Client;
        var token = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, token);

        var session = await CreateSessionAsync(client, 100);
        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/upload-sessions/{session.SessionId}/chunks?offset=0") { Content = new ByteArrayContent(new byte[50]) };
        (await client.SendAsync(putRequest)).EnsureSuccessStatusCode();

        var abortResponse = await client.DeleteAsync($"/api/upload-sessions/{session.SessionId}");
        abortResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var putAfterAbort = new HttpRequestMessage(HttpMethod.Put, $"/api/upload-sessions/{session.SessionId}/chunks?offset=50") { Content = new ByteArrayContent(new byte[50]) };
        (await client.SendAsync(putAfterAbort)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var finalizeAfterAbort = await client.PostAsync($"/api/upload-sessions/{session.SessionId}/finalize", null);
        finalizeAfterAbort.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task InterruptedTransfer_RecoversAcrossAnIsolatedBackendRestart()
    {

        var firstFactory = new CustomWebApplicationFactory(
            Path.Combine(Path.GetTempPath(), "filestorage-it-restart-" + Guid.NewGuid().ToString("N")),
            "FileStorageIntegrationTests_Restart_" + Guid.NewGuid().ToString("N"),
            ownsDatabase: true);

        var fullContent = new byte[250_000];
        new Random(42).NextBytes(fullContent);
        var expectedChecksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fullContent)).ToLowerInvariant();
        Guid sessionId;

        try
        {
            var client = firstFactory.CreateClient();
            var token = await TestHelpers.GetTokenAsync(client, "user");
            TestHelpers.AuthorizeAs(client, token);

            var session = await CreateSessionAsync(client, fullContent.Length, "restart-recovery-test.bin");
            sessionId = session.SessionId;

            var firstHalf = fullContent[..125_000];
            var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/upload-sessions/{sessionId}/chunks?offset=0")
            {
                Content = new ByteArrayContent(firstHalf),
            };
            (await client.SendAsync(putRequest)).EnsureSuccessStatusCode();
        }
        finally
        {

        }

        var secondFactory = new CustomWebApplicationFactory(firstFactory.StorageRoot, firstFactory.DatabaseName, ownsDatabase: true);
        try
        {
            var client2 = secondFactory.CreateClient();
            var token2 = await TestHelpers.GetTokenAsync(client2, "user");
            TestHelpers.AuthorizeAs(client2, token2);

            var statusAfterRestart = await client2.GetAsync($"/api/upload-sessions/{sessionId}");
            statusAfterRestart.EnsureSuccessStatusCode();
            var status = await statusAfterRestart.Content.ReadFromJsonAsync<UploadSessionStatusResponse>();
            status!.ReceivedBytes.Should().Be(125_000, "the session's durable progress must survive a backend restart");

            var secondHalf = fullContent[125_000..];
            var putRequest2 = new HttpRequestMessage(HttpMethod.Put, $"/api/upload-sessions/{sessionId}/chunks?offset={status.ReceivedBytes}")
            {
                Content = new ByteArrayContent(secondHalf),
            };
            (await client2.SendAsync(putRequest2)).EnsureSuccessStatusCode();

            var finalizeResponse = await client2.PostAsync($"/api/upload-sessions/{sessionId}/finalize", null);
            finalizeResponse.EnsureSuccessStatusCode();
            var finalized = await finalizeResponse.Content.ReadFromJsonAsync<StoredObjectResponse>();

            finalized!.SizeBytes.Should().Be(fullContent.Length);
            finalized.Checksum.Should().Be(expectedChecksum, "the file assembled across the restart must still match the original content exactly");
        }
        finally
        {
            secondFactory.Dispose();
            firstFactory.Dispose();
        }
    }
}
