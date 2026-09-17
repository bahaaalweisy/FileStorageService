using FileStorage.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace FileStorage.IntegrationTests;

public class HealthCheckTests : IntegrationTestBase
{
    [Fact]
    public async Task Ready_ReportsUnhealthy_WhenStorageRootFilesystemProbeCannotWrite()
    {
        // The happy-path /health/ready check (see AuthorizationAndDeleteTests.HealthEndpoints_RespondHealthy)
        // only proves the endpoint returns 200 when everything is fine. This test proves the
        // filesystem probe half of readiness actually fails closed: it blocks the probe's own
        // "_health" subdirectory with a file of that exact name (so Directory.CreateDirectory
        // for the probe folder cannot succeed), against this test's own isolated storage root,
        // never the real running application's storage.
        var blockedHealthDir = Path.Combine(Factory.StorageRoot, "_health");
        Directory.CreateDirectory(Factory.StorageRoot);
        await File.WriteAllTextAsync(blockedHealthDir, "blocking file occupies the _health directory name");

        var response = await Client.GetAsync("/health/ready");

        response.IsSuccessStatusCode.Should().BeFalse(
            "the filesystem probe cannot create its health-check directory and readiness must fail closed, not report Healthy");
    }
}
