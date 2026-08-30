namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public sealed class CashExpenseConfiguration : IEntityTypeConfiguration<CashExpense>
{
    public void Configure(EntityTypeBuilder<CashExpense> builder)
    {
        builder.HasKey(expense => expense.Id);
        builder.Property(expense => expense.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(expense => expense.TenantId).IsRequired();
        builder.Property(expense => expense.ExpenseDate).HasColumnType("date").IsRequired();
        builder.Property(expense => expense.Category)
            .HasMaxLength(80)
            .HasColumnType("nvarchar(80)")
            .IsRequired();
        builder.Property(expense => expense.Amount)
            .HasColumnType("decimal(12,2)")
            .IsRequired();
        builder.Property(expense => expense.Status)
            .HasMaxLength(20)
            .HasColumnType("varchar(20)")
            .IsRequired();
        builder.Property(expense => expense.Note)
            .HasMaxLength(500)
            .HasColumnType("nvarchar(500)");
        builder.Property(expense => expense.RecordedByUserId).IsRequired();
        builder.Property(expense => expense.CreatedAtUtc)
            .HasColumnType("datetime2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();
        builder.Property(expense => expense.UpdatedAtUtc).HasColumnType("datetime2");
        builder.Property(expense => expense.IsDeleted).HasDefaultValue(false);

        builder.HasIndex(expense => new { expense.TenantId, expense.ExpenseDate, expense.Status });
        builder.HasOne(expense => expense.Tenant)
            .WithMany()
            .HasForeignKey(expense => expense.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(expense => expense.RecordedByUser)
            .WithMany()
            .HasForeignKey(expense => expense.RecordedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("cash_expenses");
    }
}
