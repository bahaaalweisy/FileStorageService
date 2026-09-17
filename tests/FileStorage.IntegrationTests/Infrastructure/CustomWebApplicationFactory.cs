using FileStorage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FileStorage.IntegrationTests.Infrastructure;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "filestorage-it-" + Guid.NewGuid().ToString("N"));

    private readonly string _databaseName = "FileStorageIntegrationTests_" + Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={_databaseName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
                ["Jwt:SigningKey"] = "integration-test-signing-key-at-least-32-chars-long",
                ["Jwt:EnableMockTokenEndpoint"] = "true",
                ["Storage:RootPath"] = StorageRoot,
                ["Database:AutoMigrate"] = "false",
                ["UploadPolicy:MaxFileSizeBytes"] = (5 * 1024 * 1024).ToString(),
            });
        });

        builder.ConfigureServices(services =>
        {
            var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.Migrate();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            try
            {
                using var scope = Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
                db.Database.EnsureDeleted();
            }
            catch
            {
            }

            if (Directory.Exists(StorageRoot))
            {
                try
                {
                    Directory.Delete(StorageRoot, recursive: true);
                }
                catch
                {
                }
            }
        }
    }
}
