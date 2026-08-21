namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class MemberFollowUpConfiguration : IEntityTypeConfiguration<MemberFollowUp>
{
    public void Configure(EntityTypeBuilder<MemberFollowUp> builder)
    {
        builder.ToTable("member_follow_ups", tb =>
        {
            tb.HasCheckConstraint("CK_member_follow_ups_Reason",
                "Reason IN ('renewal','trial','payment','welcome','inactive','offer','custom')");
            tb.HasCheckConstraint("CK_member_follow_ups_Source",
                "Source IN ('system','manual')");
            tb.HasCheckConstraint("CK_member_follow_ups_Priority",
                "Priority IN ('high','medium','low')");
            tb.HasCheckConstraint("CK_member_follow_ups_Status",
                "Status IN ('pending','in_progress','contacted','no_answer','completed','cancelled')");
        });

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(f => f.TenantId).IsRequired();
        builder.Property(f => f.MemberId).IsRequired();
        builder.Property(f => f.Reason).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(f => f.Source).IsRequired().HasMaxLength(10).HasColumnType("VARCHAR(10)");
        builder.Property(f => f.SourceKey).IsRequired().HasMaxLength(80).HasColumnType("VARCHAR(80)");
        builder.Property(f => f.Priority).IsRequired().HasMaxLength(10).HasColumnType("VARCHAR(10)");
        builder.Property(f => f.Status).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(f => f.DueAtUtc).HasColumnType("DATETIME2").IsRequired();
        builder.Property(f => f.NextAction).HasMaxLength(40).HasColumnType("VARCHAR(40)");
        builder.Property(f => f.NextActionAtUtc).HasColumnType("DATETIME2");
        builder.Property(f => f.RelatedType).HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(f => f.Why).HasMaxLength(240).HasColumnType("NVARCHAR(240)");
        builder.Property(f => f.Notes).HasMaxLength(500).HasColumnType("NVARCHAR(500)");
        builder.Property(f => f.CompletedAtUtc).HasColumnType("DATETIME2");

        builder.Property(f => f.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();
        builder.Property(f => f.UpdatedAtUtc).HasColumnType("DATETIME2");
        builder.Property(f => f.IsDeleted).HasDefaultValue(false);

        builder.HasIndex(f => new { f.TenantId, f.DueAtUtc, f.Status });
        builder.HasIndex(f => new { f.TenantId, f.MemberId });
        builder.HasIndex(f => new { f.TenantId, f.SourceKey })
            .HasFilter("[IsDeleted] = 0 AND [Status] IN ('pending','in_progress','contacted','no_answer')")
            .IsUnique();

        builder.HasOne(f => f.Tenant).WithMany()
            .HasForeignKey(f => f.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(f => f.Member).WithMany()
            .HasForeignKey(f => f.MemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(f => f.Membership).WithMany()
            .HasForeignKey(f => f.MembershipId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(f => f.AssignedToUser).WithMany()
            .HasForeignKey(f => f.AssignedToUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
