namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(b => b.TenantId).IsRequired();
        builder.Property(b => b.UploadedByUserId).IsRequired();

        builder.Property(b => b.FileName).IsRequired().HasMaxLength(200).HasColumnType("NVARCHAR(200)");
        builder.Property(b => b.FileBlobUrl).IsRequired().HasMaxLength(500).HasColumnType("NVARCHAR(500)");

        builder.Property(b => b.EntityScope)
            .IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)").HasDefaultValue("members_memberships");

        builder.Property(b => b.Status)
            .IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)").HasDefaultValue("validating");

        builder.Property(b => b.TotalRows).HasDefaultValue(0);
        builder.Property(b => b.OkRows).HasDefaultValue(0);
        builder.Property(b => b.ErrorRows).HasDefaultValue(0);

        builder.Property(b => b.MappingJson).HasColumnType("NVARCHAR(MAX)");
        builder.Property(b => b.CompletedAt).HasColumnType("DATETIME2");

        builder.Property(b => b.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(b => b.UpdatedAtUtc).HasColumnType("DATETIME2");
        builder.Property(b => b.IsDeleted).HasDefaultValue(false);

        builder.HasIndex(b => new { b.TenantId, b.Status });

        builder.HasOne(b => b.Tenant).WithMany()
            .HasForeignKey(b => b.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.UploadedByUser).WithMany()
            .HasForeignKey(b => b.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("import_batches", tb =>
        {
            tb.HasCheckConstraint("CK_import_batches_EntityScope", "EntityScope IN ('members_memberships')");
            tb.HasCheckConstraint("CK_import_batches_Status",
                "Status IN ('validating','dry_run_ready','importing','completed','rolled_back','failed')");
        });
    }
}
