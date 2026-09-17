using FileStorage.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace FileStorage.IntegrationTests;

public class HealthCheckTests : IntegrationTestBase
{
    [Fact]
    public async Task Ready_ReportsUnhealthy_WhenStorageRootFilesystemProbeCannotWrite()
    {

        var blockedHealthDir = Path.Combine(Factory.StorageRoot, "_health");
        Directory.CreateDirectory(Factory.StorageRoot);
        await File.WriteAllTextAsync(blockedHealthDir, "blocking file occupies the _health directory name");

        var response = await Client.GetAsync("/health/ready");

        response.IsSuccessStatusCode.Should().BeFalse(
            "the filesystem probe cannot create its health-check directory and readiness must fail closed, not report Healthy");
    }
}
