namespace GMS.Platform.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Platform.Entities;

public class UsageCounterConfiguration : IEntityTypeConfiguration<UsageCounter>
{
    public void Configure(EntityTypeBuilder<UsageCounter> builder)
    {
        builder.ToTable("usage_counters", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_usage_counters_metric",
                "[Metric] IN ('active_members','whatsapp_messages','staff_seats','branches')");
            t.HasCheckConstraint(
                "CK_usage_counters_period",
                "LEN([Period]) = 7 AND [Period] LIKE '[0-9][0-9][0-9][0-9]-[0-9][0-9]'");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Period).HasMaxLength(7).IsRequired();
        builder.Property(x => x.Metric).HasMaxLength(20).IsRequired();
        builder.Property(x => x.OverageBilledEgp).HasPrecision(12, 2);

        builder.HasIndex(x => new { x.TenantId, x.Period, x.Metric })
            .IsUnique()
            .HasDatabaseName("UX_usage_counters_tenant_period_metric");
    }
}

public class FeatureOverrideConfiguration : IEntityTypeConfiguration<FeatureOverride>
{
    public void Configure(EntityTypeBuilder<FeatureOverride> builder)
    {
        builder.ToTable("feature_overrides", "platform");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FeatureKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.FeatureKey, x.ExpiresAtUtc });
        builder.HasIndex(x => x.GrantedByPlatformUserId);
    }
}

public class TierFeatureMapConfiguration : IEntityTypeConfiguration<TierFeatureMap>
{
    public void Configure(EntityTypeBuilder<TierFeatureMap> builder)
    {
        builder.ToTable("tier_feature_map", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_tier_feature_map_tier",
                "[Tier] IN ('starter','growth','pro','enterprise')");
        });

        builder.HasKey(x => new { x.Tier, x.FeatureKey });
        builder.Property(x => x.Tier).HasMaxLength(15).IsRequired();
        builder.Property(x => x.FeatureKey).HasMaxLength(64).IsRequired();
    }
}
