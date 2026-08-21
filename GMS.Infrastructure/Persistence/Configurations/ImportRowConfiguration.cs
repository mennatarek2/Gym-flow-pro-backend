namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class ImportRowConfiguration : IEntityTypeConfiguration<ImportRow>
{
    public void Configure(EntityTypeBuilder<ImportRow> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.BatchId).IsRequired();
        builder.Property(r => r.RowNumber).IsRequired();

        builder.Property(r => r.RawJson).IsRequired().HasColumnType("NVARCHAR(MAX)");

        builder.Property(r => r.Status)
            .IsRequired().HasMaxLength(12).HasColumnType("VARCHAR(12)").HasDefaultValue("ok");

        builder.Property(r => r.ErrorCodes).HasMaxLength(400).HasColumnType("NVARCHAR(400)");
        builder.Property(r => r.CreatedMemberId);
        builder.Property(r => r.CreatedMembershipId);

        builder.Property(r => r.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(r => r.UpdatedAtUtc).HasColumnType("DATETIME2");
        builder.Property(r => r.IsDeleted).HasDefaultValue(false);

        builder.HasIndex(r => new { r.BatchId, r.RowNumber });

        builder.HasOne(r => r.Tenant).WithMany()
            .HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);

        // Sale -> SaleLine precedent: strict single-parent ownership, Cascade is safe here.
        builder.HasOne(r => r.Batch).WithMany(b => b.Rows)
            .HasForeignKey(r => r.BatchId).OnDelete(DeleteBehavior.Cascade);

        builder.ToTable("import_rows", tb =>
            tb.HasCheckConstraint("CK_import_rows_Status", "Status IN ('ok','error','imported','skipped')"));
    }
}
