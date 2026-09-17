using System.Net;
using System.Net.Http.Json;
using FileStorage.Api.Contracts;
using FileStorage.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace FileStorage.IntegrationTests;

public class AuditLogTests : IntegrationTestBase
{

    [Fact]
    public async Task Upload_WritesAnAuditEntry_ReadableByAdmin()
    {
        var client = Client;
        var userToken = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, userToken);

        using var content = TestHelpers.BuildUploadContent(new byte[10], "audit-upload-test.txt", "text/plain");
        var uploadResponse = await client.PostAsync("/api/files", content);
        uploadResponse.EnsureSuccessStatusCode();
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<StoredObjectResponse>();
        var correlationId = uploadResponse.Headers.GetValues("X-Correlation-Id").Single();

        var adminToken = await TestHelpers.GetTokenAsync(client, "admin");
        var adminClient = Factory.CreateClient();
        TestHelpers.AuthorizeAs(adminClient, adminToken);

        var auditResponse = await adminClient.GetAsync($"/api/admin/audit-log?resourceId={uploaded!.Id}");
        auditResponse.EnsureSuccessStatusCode();
        var page = await auditResponse.Content.ReadFromJsonAsync<AuditLogListResponse>();

        var entry = page!.Items.Should().ContainSingle(x => x.Operation == "FileUpload").Subject;
        entry.ResourceId.Should().Be(uploaded.Id.ToString());
        entry.Outcome.Should().Be("Success");
        entry.ActorUserId.Should().NotBeNullOrWhiteSpace();
        entry.CorrelationId.Should().Be(correlationId, "the audit entry for this request must carry the SAME correlation id as the request's own response header");
    }

    [Fact]
    public async Task SoftDeleteThenHardDelete_BothWriteAuditEntries_StillReadableAfterHardDelete()
    {
        var client = Client;
        var userToken = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, userToken);

        using var content = TestHelpers.BuildUploadContent(new byte[10], "audit-delete-test.txt", "text/plain");
        var uploadResponse = await client.PostAsync("/api/files", content);
        uploadResponse.EnsureSuccessStatusCode();
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<StoredObjectResponse>();

        (await client.DeleteAsync($"/api/files/{uploaded!.Id}")).EnsureSuccessStatusCode();

        var adminToken = await TestHelpers.GetTokenAsync(client, "admin");
        var adminClient = Factory.CreateClient();
        TestHelpers.AuthorizeAs(adminClient, adminToken);

        (await adminClient.DeleteAsync($"/api/files/{uploaded.Id}/hard")).EnsureSuccessStatusCode();

        var auditResponse = await adminClient.GetAsync($"/api/admin/audit-log?resourceId={uploaded.Id}");
        auditResponse.EnsureSuccessStatusCode();
        var page = await auditResponse.Content.ReadFromJsonAsync<AuditLogListResponse>();

        page!.Items.Should().Contain(x => x.Operation == "FileUpload");
        page.Items.Should().Contain(x => x.Operation == "FileSoftDelete");
        page.Items.Should().Contain(x => x.Operation == "FileHardDelete");
    }

    [Fact]
    public async Task NonAdminUser_CannotReadAuditLog()
    {
        var client = Client;
        var userToken = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, userToken);

        var response = await client.GetAsync("/api/admin/audit-log");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NonAdminUser_CannotAlterAuditData()
    {

        var client = Client;
        var userToken = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, userToken);

        var deleteAttempt = await client.DeleteAsync($"/api/admin/audit-log/{Guid.NewGuid()}");
        var putAttempt = await client.PutAsync("/api/admin/audit-log", null);

        deleteAttempt.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed, HttpStatusCode.Forbidden);
        putAttempt.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuditLog_SupportsFilteringByActorAndOperation()
    {
        var client = Client;
        var userToken = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, userToken);

        using var content = TestHelpers.BuildUploadContent(new byte[10], "audit-filter-test.txt", "text/plain");
        (await client.PostAsync("/api/files", content)).EnsureSuccessStatusCode();

        var adminToken = await TestHelpers.GetTokenAsync(client, "admin");
        var adminClient = Factory.CreateClient();
        TestHelpers.AuthorizeAs(adminClient, adminToken);

        var response = await adminClient.GetAsync("/api/admin/audit-log?operation=FileUpload&actorUserId=11111111-1111-1111-1111-111111111111");
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<AuditLogListResponse>();

        page!.Items.Should().NotBeEmpty();
        page.Items.Should().OnlyContain(x => x.Operation == "FileUpload" && x.ActorUserId == "11111111-1111-1111-1111-111111111111");
    }
}
