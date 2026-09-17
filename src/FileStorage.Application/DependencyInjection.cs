using FileStorage.Application.Files.UseCases;
using FileStorage.Application.Maintenance;
using FileStorage.Application.Options;
using FileStorage.Application.Uploads;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FileStorage.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<UploadPolicyOptions>()
            .Bind(configuration.GetSection(UploadPolicyOptions.SectionName));

        services.AddOptions<ResumableUploadOptions>()
            .Bind(configuration.GetSection(ResumableUploadOptions.SectionName));

        services.AddScoped<UploadFileService>();
        services.AddScoped<ListFilesService>();
        services.AddScoped<FileAccessService>();
        services.AddScoped<DeleteFileService>();
        services.AddScoped<ReconciliationService>();
        services.AddScoped<ResumableUploadService>();

        return services;
    }
}
