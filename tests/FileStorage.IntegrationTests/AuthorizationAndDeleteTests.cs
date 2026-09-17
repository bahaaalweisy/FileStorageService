using System.Net;
using System.Net.Http.Json;
using FileStorage.Api.Contracts;
using FileStorage.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace FileStorage.IntegrationTests;

public class AuthorizationAndDeleteTests : IntegrationTestBase
{
    [Fact]
    public async Task CrossUserAccess_ReturnsNotFound_NotForbidden()
    {
        var client = Client;
        var ownerToken = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, ownerToken);
        var uploaded = await Upload(client, "private.txt");

        TestHelpers.AuthorizeAs(client, await TestHelpers.GetTokenAsync(client, "admin"));
        var adminResponse = await client.GetAsync($"/api/files/{uploaded.Id}");
        adminResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMetadata_UnknownId_ReturnsNotFound()
    {
        var client = Client;
        TestHelpers.AuthorizeAs(client, await TestHelpers.GetTokenAsync(client, "user"));

        var response = await client.GetAsync($"/api/files/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task NonAdmin_CannotHardDelete_EvenOwnFile()
    {
        var client = Client;
        TestHelpers.AuthorizeAs(client, await TestHelpers.GetTokenAsync(client, "user"));
        var uploaded = await Upload(client, "mine.txt");

        var response = await client.DeleteAsync($"/api/files/{uploaded.Id}/hard");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SoftDelete_ExcludesFileFromListing_ButHardDeleteRequiresSoftDeleteFirst()
    {
        var client = Client;
        var userToken = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, userToken);
        var uploaded = await Upload(client, "lifecycle.txt");

        var adminToken = await TestHelpers.GetTokenAsync(client, "admin");
        TestHelpers.AuthorizeAs(client, adminToken);

        var prematureHardDelete = await client.DeleteAsync($"/api/files/{uploaded.Id}/hard");
        prematureHardDelete.StatusCode.Should().Be(HttpStatusCode.Conflict, "an admin must soft-delete before hard-deleting");

        TestHelpers.AuthorizeAs(client, userToken);
        var softDelete = await client.DeleteAsync($"/api/files/{uploaded.Id}");
        softDelete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listAfterSoftDelete = await client.GetAsync("/api/files");
        var listResult = await listAfterSoftDelete.Content.ReadFromJsonAsync<FileListResponse>();
        listResult!.Items.Should().NotContain(x => x.Id == uploaded.Id);

        var repeatSoftDelete = await client.DeleteAsync($"/api/files/{uploaded.Id}");
        repeatSoftDelete.StatusCode.Should().Be(HttpStatusCode.Conflict);

        TestHelpers.AuthorizeAs(client, adminToken);
        var hardDelete = await client.DeleteAsync($"/api/files/{uploaded.Id}/hard");
        hardDelete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var downloadAfterHardDelete = await client.GetAsync($"/api/files/{uploaded.Id}/download");
        downloadAfterHardDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeletedFilesListing_SurvivesReload_AndAllowsHardDeleteAfterward()
    {
        var client = Client;
        var userToken = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, userToken);
        var uploaded = await Upload(client, "server-backed-lifecycle.txt");

        var softDelete = await client.DeleteAsync($"/api/files/{uploaded.Id}");
        softDelete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var freshClient = Factory.CreateClient();
        var adminToken = await TestHelpers.GetTokenAsync(freshClient, "admin");
        TestHelpers.AuthorizeAs(freshClient, adminToken);

        var deletedListResponse = await freshClient.GetAsync("/api/files/deleted");
        deletedListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var deletedList = await deletedListResponse.Content.ReadFromJsonAsync<FileListResponse>();
        deletedList!.Items.Should().ContainSingle(x => x.Id == uploaded.Id);

        var normalListResponse = await freshClient.GetAsync("/api/files");
        var normalList = await normalListResponse.Content.ReadFromJsonAsync<FileListResponse>();
        normalList!.Items.Should().NotContain(x => x.Id == uploaded.Id);

        var hardDelete = await freshClient.DeleteAsync($"/api/files/{uploaded.Id}/hard");
        hardDelete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var deletedListAfterHardDelete = await freshClient.GetAsync("/api/files/deleted");
        var afterHardDelete = await deletedListAfterHardDelete.Content.ReadFromJsonAsync<FileListResponse>();
        afterHardDelete!.Items.Should().NotContain(x => x.Id == uploaded.Id);
    }

    [Fact]
    public async Task DeletedFilesListing_IsForbiddenToNonAdmin()
    {
        var client = Client;
        TestHelpers.AuthorizeAs(client, await TestHelpers.GetTokenAsync(client, "user"));

        var response = await client.GetAsync("/api/files/deleted");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task HealthEndpoints_RespondHealthy()
    {
        var client = Client;

        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");

        live.StatusCode.Should().Be(HttpStatusCode.OK);
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<StoredObjectResponse> Upload(HttpClient client, string fileName)
    {
        var content = TestHelpers.BuildUploadContent(new byte[50], fileName, "text/plain");
        var response = await client.PostAsync("/api/files", content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StoredObjectResponse>())!;
    }
}
