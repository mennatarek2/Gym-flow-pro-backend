namespace GMS.Platform.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Platform.Entities;

public class PlatformSubscriptionConfiguration : IEntityTypeConfiguration<PlatformSubscription>
{
    public void Configure(EntityTypeBuilder<PlatformSubscription> builder)
    {
        builder.ToTable("subscriptions", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_subscriptions_plan_tier",
                "[PlanTier] IN ('starter','growth','pro','enterprise')");
            t.HasCheckConstraint(
                "CK_subscriptions_status",
                "[Status] IN ('trialing','active','past_due','suspended','cancelled')");
            t.HasCheckConstraint(
                "CK_subscriptions_billing_cycle",
                "[BillingCycle] IN ('monthly','annual')");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PlanTier).HasMaxLength(15).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(12).IsRequired();
        builder.Property(x => x.BillingCycle).HasMaxLength(10).IsRequired();
        builder.Property(x => x.PriceEgp).HasPrecision(12, 2);
        builder.Property(x => x.CancelAtPeriodEnd).HasDefaultValue(false);
        builder.Property(x => x.AutoRenewOptIn).HasDefaultValue(false);
        builder.Property(x => x.SavedCardToken).HasMaxLength(256);

        // One live subscription per tenant — historical cancelled/suspended rows allowed.
        builder.HasIndex(x => x.TenantId)
            .IsUnique()
            .HasFilter("[Status] IN ('trialing','active','past_due')")
            .HasDatabaseName("UX_subscriptions_tenant_live");

        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CurrentPeriodEnd);

        builder.HasMany(x => x.Changes)
            .WithOne(x => x.Subscription!)
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class SubscriptionChangeConfiguration : IEntityTypeConfiguration<SubscriptionChange>
{
    public void Configure(EntityTypeBuilder<SubscriptionChange> builder)
    {
        builder.ToTable("subscription_changes", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_subscription_changes_change_type",
                "[ChangeType] IN ('upgrade','downgrade','cycle_change','reactivation','cancellation','trial_start','trial_extend','past_due','suspension')");
            t.HasCheckConstraint(
                "CK_subscription_changes_initiated_by",
                "[InitiatedBy] IN ('self_serve','platform_admin','system')");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ChangeType).HasMaxLength(15).IsRequired();
        builder.Property(x => x.FromTier).HasMaxLength(15);
        builder.Property(x => x.ToTier).HasMaxLength(15);
        builder.Property(x => x.InitiatedBy).HasMaxLength(15).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.ProratedAmountEgp).HasPrecision(12, 2);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.EffectiveAtUtc);
        builder.HasIndex(x => x.CreatedAtUtc);
    }
}
