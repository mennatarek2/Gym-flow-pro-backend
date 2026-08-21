namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class CallOutcomeConfiguration : IEntityTypeConfiguration<CallOutcome>
{
    public void Configure(EntityTypeBuilder<CallOutcome> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.UserId).IsRequired();

        builder.Property(c => c.Outcome).IsRequired().HasMaxLength(24).HasColumnType("VARCHAR(24)");
        builder.Property(c => c.Note).HasMaxLength(300).HasColumnType("NVARCHAR(300)");
        builder.Property(c => c.NextAction).HasMaxLength(40).HasColumnType("VARCHAR(40)");
        builder.Property(c => c.NextActionAtUtc).HasColumnType("DATETIME2");

        builder.Property(c => c.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(c => c.UpdatedAtUtc).HasColumnType("DATETIME2");
        builder.Property(c => c.IsDeleted).HasDefaultValue(false);

        builder.HasIndex(c => new { c.TenantId, c.MembershipId });
        builder.HasIndex(c => new { c.TenantId, c.FollowUpId });
        builder.HasIndex(c => new { c.TenantId, c.MemberId });

        builder.HasOne(c => c.Tenant).WithMany()
            .HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.FollowUp).WithMany()
            .HasForeignKey(c => c.FollowUpId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Member).WithMany()
            .HasForeignKey(c => c.MemberId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Membership).WithMany()
            .HasForeignKey(c => c.MembershipId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.User).WithMany()
            .HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("call_outcomes", tb =>
            tb.HasCheckConstraint("CK_call_outcomes_Outcome",
                "Outcome IN ('contacted','renewed','declined','no_answer','reached','busy','wrong_number','not_interested','will_visit','needs_follow_up')"));
    }
}
