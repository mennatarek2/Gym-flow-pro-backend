namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.MemberId);
        builder.Property(p => p.MembershipId);

        builder.Property(p => p.Gateway).HasMaxLength(20).IsRequired();
        builder.Property(p => p.ExternalRef).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Amount).HasColumnType("DECIMAL(14,2)").IsRequired();
        builder.Property(p => p.Currency).HasMaxLength(5).HasDefaultValue("EGP");
        builder.Property(p => p.Status).HasMaxLength(20).HasDefaultValue("success");
        builder.Property(p => p.RawPayload).HasColumnType("NVARCHAR(MAX)");
        builder.Property(p => p.HmacVerified).HasDefaultValue(false);
        builder.Property(p => p.PaidAtUtc).IsRequired();
        builder.Property(p => p.IsDeleted).HasDefaultValue(false);

        // SaleId's FK constraint is still deferred (not part of this migration).
        builder.Property(p => p.SaleId);
        builder.Property(p => p.ShiftId);

        builder.Property(p => p.ReceivedByUserId);
        builder.Property(p => p.Method).HasMaxLength(20).HasColumnType("VARCHAR(20)");

        // Unique index on ExternalRef for idempotency
        builder.HasIndex(p => p.ExternalRef).IsUnique();

        // Composite indexes
        builder.HasIndex(p => new { p.TenantId, p.MemberId });
        builder.HasIndex(p => new { p.TenantId, p.Gateway });

        // Navigation
        builder.HasOne(p => p.Tenant).WithMany()
            .HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Member).WithMany()
            .HasForeignKey(p => p.MemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Membership).WithMany()
            .HasForeignKey(p => p.MembershipId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.ReceivedByUser).WithMany()
            .HasForeignKey(p => p.ReceivedByUserId).OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.Shift).WithMany()
            .HasForeignKey(p => p.ShiftId).OnDelete(DeleteBehavior.SetNull);

        builder.ToTable("payment_transactions");
    }
}
