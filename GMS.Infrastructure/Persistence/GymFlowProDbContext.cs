namespace GMS.Infrastructure.Persistence;

using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using GMS.Core.Entities;
using GMS.Core.Entities.Identity;
using GMS.Core.Interfaces;

/// <summary>
/// GymFlow Pro Application DbContext for multi-tenant data persistence.
/// 
/// KEY FEATURES:
/// - ASP.NET Core Identity integration via IdentityDbContext
/// - Shared-schema multi-tenancy with tenant_id filtering on all entities
/// - Soft delete pattern with automatic IsDeleted query filters
/// - NEWSEQUENTIALID() for all PKs (optimized insert performance)
/// - EF Core 8 Fluent API configurations (no data annotations)
/// - Global query filters prevent cross-tenant data leakage
/// - Automatic timestamp management (CreatedAt, UpdatedAt)
/// - Azure SQL compatible (DATETIME2, NVARCHAR for Unicode)
/// 
/// MULTI-TENANCY LAYER 2: Query Filters
/// Every entity with TenantId gets filtered automatically unless IgnoreQueryFilters() is used.
/// This prevents accidental cross-tenant queries in normal operations.
/// IgnoreQueryFilters() should only be used in admin/migration contexts.
/// </summary>
public class GymFlowProDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    private readonly ITenantContext? _tenantContext;

    /// <summary>
    /// Entity types given a combined tenant+soft-delete filter in <see cref="ApplyGlobalQueryFilters"/>.
    /// <see cref="ApplySoftDeleteFilter"/> must skip these — EF Core does not merge multiple
    /// <c>HasQueryFilter</c> calls on the same entity; a second call replaces the first outright,
    /// which previously caused the tenant predicate to be silently dropped for every one of these types.
    /// </summary>
    private static readonly HashSet<Type> TenantScopedEntityTypes = new()
    {
        typeof(GymMember), typeof(MembershipPlan), typeof(Membership), typeof(GymAttendance),
        typeof(MemberInvitation), typeof(AppUser), typeof(Notification), typeof(AuditEvent),
        typeof(PromoCode), typeof(Sale), typeof(SaleLine), typeof(Invoice),
        typeof(Shift), typeof(CashMovement), typeof(ZReport),
        typeof(CashExpense),
        typeof(Refund), typeof(MemberCredit), typeof(CallOutcome), typeof(MemberFollowUp),
        typeof(ImportBatch), typeof(ImportRow),
        typeof(PaymentTransaction), typeof(AnalyticsSnapshot), typeof(GymAnalyticsSnapshot),
        typeof(RefreshToken), typeof(ReferralReward),
        typeof(ProductCategory), typeof(Product), typeof(Warehouse),
        typeof(StockMovement), typeof(StockBalance),
        typeof(StockAdjustment), typeof(StockAdjustmentLine),
        typeof(Supplier), typeof(PurchaseOrder), typeof(PurchaseOrderLine),
        typeof(GoodsReceipt), typeof(GoodsReceiptLine), typeof(ProductBatch),
        typeof(SupplierLedgerEntry),
        typeof(StockTransfer), typeof(StockTransferLine),
        typeof(StockCount), typeof(StockCountLine),
        typeof(MemberOrder), typeof(MemberOrderLine),
        typeof(Offer),
        typeof(Activity), typeof(ActivitySchedule), typeof(ActivitySession),
        typeof(ActivityBooking), typeof(PlanEntitlement),
        typeof(Department), typeof(Position), typeof(Employee), typeof(EmployeeContract),
        typeof(EmployeeShift), typeof(EmployeeScheduleAssignment), typeof(EmployeeAttendance),
        typeof(LeaveRequest), typeof(LeaveBalance),
        typeof(PayrollPeriod), typeof(PayrollLine), typeof(PayrollAdjustment),
        typeof(EmployeeDocument)
    };

    // DbSets for all domain entities
    public DbSet<Tenant> Tenants { get; set; } = null!;
    public DbSet<GymMember> GymMembers { get; set; } = null!;
    public DbSet<MemberAppActivationCode> MemberAppActivationCodes { get; set; } = null!;
    public DbSet<EmployeeAppActivationCode> EmployeeAppActivationCodes { get; set; } = null!;
    public DbSet<MembershipPlan> MembershipPlans { get; set; } = null!;
    public DbSet<Membership> Memberships { get; set; } = null!;
    public DbSet<GymAttendance> GymAttendances { get; set; } = null!;
    public DbSet<MemberInvitation> MemberInvitations { get; set; } = null!;
    public DbSet<AppUser> AppUsers { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<AnalyticsSnapshot> AnalyticsSnapshots { get; set; } = null!;
    public DbSet<GymAnalyticsSnapshot> GymAnalyticsSnapshots { get; set; } = null!;
    public DbSet<PaymentTransaction> PaymentTransactions { get; set; } = null!;
    public DbSet<Notification> Notifications { get; set; } = null!;
    public DbSet<AuditEvent> AuditEvents { get; set; } = null!;
    public DbSet<PromoCode> PromoCodes { get; set; } = null!;
    public DbSet<Sale> Sales { get; set; } = null!;
    public DbSet<SaleLine> SaleLines { get; set; } = null!;
    public DbSet<SaleIdempotencyKey> SaleIdempotencyKeys { get; set; } = null!;
    public DbSet<Invoice> Invoices { get; set; } = null!;
    public DbSet<InvoiceSequence> InvoiceSequences { get; set; } = null!;
    public DbSet<Shift> Shifts { get; set; } = null!;
    public DbSet<CashMovement> CashMovements { get; set; } = null!;
    public DbSet<CashExpense> CashExpenses { get; set; } = null!;
    public DbSet<ZReport> ZReports { get; set; } = null!;
    public DbSet<Refund> Refunds { get; set; } = null!;
    public DbSet<MemberCredit> MemberCredits { get; set; } = null!;
    public DbSet<ReferralReward> ReferralRewards { get; set; } = null!;
    public DbSet<CallOutcome> CallOutcomes { get; set; } = null!;
    public DbSet<MemberFollowUp> MemberFollowUps { get; set; } = null!;
    public DbSet<ImportBatch> ImportBatches { get; set; } = null!;
    public DbSet<ImportRow> ImportRows { get; set; } = null!;
    public DbSet<ProductCategory> ProductCategories { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<Warehouse> Warehouses { get; set; } = null!;
    public DbSet<StockMovement> StockMovements { get; set; } = null!;
    public DbSet<StockBalance> StockBalances { get; set; } = null!;
    public DbSet<StockAdjustment> StockAdjustments { get; set; } = null!;
    public DbSet<StockAdjustmentLine> StockAdjustmentLines { get; set; } = null!;
    public DbSet<Supplier> Suppliers { get; set; } = null!;
    public DbSet<PurchaseOrder> PurchaseOrders { get; set; } = null!;
    public DbSet<PurchaseOrderLine> PurchaseOrderLines { get; set; } = null!;
    public DbSet<GoodsReceipt> GoodsReceipts { get; set; } = null!;
    public DbSet<GoodsReceiptLine> GoodsReceiptLines { get; set; } = null!;
    public DbSet<ProductBatch> ProductBatches { get; set; } = null!;
    public DbSet<SupplierLedgerEntry> SupplierLedgerEntries { get; set; } = null!;
    public DbSet<StockTransfer> StockTransfers { get; set; } = null!;
    public DbSet<StockTransferLine> StockTransferLines { get; set; } = null!;
    public DbSet<StockCount> StockCounts { get; set; } = null!;
    public DbSet<StockCountLine> StockCountLines { get; set; } = null!;
    public DbSet<MemberOrder> MemberOrders { get; set; } = null!;
    public DbSet<MemberOrderLine> MemberOrderLines { get; set; } = null!;
    public DbSet<Offer> Offers { get; set; } = null!;
    public DbSet<Activity> Activities { get; set; } = null!;
    public DbSet<ActivitySchedule> ActivitySchedules { get; set; } = null!;
    public DbSet<ActivitySession> ActivitySessions { get; set; } = null!;
    public DbSet<ActivityBooking> ActivityBookings { get; set; } = null!;
    public DbSet<PlanEntitlement> PlanEntitlements { get; set; } = null!;
    public DbSet<Department> Departments { get; set; } = null!;
    public DbSet<Position> Positions { get; set; } = null!;
    public DbSet<Employee> Employees { get; set; } = null!;
    public DbSet<EmployeeContract> EmployeeContracts { get; set; } = null!;
    public DbSet<EmployeeShift> EmployeeShifts { get; set; } = null!;
    public DbSet<EmployeeScheduleAssignment> EmployeeScheduleAssignments { get; set; } = null!;
    public DbSet<EmployeeAttendance> EmployeeAttendances { get; set; } = null!;
    public DbSet<LeaveRequest> LeaveRequests { get; set; } = null!;
    public DbSet<LeaveBalance> LeaveBalances { get; set; } = null!;
    public DbSet<PayrollPeriod> PayrollPeriods { get; set; } = null!;
    public DbSet<PayrollLine> PayrollLines { get; set; } = null!;
    public DbSet<PayrollAdjustment> PayrollAdjustments { get; set; } = null!;
    public DbSet<EmployeeDocument> EmployeeDocuments { get; set; } = null!;

    /// <summary>
    /// Constructor allowing optional ITenantContext for startup scenarios.
    /// In production, the context is always available during HTTP requests.
    /// </summary>
    public GymFlowProDbContext(DbContextOptions<GymFlowProDbContext> options, ITenantContext? tenantContext = null)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure all entity types using Fluent API
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GymFlowProDbContext).Assembly);

        // Identity's ApplicationUser isn't covered by IEntityTypeConfiguration<T> conventions above.
        modelBuilder.Entity<ApplicationUser>(b =>
        {
            b.Property(u => u.PermissionsOverride).HasColumnType("nvarchar(max)");
        });

        // MULTI-TENANCY LAYER 2: Apply global query filters
        // This prevents cross-tenant data leakage at the query level
        ApplyGlobalQueryFilters(modelBuilder);

        // Configure soft delete filter
        ApplySoftDeleteFilter(modelBuilder);
    }

    /// <summary>
    /// Applies global tenant query filters to all multi-tenant entities.
    /// 
    /// CRITICAL: Every entity that has a TenantId property must be filtered.
    /// Without this, queries might return data from other tenants.
    /// 
    /// IMPORTANT: TenantContext is optional during startup/migrations.
    /// Query filters only apply when TenantContext is initialized within an HTTP request.
    /// 
    /// When to use IgnoreQueryFilters():
    /// - Admin operations that need to query all tenants
    /// - Data migration between tenants
    /// - Reporting across all tenants (with authorization checks)
    /// - System-wide maintenance tasks
    /// 
    /// ALWAYS verify tenant context is properly initialized before querying.
    /// </summary>
    private void ApplyGlobalQueryFilters(ModelBuilder modelBuilder)
    {
        // Only apply filters if tenant context is available
        if (_tenantContext == null)
            return;

        // Each filter below combines the tenant predicate AND the soft-delete predicate in a single
        // HasQueryFilter call (see TenantScopedEntityTypes doc comment for why they can't be split).

        // GymMember filter
        modelBuilder.Entity<GymMember>().HasQueryFilter(m =>
            m.TenantId == _tenantContext.TenantId && !m.IsDeleted);

        // MembershipPlan filter
        modelBuilder.Entity<MembershipPlan>().HasQueryFilter(p =>
            p.TenantId == _tenantContext.TenantId && !p.IsDeleted);

        // Membership filter
        modelBuilder.Entity<Membership>().HasQueryFilter(m =>
            m.TenantId == _tenantContext.TenantId && !m.IsDeleted);

        // GymAttendance filter
        modelBuilder.Entity<GymAttendance>().HasQueryFilter(a =>
            a.TenantId == _tenantContext.TenantId && !a.IsDeleted);

        // MemberInvitation filter
        modelBuilder.Entity<MemberInvitation>().HasQueryFilter(i =>
            i.TenantId == _tenantContext.TenantId && !i.IsDeleted);

        // AppUser filter
        modelBuilder.Entity<AppUser>().HasQueryFilter(u =>
            u.TenantId == _tenantContext.TenantId && !u.IsDeleted);

        // Notification filter
        modelBuilder.Entity<Notification>().HasQueryFilter(n =>
            n.TenantId == _tenantContext.TenantId && !n.IsDeleted);

        // AuditEvent filter (IsDeleted is always false in practice — audit rows are never deleted —
        // but it's still ANDed in here since this entity is BaseEntity like the rest)
        modelBuilder.Entity<AuditEvent>().HasQueryFilter(a =>
            a.TenantId == _tenantContext.TenantId && !a.IsDeleted);

        // PromoCode filter
        modelBuilder.Entity<PromoCode>().HasQueryFilter(p =>
            p.TenantId == _tenantContext.TenantId && !p.IsDeleted);

        // Sale filter
        modelBuilder.Entity<Sale>().HasQueryFilter(s =>
            s.TenantId == _tenantContext.TenantId && !s.IsDeleted);

        // SaleLine filter
        modelBuilder.Entity<SaleLine>().HasQueryFilter(l =>
            l.TenantId == _tenantContext.TenantId && !l.IsDeleted);

        // Invoice filter
        modelBuilder.Entity<Invoice>().HasQueryFilter(i =>
            i.TenantId == _tenantContext.TenantId && !i.IsDeleted);

        // Shift filter
        modelBuilder.Entity<Shift>().HasQueryFilter(s =>
            s.TenantId == _tenantContext.TenantId && !s.IsDeleted);

        // CashMovement filter
        modelBuilder.Entity<CashMovement>().HasQueryFilter(c =>
            c.TenantId == _tenantContext.TenantId && !c.IsDeleted);

        // ZReport filter
        modelBuilder.Entity<ZReport>().HasQueryFilter(z =>
            z.TenantId == _tenantContext.TenantId && !z.IsDeleted);

        // Refund filter
        modelBuilder.Entity<Refund>().HasQueryFilter(r =>
            r.TenantId == _tenantContext.TenantId && !r.IsDeleted);

        // MemberCredit filter (IsDeleted is always false in practice — entries are append-only and
        // never soft-deleted — but it's still ANDed in here since this entity is BaseEntity like the rest)
        modelBuilder.Entity<MemberCredit>().HasQueryFilter(c =>
            c.TenantId == _tenantContext.TenantId && !c.IsDeleted);

        modelBuilder.Entity<ReferralReward>().HasQueryFilter(r =>
            r.TenantId == _tenantContext.TenantId && !r.IsDeleted);

        // CallOutcome filter (append-only, IsDeleted always false in practice — same reasoning as MemberCredit)
        modelBuilder.Entity<CallOutcome>().HasQueryFilter(c =>
            c.TenantId == _tenantContext.TenantId && !c.IsDeleted);

        modelBuilder.Entity<MemberFollowUp>().HasQueryFilter(f =>
            f.TenantId == _tenantContext.TenantId && !f.IsDeleted);

        // ImportBatch / ImportRow filters
        modelBuilder.Entity<ImportBatch>().HasQueryFilter(b =>
            b.TenantId == _tenantContext.TenantId && !b.IsDeleted);

        modelBuilder.Entity<ImportRow>().HasQueryFilter(r =>
            r.TenantId == _tenantContext.TenantId && !r.IsDeleted);

        modelBuilder.Entity<PaymentTransaction>().HasQueryFilter(p =>
            p.TenantId == _tenantContext.TenantId && !p.IsDeleted);

        modelBuilder.Entity<AnalyticsSnapshot>().HasQueryFilter(s =>
            s.TenantId == _tenantContext.TenantId && !s.IsDeleted);

        modelBuilder.Entity<GymAnalyticsSnapshot>().HasQueryFilter(s =>
            s.TenantId == _tenantContext.TenantId && !s.IsDeleted);

        // Auth refresh looks up by token hash with IgnoreQueryFilters; revocation under ambient tenant uses this filter.
        modelBuilder.Entity<RefreshToken>().HasQueryFilter(t =>
            t.TenantId == _tenantContext.TenantId && !t.IsDeleted);

        modelBuilder.Entity<ProductCategory>().HasQueryFilter(c =>
            c.TenantId == _tenantContext.TenantId && !c.IsDeleted);

        modelBuilder.Entity<Product>().HasQueryFilter(p =>
            p.TenantId == _tenantContext.TenantId && !p.IsDeleted);

        modelBuilder.Entity<Warehouse>().HasQueryFilter(w =>
            w.TenantId == _tenantContext.TenantId && !w.IsDeleted);

        modelBuilder.Entity<StockMovement>().HasQueryFilter(m =>
            m.TenantId == _tenantContext.TenantId && !m.IsDeleted);

        modelBuilder.Entity<StockBalance>().HasQueryFilter(b =>
            b.TenantId == _tenantContext.TenantId && !b.IsDeleted);

        modelBuilder.Entity<StockAdjustment>().HasQueryFilter(a =>
            a.TenantId == _tenantContext.TenantId && !a.IsDeleted);

        modelBuilder.Entity<StockAdjustmentLine>().HasQueryFilter(l =>
            l.TenantId == _tenantContext.TenantId && !l.IsDeleted);

        modelBuilder.Entity<Supplier>().HasQueryFilter(s =>
            s.TenantId == _tenantContext.TenantId && !s.IsDeleted);
        modelBuilder.Entity<PurchaseOrder>().HasQueryFilter(p =>
            p.TenantId == _tenantContext.TenantId && !p.IsDeleted);
        modelBuilder.Entity<PurchaseOrderLine>().HasQueryFilter(l =>
            l.TenantId == _tenantContext.TenantId && !l.IsDeleted);
        modelBuilder.Entity<GoodsReceipt>().HasQueryFilter(g =>
            g.TenantId == _tenantContext.TenantId && !g.IsDeleted);
        modelBuilder.Entity<GoodsReceiptLine>().HasQueryFilter(l =>
            l.TenantId == _tenantContext.TenantId && !l.IsDeleted);
        modelBuilder.Entity<ProductBatch>().HasQueryFilter(b =>
            b.TenantId == _tenantContext.TenantId && !b.IsDeleted);
        modelBuilder.Entity<SupplierLedgerEntry>().HasQueryFilter(e =>
            e.TenantId == _tenantContext.TenantId && !e.IsDeleted);

        modelBuilder.Entity<StockTransfer>().HasQueryFilter(t =>
            t.TenantId == _tenantContext.TenantId && !t.IsDeleted);
        modelBuilder.Entity<StockTransferLine>().HasQueryFilter(l =>
            l.TenantId == _tenantContext.TenantId && !l.IsDeleted);

        modelBuilder.Entity<StockCount>().HasQueryFilter(c =>
            c.TenantId == _tenantContext.TenantId && !c.IsDeleted);
        modelBuilder.Entity<StockCountLine>().HasQueryFilter(l =>
            l.TenantId == _tenantContext.TenantId && !l.IsDeleted);

        modelBuilder.Entity<MemberOrder>().HasQueryFilter(o =>
            o.TenantId == _tenantContext.TenantId && !o.IsDeleted);
        modelBuilder.Entity<MemberOrderLine>().HasQueryFilter(l =>
            l.TenantId == _tenantContext.TenantId && !l.IsDeleted);

        modelBuilder.Entity<Offer>().HasQueryFilter(o =>
            o.TenantId == _tenantContext.TenantId && !o.IsDeleted);

        modelBuilder.Entity<CashExpense>().HasQueryFilter(e =>
            e.TenantId == _tenantContext.TenantId && !e.IsDeleted);

        modelBuilder.Entity<Activity>().HasQueryFilter(a =>
            a.TenantId == _tenantContext.TenantId && !a.IsDeleted);
        modelBuilder.Entity<ActivitySchedule>().HasQueryFilter(s =>
            s.TenantId == _tenantContext.TenantId && !s.IsDeleted);
        modelBuilder.Entity<ActivitySession>().HasQueryFilter(s =>
            s.TenantId == _tenantContext.TenantId && !s.IsDeleted);
        modelBuilder.Entity<ActivityBooking>().HasQueryFilter(b =>
            b.TenantId == _tenantContext.TenantId && !b.IsDeleted);
        modelBuilder.Entity<PlanEntitlement>().HasQueryFilter(e =>
            e.TenantId == _tenantContext.TenantId && !e.IsDeleted);

        modelBuilder.Entity<Department>().HasQueryFilter(d =>
            d.TenantId == _tenantContext.TenantId && !d.IsDeleted);
        modelBuilder.Entity<Position>().HasQueryFilter(p =>
            p.TenantId == _tenantContext.TenantId && !p.IsDeleted);
        modelBuilder.Entity<Employee>().HasQueryFilter(e =>
            e.TenantId == _tenantContext.TenantId && !e.IsDeleted);
        modelBuilder.Entity<EmployeeContract>().HasQueryFilter(c =>
            c.TenantId == _tenantContext.TenantId && !c.IsDeleted);

        modelBuilder.Entity<EmployeeShift>().HasQueryFilter(s =>
            s.TenantId == _tenantContext.TenantId && !s.IsDeleted);
        modelBuilder.Entity<EmployeeScheduleAssignment>().HasQueryFilter(a =>
            a.TenantId == _tenantContext.TenantId && !a.IsDeleted);
        modelBuilder.Entity<EmployeeAttendance>().HasQueryFilter(a =>
            a.TenantId == _tenantContext.TenantId && !a.IsDeleted);

        modelBuilder.Entity<LeaveRequest>().HasQueryFilter(l =>
            l.TenantId == _tenantContext.TenantId && !l.IsDeleted);
        modelBuilder.Entity<LeaveBalance>().HasQueryFilter(b =>
            b.TenantId == _tenantContext.TenantId && !b.IsDeleted);

        modelBuilder.Entity<PayrollPeriod>().HasQueryFilter(p =>
            p.TenantId == _tenantContext.TenantId && !p.IsDeleted);
        modelBuilder.Entity<PayrollLine>().HasQueryFilter(l =>
            l.TenantId == _tenantContext.TenantId && !l.IsDeleted);
        modelBuilder.Entity<PayrollAdjustment>().HasQueryFilter(a =>
            a.TenantId == _tenantContext.TenantId && !a.IsDeleted);

        modelBuilder.Entity<EmployeeDocument>().HasQueryFilter(d =>
            d.TenantId == _tenantContext.TenantId && !d.IsDeleted);

        // NOTE: SaleIdempotencyKey and InvoiceSequence have no global query filter — neither is a
        // BaseEntity (no IsDeleted) and both are always queried by an explicit TenantId, per their
        // own doc comments. InvoiceSequence in particular is read/written from Hangfire job scopes
        // where no ambient ITenantContext exists at all.
        // NOTE: Tenant entity is NOT filtered because it needs to be queried without tenant context
        // NOTE: Identity ApplicationUser (AspNetUsers) is NOT filtered — login/admin must query by
        // TenantId explicitly (see AdminService FindStaffUserAsync). Domain AppUser IS filtered.
    }

    /// <summary>
    /// Applies a soft-delete-only filter to every remaining BaseEntity type not already covered by
    /// <see cref="ApplyGlobalQueryFilters"/>. Skips <see cref="TenantScopedEntityTypes"/> when a tenant
    /// context is present, since those already got a combined filter and a second HasQueryFilter call
    /// would silently replace it.
    /// </summary>
    private void ApplySoftDeleteFilter(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            if (_tenantContext != null && TenantScopedEntityTypes.Contains(entityType.ClrType))
                continue;

            var parameter = Expression.Parameter(entityType.ClrType);
            var propertyAccess = Expression.Property(parameter, nameof(BaseEntity.IsDeleted));
            var notExpression = Expression.Not(propertyAccess);
            var lambda = Expression.Lambda(notExpression, parameter);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries<BaseEntity>();

        foreach (var entry in entries)
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = DateTime.UtcNow;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAtUtc = DateTime.UtcNow;
                    break;

                case EntityState.Deleted:
                    // Soft delete: set IsDeleted flag instead of hard delete
                    entry.Entity.IsDeleted = true;
                    entry.State = EntityState.Modified;
                    break;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
