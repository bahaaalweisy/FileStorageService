namespace FileStorage.Infrastructure.Storage;

public sealed class MaintenanceLock : IDisposable
{
    private readonly FileStream? _stream;

    private MaintenanceLock(FileStream? stream) => _stream = stream;

    public bool Acquired => _stream is not null;

    public static MaintenanceLock TryAcquire(string storageRootFullPath)
    {
        var lockPath = Path.Combine(storageRootFullPath, "_maintenance.lock");

        try
        {
            Directory.CreateDirectory(storageRootFullPath);
            var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new MaintenanceLock(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new MaintenanceLock(null);
        }
    }

    public void Dispose() => _stream?.Dispose();
}
