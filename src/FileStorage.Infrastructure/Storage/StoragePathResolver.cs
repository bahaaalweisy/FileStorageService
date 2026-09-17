using System.Text.RegularExpressions;

namespace FileStorage.Infrastructure.Storage;

public sealed partial class StoragePathResolver
{
    [GeneratedRegex("^[A-Za-z0-9_-]{8,64}$")]
    private static partial Regex KeyPattern();

    private readonly string _rootFullPath;

    public StoragePathResolver(string rootPath)
    {
        Directory.CreateDirectory(rootPath);
        _rootFullPath = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public string RootFullPath => _rootFullPath;

    public string GetContentPath(string key, DateTime createdAtUtc)
    {
        if (!KeyPattern().IsMatch(key))
        {
            throw new ArgumentException("Storage key has an unexpected format.", nameof(key));
        }

        var relative = Path.Combine(
            createdAtUtc.Year.ToString("D4"),
            createdAtUtc.Month.ToString("D2"),
            createdAtUtc.Day.ToString("D2"),
            key,
            "content.bin");

        var fullPath = Path.GetFullPath(Path.Combine(_rootFullPath, relative));

        if (!IsContained(fullPath))
        {
            throw new InvalidOperationException("Resolved storage path escaped the storage root.");
        }

        return fullPath;
    }

    public string GetTempDirectory()
    {
        var dir = Path.Combine(_rootFullPath, "_tmp");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public string GetHealthProbeDirectory()
    {
        var dir = Path.Combine(_rootFullPath, "_health");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private bool IsContained(string fullPath)
    {
        var normalizedRoot = _rootFullPath + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
