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
    public DbSet<CommercialPlan> CommercialPlans => Set<CommercialPlan>();
    public DbSet<PlanChangeLog> PlanChangeLogs => Set<PlanChangeLog>();
    public DbSet<AutomationEnrollment> AutomationEnrollments => Set<AutomationEnrollment>();
    public DbSet<PriceOverride> PriceOverrides => Set<PriceOverride>();
    public DbSet<TenantHealthScore> TenantHealthScores => Set<TenantHealthScore>();
    public DbSet<RiskQueueOutcome> RiskQueueOutcomes => Set<RiskQueueOutcome>();

    // HyMotion Local Lifetime licensing — separate concept from Subscriptions above, see
    // LocalLicense's class remarks for why these are not merged with SaaS billing.
    public DbSet<LocalLicense> LocalLicenses => Set<LocalLicense>();
    public DbSet<LocalLicenseChange> LocalLicenseChanges => Set<LocalLicenseChange>();
    public DbSet<LocalInstallation> LocalInstallations => Set<LocalInstallation>();
    public DbSet<LocalActivationAttempt> LocalActivationAttempts => Set<LocalActivationAttempt>();
    public DbSet<LocalLifecycleEvent> LocalLifecycleEvents => Set<LocalLifecycleEvent>();
    public DbSet<LocalOwnerRecoveryRequest> LocalOwnerRecoveries => Set<LocalOwnerRecoveryRequest>();

    public DbSet<PlatformCustomer> Customers => Set<PlatformCustomer>();
    public DbSet<PlatformCatalogProduct> CatalogProducts => Set<PlatformCatalogProduct>();
    public DbSet<PlatformContract> Contracts => Set<PlatformContract>();
    public DbSet<PlatformContractItem> ContractItems => Set<PlatformContractItem>();
    public DbSet<PlatformCustomerPayment> CustomerPayments => Set<PlatformCustomerPayment>();
    public DbSet<PlatformSupportTicket> SupportTickets => Set<PlatformSupportTicket>();
    public DbSet<DeskFeedback> DeskFeedback => Set<DeskFeedback>();
    public DbSet<PlatformNumberSequence> NumberSequences => Set<PlatformNumberSequence>();
    public DbSet<LocalSalesContractTerms> LocalSalesContractTerms => Set<LocalSalesContractTerms>();
    public DbSet<LocalSalesContractDocument> LocalSalesContractDocuments => Set<LocalSalesContractDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("platform");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlatformDbContext).Assembly);

        // Explicit: zero tenant-scoped HasQueryFilter calls on this model.
        base.OnModelCreating(modelBuilder);
    }
}
