using FileStorage.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileStorage.Infrastructure.Persistence.Configurations;

public class UploadSessionConfiguration : IEntityTypeConfiguration<UploadSession>
{
    public void Configure(EntityTypeBuilder<UploadSession> builder)
    {
        builder.ToTable("UploadSessions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OwnerUserId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.OriginalFileName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.ContentType)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.Tags)
            .HasMaxLength(1024)
            .IsRequired(false);

        builder.Property(x => x.TotalSizeBytes)
            .IsRequired();

        builder.Property(x => x.ReceivedBytes)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc)
            .IsRequired();

        builder.Property(x => x.ExpiresAtUtc)
            .IsRequired();

        builder.Property(x => x.FinalizedFileId)
            .IsRequired(false);

        builder.HasIndex(x => new { x.OwnerUserId, x.Status })
            .HasDatabaseName("IX_UploadSessions_Owner_Status");

        builder.HasIndex(x => new { x.Status, x.ExpiresAtUtc })
            .HasDatabaseName("IX_UploadSessions_Status_Expiry");
    }
}
