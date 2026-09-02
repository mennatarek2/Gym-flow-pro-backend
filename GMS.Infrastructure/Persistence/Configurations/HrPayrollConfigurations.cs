namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class PayrollPeriodConfiguration : IEntityTypeConfiguration<PayrollPeriod>
{
    public void Configure(EntityTypeBuilder<PayrollPeriod> builder)
    {
        builder.ToTable("payroll_periods");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.Year).IsRequired();
        builder.Property(p => p.Month).IsRequired();
        builder.Property(p => p.Status).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");

        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedAtUtc);
        builder.Property(p => p.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(p => new { p.TenantId, p.Year, p.Month }).IsUnique();

        builder.HasOne(p => p.Tenant).WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PayrollLineConfiguration : IEntityTypeConfiguration<PayrollLine>
{
    public void Configure(EntityTypeBuilder<PayrollLine> builder)
    {
        builder.ToTable("payroll_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();

        builder.Property(l => l.TenantId).IsRequired();
        builder.Property(l => l.PayrollPeriodId).IsRequired();
        builder.Property(l => l.EmployeeId).IsRequired();
        builder.Property(l => l.BasicSalary).IsRequired().HasColumnType("DECIMAL(14,2)");
        builder.Property(l => l.OvertimeAmount).IsRequired().HasColumnType("DECIMAL(14,2)").HasDefaultValue(0m);
        builder.Property(l => l.BonusAmount).IsRequired().HasColumnType("DECIMAL(14,2)").HasDefaultValue(0m);
        builder.Property(l => l.AllowanceAmount).IsRequired().HasColumnType("DECIMAL(14,2)").HasDefaultValue(0m);
        builder.Property(l => l.DeductionAmount).IsRequired().HasColumnType("DECIMAL(14,2)").HasDefaultValue(0m);
        builder.Property(l => l.NetSalary).IsRequired().HasColumnType("DECIMAL(14,2)");

        builder.Property(l => l.CreatedAtUtc).IsRequired();
        builder.Property(l => l.UpdatedAtUtc);
        builder.Property(l => l.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(l => new { l.TenantId, l.PayrollPeriodId, l.EmployeeId }).IsUnique();
        builder.HasIndex(l => new { l.TenantId, l.EmployeeId });

        builder.HasOne(l => l.PayrollPeriod).WithMany(p => p.Lines).HasForeignKey(l => l.PayrollPeriodId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(l => l.Employee).WithMany().HasForeignKey(l => l.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.Contract).WithMany().HasForeignKey(l => l.ContractId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PayrollAdjustmentConfiguration : IEntityTypeConfiguration<PayrollAdjustment>
{
    public void Configure(EntityTypeBuilder<PayrollAdjustment> builder)
    {
        builder.ToTable("payroll_adjustments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();

        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.PayrollPeriodId).IsRequired();
        builder.Property(a => a.EmployeeId).IsRequired();
        builder.Property(a => a.Type).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(a => a.Amount).IsRequired().HasColumnType("DECIMAL(14,2)");
        builder.Property(a => a.Reason).HasMaxLength(500).HasColumnType("NVARCHAR(500)");

        builder.Property(a => a.CreatedAtUtc).IsRequired();
        builder.Property(a => a.UpdatedAtUtc);
        builder.Property(a => a.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(a => new { a.TenantId, a.PayrollPeriodId, a.EmployeeId });

        builder.HasOne(a => a.PayrollPeriod).WithMany().HasForeignKey(a => a.PayrollPeriodId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(a => a.Employee).WithMany().HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PayrollPaymentConfiguration : IEntityTypeConfiguration<PayrollPayment>
{
    public void Configure(EntityTypeBuilder<PayrollPayment> builder)
    {
        builder.ToTable("payroll_payments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.PayrollPeriodId).IsRequired();
        builder.Property(p => p.PayrollLineId).IsRequired();
        builder.Property(p => p.Amount).IsRequired().HasColumnType("DECIMAL(14,2)");
        builder.Property(p => p.PaidDate).IsRequired().HasColumnType("DATE");
        builder.Property(p => p.PaymentMethod).IsRequired().HasMaxLength(30).HasColumnType("VARCHAR(30)");
        builder.Property(p => p.Reference).HasMaxLength(200).HasColumnType("NVARCHAR(200)");
        builder.Property(p => p.Status).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(p => p.PaidByAppUserId).IsRequired();
        builder.Property(p => p.CashExpenseId).IsRequired();
        builder.Property(p => p.CashMovementId);
        builder.Property(p => p.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.HasIndex(p => new { p.TenantId, p.PayrollPeriodId, p.PaidDate });
        builder.HasOne(p => p.Tenant).WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.PayrollPeriod).WithMany().HasForeignKey(p => p.PayrollPeriodId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.PayrollLine).WithMany().HasForeignKey(p => p.PayrollLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.PaidByAppUser).WithMany().HasForeignKey(p => p.PaidByAppUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.CashExpense).WithMany().HasForeignKey(p => p.CashExpenseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.CashMovement).WithMany().HasForeignKey(p => p.CashMovementId).OnDelete(DeleteBehavior.SetNull);
    }
}
