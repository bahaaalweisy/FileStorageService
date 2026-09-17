using FileStorage.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileStorage.Infrastructure.Persistence.Configurations;

public class StoredObjectConfiguration : IEntityTypeConfiguration<StoredObject>
{
    public void Configure(EntityTypeBuilder<StoredObject> builder)
    {
        builder.ToTable("StoredObjects");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Key)
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(x => x.Key)
            .IsUnique();

        builder.Property(x => x.OriginalName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.SizeBytes)
            .IsRequired();

        builder.Property(x => x.ContentType)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.Checksum)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Tags)
            .HasMaxLength(1024)
            .IsRequired(false);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.DeletedAtUtc)
            .IsRequired(false);

        builder.Property(x => x.Version)
            .IsRequired();

        builder.Property(x => x.CreatedByUserId)
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(x => new { x.CreatedByUserId, x.DeletedAtUtc, x.CreatedAtUtc, x.Id })
            .HasDatabaseName("IX_StoredObjects_Owner_Listing");

        builder.HasIndex(x => new { x.DeletedAtUtc, x.CreatedAtUtc, x.Id })
            .HasDatabaseName("IX_StoredObjects_Listing");

        builder.HasIndex(x => x.ContentType)
            .HasDatabaseName("IX_StoredObjects_ContentType");
    }
}
