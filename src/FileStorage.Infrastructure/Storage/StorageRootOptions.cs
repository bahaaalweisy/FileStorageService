namespace FileStorage.Infrastructure.Storage;

public sealed class StorageRootOptions
{
    public const string SectionName = "Storage";

    public string RootPath { get; set; } = "_storage";
}
