using FileStorage.Application.Abstractions;
using FileStorage.Infrastructure.Audit;
using FileStorage.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FileStorage.UnitTests.Infrastructure;

public class AuditLogWriterTests
{

    [Fact]
    public async Task RecordAsync_NeverThrows_WhenTheUnderlyingDatabaseWriteFails()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=qa-nonexistent-host-for-audit-failure-test,1;Database=DoesNotExist;Connect Timeout=1;TrustServerCertificate=True")
            .Options;

        await using var db = new AppDbContext(options);
        var writer = new AuditLogWriter(
            db,
            new FixedCurrentUser(),
            new FixedCorrelationIdAccessor(),
            new FixedClock(),
            NullLogger<AuditLogWriter>.Instance);

        var act = async () => await writer.RecordAsync("FileUpload", "some-id", "StoredObject", AuditOutcome.Success, null, CancellationToken.None);

        await act.Should().NotThrowAsync(
            "a caller like UploadFileService awaits RecordAsync without its own try/catch -- if this threw, every file operation would fail whenever the audit table/database was briefly unavailable, turning a non-critical logging path into an availability risk for the core product");
    }

    private sealed class FixedCurrentUser : ICurrentUser
    {
        public string UserId => "user-1";
        public bool IsAdmin => false;
        public string Role => "user";
    }

    private sealed class FixedCorrelationIdAccessor : ICorrelationIdAccessor
    {
        public string CorrelationId => "test-correlation-id";
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
