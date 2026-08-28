namespace GMS.Platform.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Platform.Entities;

public class CommercialPlanConfiguration : IEntityTypeConfiguration<CommercialPlan>
{
    public void Configure(EntityTypeBuilder<CommercialPlan> builder)
    {
        builder.ToTable("commercial_plans", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_commercial_plans_tier",
                "[Tier] IN ('starter','growth','pro','enterprise')");
        });

        builder.HasKey(x => x.Tier);
        builder.Property(x => x.Tier).HasMaxLength(15).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.MonthlyPriceEgp).HasPrecision(12, 2);

        builder.HasIndex(x => x.IsDefault)
            .HasFilter("[IsDefault] = 1")
            .IsUnique()
            .HasDatabaseName("UX_commercial_plans_single_default");
    }
}

public class PlanChangeLogConfiguration : IEntityTypeConfiguration<PlanChangeLog>
{
    public void Configure(EntityTypeBuilder<PlanChangeLog> builder)
    {
        builder.ToTable("plan_change_log", "platform");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Tier).HasMaxLength(15).IsRequired();
        builder.Property(x => x.FieldName).HasMaxLength(64).IsRequired();
        builder.Property(x => x.OldValue).HasMaxLength(500);
        builder.Property(x => x.NewValue).HasMaxLength(500);
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();

        builder.HasIndex(x => new { x.Tier, x.CreatedAtUtc });
    }
}
