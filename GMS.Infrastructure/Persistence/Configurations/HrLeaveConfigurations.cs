namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> builder)
    {
        builder.ToTable("leave_requests");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();

        builder.Property(l => l.TenantId).IsRequired();
        builder.Property(l => l.EmployeeId).IsRequired();
        builder.Property(l => l.LeaveType).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(l => l.StartDate).IsRequired().HasColumnType("DATE");
        builder.Property(l => l.EndDate).IsRequired().HasColumnType("DATE");
        builder.Property(l => l.DurationDays).IsRequired().HasColumnType("DECIMAL(6,2)");
        builder.Property(l => l.Reason).HasMaxLength(500).HasColumnType("NVARCHAR(500)");
        builder.Property(l => l.Status).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(l => l.RequestedAtUtc).IsRequired();
        builder.Property(l => l.ReviewNotes).HasMaxLength(500).HasColumnType("NVARCHAR(500)");

        builder.Property(l => l.CreatedAtUtc).IsRequired();
        builder.Property(l => l.UpdatedAtUtc);
        builder.Property(l => l.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(l => new { l.TenantId, l.EmployeeId, l.Status });
        builder.HasIndex(l => new { l.TenantId, l.StartDate, l.EndDate });

        builder.HasOne(l => l.Employee).WithMany().HasForeignKey(l => l.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class LeaveBalanceConfiguration : IEntityTypeConfiguration<LeaveBalance>
{
    public void Configure(EntityTypeBuilder<LeaveBalance> builder)
    {
        builder.ToTable("leave_balances");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();

        builder.Property(b => b.TenantId).IsRequired();
        builder.Property(b => b.EmployeeId).IsRequired();
        builder.Property(b => b.LeaveType).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(b => b.Year).IsRequired();
        builder.Property(b => b.EntitledDays).IsRequired().HasColumnType("DECIMAL(6,2)");
        builder.Property(b => b.UsedDays).IsRequired().HasColumnType("DECIMAL(6,2)").HasDefaultValue(0m);

        builder.Property(b => b.CreatedAtUtc).IsRequired();
        builder.Property(b => b.UpdatedAtUtc);
        builder.Property(b => b.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(b => new { b.TenantId, b.EmployeeId, b.LeaveType, b.Year }).IsUnique();

        builder.HasOne(b => b.Employee).WithMany().HasForeignKey(b => b.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
