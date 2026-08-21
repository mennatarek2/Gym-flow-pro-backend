namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class MemberCreditConfiguration : IEntityTypeConfiguration<MemberCredit>
{
    public void Configure(EntityTypeBuilder<MemberCredit> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.MemberId).IsRequired();

        builder.Property(c => c.Amount).HasColumnType("DECIMAL(12,2)").IsRequired();
        builder.Property(c => c.EntryType).IsRequired().HasMaxLength(15).HasColumnType("VARCHAR(15)");
        builder.Property(c => c.ReferenceId);
        builder.Property(c => c.Reason).HasMaxLength(200).HasColumnType("NVARCHAR(200)");
        builder.Property(c => c.CreatedByUserId).IsRequired();

        builder.Property(c => c.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(c => c.UpdatedAtUtc).HasColumnType("DATETIME2");
        builder.Property(c => c.IsDeleted).HasDefaultValue(false);

        builder.HasIndex(c => new { c.TenantId, c.MemberId });

        builder.HasOne(c => c.Tenant).WithMany()
            .HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Member).WithMany()
            .HasForeignKey(c => c.MemberId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.CreatedByUser).WithMany()
            .HasForeignKey(c => c.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("member_credits", tb =>
            tb.HasCheckConstraint("CK_member_credits_EntryType",
                "EntryType IN ('refund','payment_use','adjustment','referral_reward')"));
    }
}
