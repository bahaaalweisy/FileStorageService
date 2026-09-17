using FileStorage.Application.Abstractions;
using FileStorage.Domain.Entities;
using FileStorage.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FileStorage.Infrastructure.Persistence;

public class UploadSessionRepository : IUploadSessionRepository
{
    private readonly AppDbContext _db;

    public UploadSessionRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(UploadSession session, CancellationToken cancellationToken)
        => await _db.UploadSessions.AddAsync(session, cancellationToken);

    public Task<UploadSession?> FindAsync(Guid id, CancellationToken cancellationToken)
        => _db.UploadSessions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<UploadSession>> FindExpiredInProgressAsync(DateTime nowUtc, int maxCount, CancellationToken cancellationToken)
        => await _db.UploadSessions
            .Where(x => x.Status == UploadSessionStatus.InProgress && x.ExpiresAtUtc <= nowUtc)
            .OrderBy(x => x.ExpiresAtUtc)
            .Take(maxCount)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
        => _db.SaveChangesAsync(cancellationToken);
}
