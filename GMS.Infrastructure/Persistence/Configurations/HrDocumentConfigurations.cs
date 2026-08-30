namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class EmployeeDocumentConfiguration : IEntityTypeConfiguration<EmployeeDocument>
{
    public void Configure(EntityTypeBuilder<EmployeeDocument> builder)
    {
        builder.ToTable("employee_documents");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();

        builder.Property(d => d.TenantId).IsRequired();
        builder.Property(d => d.EmployeeId).IsRequired();
        builder.Property(d => d.DocumentType).IsRequired().HasMaxLength(30).HasColumnType("VARCHAR(30)");
        builder.Property(d => d.FileUrl).IsRequired().HasMaxLength(500).HasColumnType("NVARCHAR(500)");
        builder.Property(d => d.FileName).IsRequired().HasMaxLength(260).HasColumnType("NVARCHAR(260)");
        builder.Property(d => d.ContentType).IsRequired().HasMaxLength(100).HasColumnType("VARCHAR(100)");
        builder.Property(d => d.IssueDate).HasColumnType("DATE");
        builder.Property(d => d.ExpiryDate).HasColumnType("DATE");
        builder.Property(d => d.Notes).HasMaxLength(500).HasColumnType("NVARCHAR(500)");

        builder.Property(d => d.CreatedAtUtc).IsRequired();
        builder.Property(d => d.UpdatedAtUtc);
        builder.Property(d => d.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(d => new { d.TenantId, d.EmployeeId });
        builder.HasIndex(d => new { d.TenantId, d.ExpiryDate });

        builder.HasOne(d => d.Employee).WithMany().HasForeignKey(d => d.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
