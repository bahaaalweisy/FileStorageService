using FileStorage.Application.Maintenance;
using FileStorage.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FileStorage.Api.Maintenance;

public static class ReconcileCommand
{
    public static async Task<int> RunAsync(IServiceProvider rootServices, string[] args)
    {
        var deleteOrphans = args.Contains("--delete-orphans");
        var graceMinutes = ParseGraceMinutes(args);

        if (graceMinutes is < 0)
        {
            Console.WriteLine($"--grace-minutes must not be negative (got {graceMinutes}).");
            return 1;
        }

        using var scope = rootServices.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ReconciliationService>();

        Console.WriteLine("Scanning for DB/filesystem inconsistencies...");
        var report = await service.ScanAsync(
            graceMinutes.HasValue ? TimeSpan.FromMinutes(graceMinutes.Value) : null,
            CancellationToken.None);

        Console.WriteLine();
        Console.WriteLine($"Database rows scanned: {report.TotalDatabaseRows}");
        Console.WriteLine($"Files scanned:         {report.TotalFilesScanned}");
        Console.WriteLine();

        if (report.OrphanFiles.Count == 0 && report.MissingContent.Count == 0)
        {
            Console.WriteLine("No inconsistencies found.");
            return 0;
        }

        if (report.OrphanFiles.Count > 0)
        {
            Console.WriteLine($"Orphan files (on disk, no matching database row) — {report.OrphanFiles.Count}:");
            foreach (var orphan in report.OrphanFiles)
            {
                Console.WriteLine($"  key={orphan.Key} createdAtUtc={orphan.CreatedAtUtc:O} lastWriteUtc={orphan.LastWriteTimeUtc:O} size={orphan.SizeBytes}");
            }
            Console.WriteLine();
        }

        if (report.MissingContent.Count > 0)
        {
            Console.WriteLine($"Missing content (database row, no file on disk) — {report.MissingContent.Count}:");
            Console.WriteLine("  These are NOT auto-fixed. Recovery requires a human decision per row: restore");
            Console.WriteLine("  the file from a backup if one exists, or soft/hard-delete the row via the API");
            Console.WriteLine("  if the content is confirmed unrecoverable.");
            foreach (var missing in report.MissingContent)
            {
                Console.WriteLine($"  id={missing.Id} key={missing.Key} createdAtUtc={missing.CreatedAtUtc:O} softDeleted={missing.IsSoftDeleted}");
            }
            Console.WriteLine();
        }

        if (!deleteOrphans || report.OrphanFiles.Count == 0)
        {
            if (report.OrphanFiles.Count > 0)
            {
                Console.WriteLine("Report-only run: no files or database rows were modified.");
                Console.WriteLine("Re-run with --delete-orphans to remove the orphan files listed above.");
            }

            return 3;
        }

        var storageRootPath = Path.GetFullPath(scope.ServiceProvider.GetRequiredService<IOptions<StorageRootOptions>>().Value.RootPath);
        using var maintenanceLock = MaintenanceLock.TryAcquire(storageRootPath);

        if (!maintenanceLock.Acquired)
        {
            Console.WriteLine("SAFETY CHECK FAILED: could not acquire exclusive access to the storage root.");
            Console.WriteLine($"  ({storageRootPath})");
            Console.WriteLine("This means the API process (or another reconcile process) currently holds the");
            Console.WriteLine("maintenance lock. Orphan deletion is refused while that's the case — stop the API");
            Console.WriteLine("(and any other writer against this storage root) first, then re-run with --delete-orphans.");
            Console.WriteLine("No files were deleted.");
            return 4;
        }

        Console.WriteLine($"Exclusive access acquired. Deleting {report.OrphanFiles.Count} orphan file(s)...");
        var deleted = await service.DeleteOrphanFilesAsync(report.OrphanFiles, CancellationToken.None);
        Console.WriteLine($"Deleted {deleted} orphan file(s). Missing-content rows above were left untouched.");

        return 5;
    }

    private static int? ParseGraceMinutes(string[] args)
    {
        var prefix = "--grace-minutes=";
        var match = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal));
        return match is not null && int.TryParse(match.AsSpan(prefix.Length), out var minutes) ? minutes : null;
    }
}
