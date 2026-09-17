using System.Net.Http.Json;
using FileStorage.Api.Contracts;
using FileStorage.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace FileStorage.IntegrationTests;

public class ListingTests : IntegrationTestBase
{
    [Fact]
    public async Task List_FiltersByTag_UsingExactMatch_NotSubstring()
    {
        var client = Client;
        var token = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, token);

        await Upload(client, "a.txt", new[] { "category" });
        await Upload(client, "b.txt", new[] { "cat" });

        var response = await client.GetAsync("/api/files?tag=cat");
        var result = await response.Content.ReadFromJsonAsync<FileListResponse>();

        result!.Items.Should().ContainSingle(x => x.OriginalName == "b.txt");
        result.Items.Should().NotContain(x => x.OriginalName == "a.txt");
    }

    [Fact]
    public async Task List_FiltersByName()
    {
        var client = Client;
        var token = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, token);

        await Upload(client, "invoice-march.pdf");
        await Upload(client, "receipt-march.pdf");

        var response = await client.GetAsync("/api/files?name=invoice");
        var result = await response.Content.ReadFromJsonAsync<FileListResponse>();

        result!.Items.Should().OnlyContain(x => x.OriginalName.Contains("invoice"));
    }

    [Fact]
    public async Task List_Paginates_WithDeterministicOrder()
    {
        var client = Client;
        var token = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, token);

        for (var i = 0; i < 5; i++)
        {
            await Upload(client, $"page-item-{i}.txt");
        }

        var page1 = await client.GetAsync("/api/files?pageSize=2&page=1");
        var page1Result = await page1.Content.ReadFromJsonAsync<FileListResponse>();

        var page2 = await client.GetAsync("/api/files?pageSize=2&page=2");
        var page2Result = await page2.Content.ReadFromJsonAsync<FileListResponse>();

        page1Result!.Items.Should().HaveCount(2);
        page2Result!.Items.Should().HaveCount(2);
        page1Result.Items.Select(x => x.Id).Should().NotIntersectWith(page2Result.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task List_FiltersByDateRange_TreatingOffsetlessValuesAsUtc()
    {

        var client = Client;
        var token = await TestHelpers.GetTokenAsync(client, "user");
        TestHelpers.AuthorizeAs(client, token);

        await Upload(client, "date-range-test.txt");

        var tomorrowNoOffset = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ss");
        var excluded = await client.GetAsync($"/api/files?from={Uri.EscapeDataString(tomorrowNoOffset)}");
        var excludedResult = await excluded.Content.ReadFromJsonAsync<FileListResponse>();
        excludedResult!.Items.Should().NotContain(x => x.OriginalName == "date-range-test.txt");

        var yesterdayNoOffset = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ss");
        var included = await client.GetAsync($"/api/files?from={Uri.EscapeDataString(yesterdayNoOffset)}");
        var includedResult = await included.Content.ReadFromJsonAsync<FileListResponse>();
        includedResult!.Items.Should().Contain(x => x.OriginalName == "date-range-test.txt");
    }

    [Fact]
    public async Task List_OnlyReturnsOwnFiles_ForNonAdminUser()
    {
        var client = Client;
        var userToken = await TestHelpers.GetTokenAsync(client, "user");
        var adminToken = await TestHelpers.GetTokenAsync(client, "admin");

        TestHelpers.AuthorizeAs(client, adminToken);
        await Upload(client, "admin-owned.txt");

        TestHelpers.AuthorizeAs(client, userToken);
        await Upload(client, "user-owned.txt");

        var response = await client.GetAsync("/api/files");
        var result = await response.Content.ReadFromJsonAsync<FileListResponse>();

        result!.Items.Should().OnlyContain(x => x.OriginalName == "user-owned.txt");
    }

    private static async Task<StoredObjectResponse> Upload(HttpClient client, string fileName, string[]? tags = null)
    {
        var content = TestHelpers.BuildUploadContent(new byte[100], fileName, "text/plain", tags);
        var response = await client.PostAsync("/api/files", content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StoredObjectResponse>())!;
    }
}
