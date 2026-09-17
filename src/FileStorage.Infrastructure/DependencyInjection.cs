using FileStorage.Application.Abstractions;
using FileStorage.Infrastructure.Persistence;
using FileStorage.Infrastructure.Storage;
using FileStorage.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FileStorage.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
                ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

            options.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
        });

        services.AddScoped<IStoredObjectRepository, StoredObjectRepository>();

        services.AddOptions<StorageRootOptions>()
            .Bind(configuration.GetSection(StorageRootOptions.SectionName));

        services.AddSingleton<IFileStorage, FileSystemStorage>();
        services.AddSingleton<IStorageKeyGenerator, RandomStorageKeyGenerator>();

        services.AddSingleton<IClock, SystemClock>();

        services.AddSingleton<IStorageInventory, FileSystemStorageInventory>();

        return services;
    }
}
