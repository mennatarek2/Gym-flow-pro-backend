namespace GMS.Platform.Persistence;

using Microsoft.EntityFrameworkCore;
using GMS.Platform.Entities;

/// <summary>
/// Control-plane DbContext. NO tenant global query filters.
/// Never construct from a request carrying a tenant JWT — only platform-admin auth.
/// </summary>
public class PlatformDbContext : DbContext
{
    public PlatformDbContext(DbContextOptions<PlatformDbContext> options) : base(options)
    {
    }

    public DbSet<PlatformAdminUser> PlatformAdminUsers => Set<PlatformAdminUser>();
    public DbSet<PlatformAuditLog> PlatformAuditLogs => Set<PlatformAuditLog>();
    public DbSet<PlatformSubscription> Subscriptions => Set<PlatformSubscription>();
    public DbSet<SubscriptionChange> SubscriptionChanges => Set<SubscriptionChange>();
    public DbSet<PlatformInvoice> PlatformInvoices => Set<PlatformInvoice>();
    public DbSet<PlatformInvoiceSequence> PlatformInvoiceSequences => Set<PlatformInvoiceSequence>();
    public DbSet<PlatformPaymentEvent> PlatformPaymentEvents => Set<PlatformPaymentEvent>();
    public DbSet<UsageCounter> UsageCounters => Set<UsageCounter>();
    public DbSet<FeatureOverride> FeatureOverrides => Set<FeatureOverride>();
    public DbSet<TierFeatureMap> TierFeatureMaps => Set<TierFeatureMap>();
    public DbSet<AutomationEnrollment> AutomationEnrollments => Set<AutomationEnrollment>();
    public DbSet<PriceOverride> PriceOverrides => Set<PriceOverride>();
    public DbSet<TenantHealthScore> TenantHealthScores => Set<TenantHealthScore>();
    public DbSet<RiskQueueOutcome> RiskQueueOutcomes => Set<RiskQueueOutcome>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("platform");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlatformDbContext).Assembly);

        // Explicit: zero tenant-scoped HasQueryFilter calls on this model.
        base.OnModelCreating(modelBuilder);
    }
}
