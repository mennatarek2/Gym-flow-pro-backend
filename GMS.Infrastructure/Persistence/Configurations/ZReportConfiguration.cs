namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class ZReportConfiguration : IEntityTypeConfiguration<ZReport>
{
    public void Configure(EntityTypeBuilder<ZReport> builder)
    {
        builder.HasKey(z => z.Id);
        builder.Property(z => z.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(z => z.TenantId).IsRequired();
        builder.Property(z => z.ReportDate).HasColumnType("DATE").IsRequired();

        builder.Property(z => z.PayloadJson).IsRequired().HasColumnType("NVARCHAR(MAX)");
        builder.Property(z => z.PdfUrl).HasMaxLength(500).HasColumnType("NVARCHAR(500)");

        builder.Property(z => z.GeneratedAt).HasColumnType("DATETIME2").IsRequired();

        builder.Property(z => z.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(z => z.UpdatedAtUtc).HasColumnType("DATETIME2");
        builder.Property(z => z.IsDeleted).HasDefaultValue(false);

        builder.HasIndex(z => new { z.TenantId, z.ReportDate }).IsUnique();

        builder.HasOne(z => z.Tenant).WithMany()
            .HasForeignKey(z => z.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(z => z.GeneratedByUser).WithMany()
            .HasForeignKey(z => z.GeneratedByUserId).OnDelete(DeleteBehavior.SetNull);

        builder.ToTable("z_reports", tb =>
            tb.HasCheckConstraint("CK_z_reports_PayloadJson_IsJson", "ISJSON([PayloadJson]) = 1"));
    }
}
