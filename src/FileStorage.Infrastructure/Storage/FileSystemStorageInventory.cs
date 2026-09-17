using System.Globalization;
using System.Runtime.CompilerServices;
using FileStorage.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace FileStorage.Infrastructure.Storage;

public sealed class FileSystemStorageInventory : IStorageInventory
{
    private readonly StoragePathResolver _paths;

    public FileSystemStorageInventory(IOptions<StorageRootOptions> rootOptions)
    {
        _paths = new StoragePathResolver(rootOptions.Value.RootPath);
    }

    public async IAsyncEnumerable<StoredContentEntry> EnumerateContentAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var root = _paths.RootFullPath;
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var yearDir in SafeEnumerateDirectories(root))
        {
            var yearName = Path.GetFileName(yearDir);
            if (!IsAllDigits(yearName, 4))
            {
                continue;
            }

            foreach (var monthDir in SafeEnumerateDirectories(yearDir))
            {
                if (!IsAllDigits(Path.GetFileName(monthDir), 2))
                {
                    continue;
                }

                foreach (var dayDir in SafeEnumerateDirectories(monthDir))
                {
                    if (!IsAllDigits(Path.GetFileName(dayDir), 2))
                    {
                        continue;
                    }

                    foreach (var keyDir in SafeEnumerateDirectories(dayDir))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var contentPath = Path.Combine(keyDir, "content.bin");
                        if (!File.Exists(contentPath))
                        {
                            continue;
                        }

                        var key = Path.GetFileName(keyDir);
                        var info = new FileInfo(contentPath);

                        var createdAtUtc = ParseDatePath(yearDir, monthDir, dayDir);

                        yield return new StoredContentEntry(key, createdAtUtc, info.LastWriteTimeUtc, info.Length);
                    }
                }
            }
        }

        await Task.CompletedTask;
    }

    private static DateTime ParseDatePath(string yearDir, string monthDir, string dayDir)
    {
        var year = int.Parse(Path.GetFileName(yearDir), CultureInfo.InvariantCulture);
        var month = int.Parse(Path.GetFileName(monthDir), CultureInfo.InvariantCulture);
        var day = int.Parse(Path.GetFileName(dayDir), CultureInfo.InvariantCulture);
        return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
    }

    private static bool IsAllDigits(string value, int expectedLength) =>
        value.Length == expectedLength && value.All(char.IsDigit);

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch (IOException)
        {
            return Enumerable.Empty<string>();
        }
    }
}
