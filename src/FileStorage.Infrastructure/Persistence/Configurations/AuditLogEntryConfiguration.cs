using FileStorage.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileStorage.Infrastructure.Persistence.Configurations;

public class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("AuditLogEntries");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ActorUserId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.ActorRole)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Operation)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.ResourceId)
            .HasMaxLength(64)
            .IsRequired(false);

        builder.Property(x => x.ResourceType)
            .HasMaxLength(32)
            .IsRequired(false);

        builder.Property(x => x.Outcome)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(x => x.Detail)
            .HasMaxLength(512)
            .IsRequired(false);

        builder.Property(x => x.CorrelationId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.TimestampUtc)
            .IsRequired();

        builder.HasIndex(x => new { x.TimestampUtc, x.Id })
            .HasDatabaseName("IX_AuditLogEntries_Timestamp");

        builder.HasIndex(x => new { x.ActorUserId, x.TimestampUtc, x.Id })
            .HasDatabaseName("IX_AuditLogEntries_Actor");

        builder.HasIndex(x => new { x.ResourceId, x.TimestampUtc })
            .HasDatabaseName("IX_AuditLogEntries_Resource");

        builder.HasIndex(x => new { x.Operation, x.TimestampUtc })
            .HasDatabaseName("IX_AuditLogEntries_Operation");
    }
}
