namespace GMS.Platform.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Platform.Entities;

public class PriceOverrideConfiguration : IEntityTypeConfiguration<PriceOverride>
{
    public void Configure(EntityTypeBuilder<PriceOverride> builder)
    {
        builder.ToTable("price_overrides", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_price_overrides_discount_type",
                "[DiscountType] IN ('percent','fixed')");
            t.HasCheckConstraint(
                "CK_price_overrides_value",
                "[Value] >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.DiscountType).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Value).HasPrecision(12, 2);
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.ExpiresAtUtc });
    }
}

public class TenantHealthScoreConfiguration : IEntityTypeConfiguration<TenantHealthScore>
{
    public void Configure(EntityTypeBuilder<TenantHealthScore> builder)
    {
        builder.ToTable("tenant_health_scores", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_tenant_health_scores_risk_band",
                "[RiskBand] IN ('healthy','watch','at_risk','critical')");
            t.HasCheckConstraint(
                "CK_tenant_health_scores_score",
                "[Score] >= 0 AND [Score] <= 100");
        });

        builder.HasKey(x => x.TenantId);
        builder.Property(x => x.RiskBand).HasMaxLength(10).IsRequired();
        builder.Property(x => x.ContributingFactorsJson)
            .HasColumnName("contributing_factors")
            .HasColumnType("nvarchar(max)");
        builder.Property(x => x.ComputedAtUtc).HasColumnName("computed_at");
        builder.HasIndex(x => x.RiskBand);
        builder.HasIndex(x => new { x.RiskBand, x.Score });
    }
}

public class RiskQueueOutcomeConfiguration : IEntityTypeConfiguration<RiskQueueOutcome>
{
    public void Configure(EntityTypeBuilder<RiskQueueOutcome> builder)
    {
        builder.ToTable("risk_queue_outcomes", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_risk_queue_outcomes_outcome",
                "[Outcome] IN ('contacted','retained','churned','no_answer','watching')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Outcome).HasMaxLength(15).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(300);

        builder.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
    }
}
