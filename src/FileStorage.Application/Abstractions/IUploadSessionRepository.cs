using FileStorage.Domain.Entities;

namespace FileStorage.Application.Abstractions;

public interface IUploadSessionRepository
{
    Task AddAsync(UploadSession session, CancellationToken cancellationToken);

    Task<UploadSession?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<UploadSession>> FindExpiredInProgressAsync(DateTime nowUtc, int maxCount, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
