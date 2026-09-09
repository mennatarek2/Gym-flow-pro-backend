# Database & EF Core Audit

Scope: `GMS.Infrastructure`, `GMS.Platform`, `GMS.Core/Entities`, `GMS.Api` startup/DI wiring. Read-only audit for the "Local Lifetime Edition" feasibility study. All claims below are backed by file:line citations; anything not directly verifiable is marked UNKNOWN — REQUIRES VERIFICATION.

---

## 1. DbContext(s)

There are **two separate `DbContext` classes**, and they point at the **same physical SQL Server database** via the same `DefaultConnection` string, differentiated only by schema. This is a critical fact for the local-deployment study: it is one database, not two.

### 1.1 `GymFlowProDbContext` (tenant/application data — GMS.Infrastructure)

- File: `D:\GMS\GMS\GMS.Infrastructure\Persistence\GymFlowProDbContext.cs`
- Declaration: `public class GymFlowProDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>` (line 29) — this is the ASP.NET Core Identity context (`AspNetUsers`, `AspNetRoles`, etc.) **plus** ~65 domain `DbSet<T>` properties (lines 70–142). Default schema `dbo`.
- Registration: `D:\GMS\GMS\GMS.Infrastructure\InfrastructureServiceExtensions.cs:40-44` — `services.AddDbContext<GymFlowProDbContext>(... options.UseSqlServer(connectionString, sqlOptions => sqlOptions.MigrationsAssembly(...)))`. **No `EnableRetryOnFailure`, no custom execution strategy, no interceptors registered.**
- Connection string source: `GMS.Api\Program.cs:27-28` — `configuration.GetConnectionString("DefaultConnection")`, throws if missing. Passed into `AddInfrastructure(connectionString, configuration)` at `Program.cs:86`.
- Provider: `Microsoft.EntityFrameworkCore.SqlServer` via `UseSqlServer(...)` (confirmed by call site above; package reference not independently re-verified but the API is SqlServer-specific).
- Interceptors: none found. `grep -r "AddInterceptors|IInterceptor|ISaveChangesInterceptor"` across `GMS.Infrastructure` returned no matches.
- `SaveChangesAsync` override: `GymFlowProDbContext.cs:436-463`. On every save it:
  - Stamps `CreatedAtUtc` on `Added` entities, `UpdatedAtUtc` on `Modified` entities (lines 444-451).
  - Special-cases `GymAttendance`: stamps `AttendanceDateCairo` from `CheckInAtUtc` via `MembershipOperational.ToCairoDate` (lines 446-447).
  - Converts any `EntityState.Deleted` into a **soft delete**: sets `IsDeleted = true` and flips state to `Modified` instead of issuing a `DELETE` (lines 454-457). This applies uniformly to every entity derived from `BaseEntity` — there is no hard-delete path through this context's `SaveChanges`.
- Global query filters (`HasQueryFilter`) — **the critical multi-tenancy mechanism**:
  - Applied in `ApplyGlobalQueryFilters` (lines 192-409), only when `_tenantContext != null` (constructor accepts an *optional* `ITenantContext?`, line 148, 31). During design-time/migrations there is no tenant context, so filters are skipped entirely at that time.
  - **62 entity types get an explicit combined `TenantId == ctx.TenantId && !IsDeleted` filter.** Full list (each is one `modelBuilder.Entity<T>().HasQueryFilter(...)` call, lines 202-400): `GymMember, MembershipPlan, Membership, GymAttendance, MemberInvitation, AppUser, Notification, AuditEvent, PromoCode, Sale, SaleLine, Invoice, Shift, CashMovement, ZReport, Refund, MemberCredit, ReferralReward, CallOutcome, MemberFollowUp, ImportBatch, ImportRow, PaymentTransaction, AnalyticsSnapshot, GymAnalyticsSnapshot, RefreshToken, ProductCategory, Product, Warehouse, StockMovement, StockBalance, StockAdjustment, StockAdjustmentLine, Supplier, PurchaseOrder, PurchaseOrderLine, GoodsReceipt, GoodsReceiptLine, ProductBatch, SupplierLedgerEntry, StockTransfer, StockTransferLine, StockCount, StockCountLine, MemberOrder, MemberOrderLine, Offer, CashExpense, Activity, ActivitySchedule, ActivitySession, ActivityBooking, PlanEntitlement, Department, Position, Employee, EmployeeContract, EmployeeShift, EmployeeScheduleAssignment, EmployeeAttendance, LeaveRequest, LeaveBalance, PayrollPeriod, PayrollLine, PayrollAdjustment, PayrollPayment, EmployeeDocument`.
  - `ApplySoftDeleteFilter` (lines 417-434) then walks **every** `BaseEntity`-derived type in the model and applies a soft-delete-only filter (`!IsDeleted`) to any type *not* already in the `TenantScopedEntityTypes` hash set (lines 39-67) — this exists specifically because EF Core replaces (not merges) a second `HasQueryFilter` call on the same entity (see doc comment, lines 34-38), so the two passes are carefully partitioned to avoid silently dropping the tenant predicate.
  - **Entities explicitly and deliberately NOT tenant-filtered** (per the NOTE block, lines 402-408):
    - `SaleIdempotencyKey`, `InvoiceSequence` — not `BaseEntity` (no `IsDeleted`), queried by explicit `TenantId` in code; `InvoiceSequence` is touched from Hangfire jobs with no ambient tenant context at all.
    - `Tenant` itself — must be queryable without a tenant context (it *is* the tenant).
    - `ApplicationUser` (`AspNetUsers`, Identity) — **not tenant-filtered**. Login/admin code must filter by `TenantId` manually (see §5 for a concrete example). The parallel domain entity `AppUser` **is** filtered.
  - Entities that have `TenantId` but are seen in the codebase without a corresponding explicit tenant filter in the list above: **none found** beyond the two justified exceptions above — coverage appears complete for `BaseEntity`-derived, tenant-scoped types.

### 1.2 `PlatformDbContext` (control-plane / billing data — GMS.Platform)

- File: `D:\GMS\GMS\GMS.Platform\Persistence\PlatformDbContext.cs`
- Declaration: plain `DbContext` (line 10), **not** an `IdentityDbContext` — platform admin auth is custom (`PlatformAdminUser` + `PlatformTokenService`, not ASP.NET Identity).
- Default schema: `platform` (`modelBuilder.HasDefaultSchema("platform")`, line 35).
- Doc comment is explicit about intent: *"Control-plane DbContext. NO tenant global query filters. Never construct from a request carrying a tenant JWT — only platform-admin auth."* (lines 6-9). Confirmed: `OnModelCreating` (lines 33-40) applies configurations from assembly and calls `base.OnModelCreating` — **zero `HasQueryFilter` calls anywhere in `GMS.Platform`** (grep for `HasQueryFilter` in that project returned nothing).
- Registration: `D:\GMS\GMS\GMS.Platform\PlatformServiceExtensions.cs:25-32` — `services.AddDbContext<PlatformDbContext>(options => options.UseSqlServer(connectionString, sql => { sql.MigrationsAssembly(...); sql.MigrationsHistoryTable("__PlatformMigrationsHistory", "platform"); }))`. Separate migrations history table from the tenant context (which uses EF's default `__EFMigrationsHistory` in `dbo`).
- Connection string: **same `connectionString`** as `GymFlowProDbContext` — `GMS.Api\Program.cs:216`: `builder.Services.AddGymFlowPlatform(connectionString, configuration)`, using the identical `connectionString` variable defined at `Program.cs:27-28`. **This confirms `GymFlowProDbContext` and `PlatformDbContext` are two DbContexts over one shared database**, distinguished by schema (`dbo` vs `platform`) and by a third schema `HangFire` used by Hangfire job storage (`InfrastructureServiceExtensions.cs:114-122`, `SchemaName = "HangFire"`, same connection string again).
- Design-time factory: `D:\GMS\GMS\GMS.Platform\Persistence\PlatformDbContextFactory.cs` — used by `dotnet ef` tooling; falls back to `Server=(localdb)\mssqllocaldb;Database=GymFlowProDb;...` if no config is found (line 24), i.e. the design-time default already assumes LocalDB.
- Interceptors / `SaveChanges` overrides: none — `PlatformDbContext` has no override of `SaveChanges`/`SaveChangesAsync`. Timestamp fields (`CreatedAtUtc`/`UpdatedAtUtc`) on platform entities are set manually in service code (e.g. property initializers with `= DateTime.UtcNow`, see `PlatformSubscription.cs:31-32`) or in seeders, not centrally.
- **Cross-schema relationship note (important, non-obvious):** `Program.cs:259` comment says *"tenant migrations first — platform.subscriptions FK → dbo.tenants"*, and `PlatformSubscription.TenantId` (`GMS.Platform\Entities\PlatformSubscription.cs:10`) logically references `Tenant.Id` in the other context's model. However, **grepping the actual Platform migrations for `AddForeignKey` shows no DB-level foreign key constraint from `platform.subscriptions` (or any platform table) to `dbo.Tenants`** — the only `AddForeignKey` calls with `principalSchema: "platform"` reference other platform-schema tables (`20260726142452_AddPlatformSubscriptions.cs:73`, `20260728091413_AddPlatformBillingCore.cs:58`). So the tenant↔subscription relationship is a **soft/logical reference only** (a bare `Guid TenantId` column with no `HasForeignKey`/constraint), not an enforced FK — despite the two DbContexts sharing one database and schema-crossing FKs being technically possible in SQL Server.

### 1.3 Migration-application asymmetry at startup (Program.cs)

`GMS.Api\Program.cs:259-309`:
1. `app.ApplyProductionDatabaseMigrationsAsync()` (line 263) migrates **only** `GymFlowProDbContext`, and only if the config flag `DatabaseConfig:ApplyMigrationsOnStartup` is `true` (default `false` — see `ProductionHostingExtensions.cs:75-76`). If unset, tenant-schema migrations are **not** applied automatically on boot.
2. `PlatformDataSeeder.SeedAsync()` (line 270) **unconditionally** calls `_db.Database.MigrateWithSqlImportBaselineAsync(...)` for `PlatformDbContext` (`GMS.Platform\Persistence\PlatformDataSeeder.cs:35-39`) — no feature flag gate. So platform-schema migrations always auto-apply on startup, tenant-schema migrations usually don't (unless the flag is set).
3. `MigrateWithSqlImportBaselineAsync` (`GMS.Platform\Persistence\MigrationImportBaselineExtensions.cs`) wraps `database.MigrateAsync()` in a retry loop (up to 64 attempts) that specifically tolerates SQL errors 2714 ("object already exists") and 1801 ("database already exists") by marking the conflicting migration as applied in the history table — a baseline-reconciliation mechanism for databases that were seeded via manual SQL import outside of EF. This logic is plain T-SQL/ADO error-code handling; nothing Azure-specific.

---

## 2. Entities & Relationships

Entity files live in `GMS.Core\Entities\*.cs` (51 files) for the tenant model, and `GMS.Platform\Entities\*.cs` (8 files) for the control plane. All tenant-scoped entities derive from `BaseEntity` (`GMS.Core\Entities\BaseEntity.cs`): `Guid Id`, `DateTime CreatedAtUtc`, `DateTime? UpdatedAtUtc`, `bool IsDeleted`, with `Id`/`CreatedAtUtc`/`IsDeleted` defaulted in the constructor.

### 2.1 Members, Memberships, Plans
- `GymMember` — `GMS.Core\Entities\GymMember.cs`. Key props: `TenantId`, `MemberNumber` (unique per tenant), `FullName`/`FullNameAr`, `PhoneNumber`, `Email`, `NationalIdEncrypted`, `IsTrial`/`TrialOutcome`/`ConvertingSaleId`, `ReferralCode`. FKs: `AppUserId → AppUser`, `ConvertingSaleId → Sale`. Nav collections: `Memberships`, `Attendances`, `InvitationsSent/Converted`.
- `Membership` — `GMS.Core\Entities\Membership.cs`. FKs: `MemberId → GymMember`, `PlanId → MembershipPlan`. Fields: `Status` ('active'/'expired'/'frozen'/'cancelled'), `SessionsRemaining`, freeze window, `AmountPaid`, `AutoRenew`, `PlanTransitionMode`.
- `MembershipPlan` — `GMS.Core\Entities\MembershipPlan.cs`. `PlanType` enum-as-string (`monthly_unlimited|session_pack|time_limited|pt_credits|family|trial|day_pass`), pricing, time restrictions, referral-reward config. Nav: `Memberships`, `Entitlements → PlanEntitlement`.

### 2.2 Attendance — TWO SEPARATE ENTITIES (confirmed)
- **`GymAttendance`** (member/gym-floor check-in) — `GMS.Core\Entities\GymAttendance.cs`. FKs: `MemberId → GymMember` (nullable, guest walk-ins use `GuestName`/`GuestPhone` instead), `MembershipId → Membership`, `BookingId → ActivityBooking`, `SessionId → ActivitySession`, `StaffUserId → AppUser`. Has `AttendanceDateCairo` (server-stamped, see §1.1) backing a unique per-member-per-day constraint scoped to `SessionId == null` rows.
- **`EmployeeAttendance`** (HR/staff attendance) — `GMS.Core\Entities\HrAttendanceEntities.cs:52-89`, in the *same file* as `EmployeeShift` (shift **template**, not the POS cash-drawer `Shift`) and `EmployeeScheduleAssignment`. FKs: `EmployeeId → Employee`, `ScheduleId → EmployeeScheduleAssignment` (nullable), `LeaveRequestId → LeaveRequest` (nullable, for auto-generated OnLeave placeholder rows). Unique per `(TenantId, EmployeeId, AttendanceDate)`.
- These two entities share no base type beyond `BaseEntity`, no FK relationship to each other, and are explicitly documented as unrelated in code comments (`HrAttendanceEntities.cs:46-51`: *"Separate from member GymAttendance... that tracks members entering the gym, this tracks staff working hours"*). The doc comment on `EmployeeShift` (line 6-8) also warns it is unrelated to the POS `Shift` (cash-drawer) entity — a third, distinct "shift" concept in the codebase.

### 2.3 Sales, Payments
- `Sale` — `GMS.Core\Entities\Sale.cs`. FKs: `MemberId → GymMember` (nullable, guest sales use `GuestName`/`GuestPhone`), `SoldByUserId → AppUser`, `ShiftId → Shift`, `PromoCodeId → PromoCode`. `Status`: completed/partially_paid/refunded/partially_refunded. `AmountDue`/`DueDate` support partial payment. Nav: `Lines → SaleLine`.
- `SaleLine` — `GMS.Core\Entities\SaleLine.cs`. FK: `SaleId → Sale`. `ReferenceId` is a **polymorphic pointer** (plan id or product id depending on `LineType`) with **no FK constraint** (documented, line 14) — this is a schema looseness worth flagging for any offline-schema redesign.
- `SaleAdjustment`, `SaleIdempotencyKey`, `Invoice`, `InvoiceSequence` — separate files, all tenant-scoped.
- `PaymentTransaction` — `GMS.Core\Entities\PaymentTransaction.cs`. FKs: `MemberId → GymMember`, `MembershipId → Membership` (nullable — retail-only sales), `SaleId` ("FK to sales.Id — constraint deferred to the P5 migration", line 44, i.e. **documented as intentionally unconstrained**), `ReceivedByUserId → AppUser`, `ShiftId → Shift`. Carries gateway/settlement lifecycle fields (`Gateway`, `ExternalRef`, `Status`, `SettlementStatus`, `HmacVerified`, `RawPayload`).

### 2.4 Inventory
- `Product` (`GMS.Core\Entities\Product.cs`), `ProductCategory` — catalog; on-hand qty deliberately lives elsewhere (`StockBalance`), not on `Product` (documented line 5).
- `Supplier`, `PurchaseOrder`, `PurchaseOrderLine`, `GoodsReceipt`, `GoodsReceiptLine`, `ProductBatch`, `SupplierLedgerEntry` — all in `GMS.Core\Entities\PurchasingEntities.cs`. Chain: `Supplier → PurchaseOrder → PurchaseOrderLine`; `PurchaseOrder → GoodsReceipt → GoodsReceiptLine → ProductBatch`.
- `Warehouse` (`GMS.Core\Entities\Warehouse.cs`), `StockMovement`/`StockBalance` (ledger — `GMS.Core\Entities\StockMovement.cs`, `StockBalance.cs`), `StockAdjustment`/`StockAdjustmentLine`, `StockTransfer`/`StockTransferLine` (`GMS.Core\Entities\StockTransferEntities.cs`), `StockCount`/`StockCountLine` (`GMS.Core\Entities\StockCountEntities.cs`).
- `StockBalance` carries the only inventory-side **concurrency token** (`RowVersion`, `StockBalance.cs:16`) — see §3/§5.

### 2.5 Expenses
- `CashExpense` — `GMS.Core\Entities\CashExpense.cs`. FKs: `ShiftId → Shift` (nullable), `RecordedByUserId → AppUser`. Feeds cash-basis profit dashboards; only `Status == "posted"` and non-deleted rows count (doc comment lines 5-6).

### 2.6 Employees, Shifts, HR
- `Department`, `Position`, `Employee`, `EmployeeContract` — `GMS.Core\Entities\HrEntities.cs`. `Employee.AppUserId` links to a **Staff desk** `AppUser` row; a *separate* `EmployeeAppUserId` links to the **Employee App** identity — both nullable, independently managed (doc comment lines 65-76). `Employee` is explicitly NOT required to have any login account.
- `EmployeeShift`, `EmployeeScheduleAssignment`, `EmployeeAttendance` — `GMS.Core\Entities\HrAttendanceEntities.cs` (see §2.2).
- `LeaveRequest`, `LeaveBalance` — `GMS.Core\Entities\HrLeaveEntities.cs` (not read in full; present per DbSet list).
- `PayrollPeriod`, `PayrollLine`, `PayrollAdjustment`, `PayrollPayment` — `GMS.Core\Entities\HrPayrollEntities.cs`.
- `EmployeeDocument` — `GMS.Core\Entities\HrDocumentEntities.cs`.
- **POS `Shift`** (cash-drawer, `GMS.Core\Entities\Shift.cs`) is a fourth, unrelated "shift" concept — opens with `OpeningFloat`, accumulates `CashMovement`s, closes with a blind count (`CountedCash` vs `ExpectedCash`, `Variance`). FKs: `UserId → AppUser`, `ApprovedByUserId → AppUser`.

### 2.7 Users, Roles, Permissions
- **Hybrid identity model, not a single clean scheme:**
  - `ApplicationUser : IdentityUser<Guid>` (`GMS.Core\Entities\Identity\ApplicationUser.cs`) — real ASP.NET Core Identity user (`AspNetUsers`), used for authentication (`GymFlowProDbContext` is an `IdentityDbContext`). Has `TenantId`, `PermissionsOverride` (JSON, "reserved for future use — currently read but not applied", line 22-26).
  - `IdentityRole<Guid>` — standard Identity roles (`AspNetRoles`): seeded set is `Owner, Manager, Trainer, Receptionist, Member, Employee` (`GMS.Infrastructure\Persistence\DataSeeder.cs:77`).
  - `AppUser` (`GMS.Core\Entities\AppUser.cs`) — a **separate, tenant-scoped domain "staff profile" entity**, linked to `ApplicationUser` only loosely via a string `UserId` property (line 13, comment "Azure AD / Identity ID" — stale comment, no Azure AD integration found elsewhere) holding `ApplicationUser.Id.ToString()`. `AppUser.Role` is an independent free-text string (`'admin'|'manager'|'trainer'|'staff'|'member'`, line 23) — **not the same value space or storage as the Identity role**, and not FK-enforced against `AspNetRoles`. Manual check-in and several services join `AppUser.UserId == ApplicationUser.Id.ToString()` — a string comparison against a GUID column, not a typed FK.
  - `IPermissionProvider` (`GMS.Core\Interfaces\IPermissionProvider.cs`) is the actual authorization source — a role→permissions mapping, layered with the unused-so-far `PermissionsOverride` JSON blob.
  - `RefreshToken` (`GMS.Core\Entities\Identity\RefreshToken.cs`) — tenant-scoped, FK to `ApplicationUser` via `ApplicationUser.RefreshTokens` nav.

### 2.8 Tenants, Subscriptions/Licensing
- `Tenant` — `GMS.Core\Entities\Tenant.cs` (tenant model, `dbo.Tenants`). Has `MaxMembers`, `SubscriptionStartDate/EndDate`, and a JSON `Settings` blob — these look like a **legacy/parallel** subscription mechanism that pre-dates or duplicates the Platform billing model below (both a `Tenant.SubscriptionEndDate` and a full `PlatformSubscription` row can exist; whether the app still reads `Tenant.Subscription*` is UNKNOWN — REQUIRES VERIFICATION, not confirmed from entities alone).
- `PlatformSubscription` — `GMS.Platform\Entities\PlatformSubscription.cs`. "Source of truth for what a tenant pays for" (doc comment line 4-6); `PlanTier` (starter/growth/pro/enterprise), `Status` (trialing/active/past_due/suspended/cancelled), `BillingCycle`, `PriceEgp`, trial/cancel/suspend timestamps. `TenantId` is a bare `Guid`, cross-schema, **not DB-FK-enforced** (see §1.2).
- `SubscriptionChange` — append-only audit trail of subscription mutations, FK `SubscriptionId → PlatformSubscription` (this FK **is** enforced at the DB level within the `platform` schema).
- `CommercialPlan`, `PlanChangeLog` — `GMS.Platform\Entities\CommercialPlanEntities.cs` — plan catalog (no PK id; `CommercialPlan` is keyed by `Tier` string per its Configuration, not independently verified beyond entity shape).
- Other platform entities present but not read in full: `PlatformAdminUser`, `PlatformAuditLog`, `PlatformInvoice`, `PlatformInvoiceSequence`, `PlatformPaymentEvent`, `UsageCounter`, `FeatureOverride`, `TierFeatureMap`, `AutomationEnrollment`, `PriceOverride`, `TenantHealthScore`, `RiskQueueOutcome` (all confirmed present via `GMS.Platform\Persistence\PlatformDbContext.cs:16-31` DbSets and matching `IEntityTypeConfiguration<T>` classes — see §3).

---

## 3. EF Core Configurations

`GMS.Infrastructure\Persistence\Configurations\*.cs` — **51 files** containing one `IEntityTypeConfiguration<T>` class per file (some files hold several related classes, e.g. `PurchasingConfigurations.cs` holds 7, `HrPayrollConfigurations.cs` holds 4). `GMS.Platform\Persistence\Configurations\*.cs` — **7 files**, 16 configuration classes, one per Platform entity — **full coverage**, no Platform entity relies on convention alone.

### 3.1 Convention-only entities (no explicit configuration) in `GMS.Infrastructure`
Comparing all `GymFlowProDbContext` `DbSet<T>` declarations against every `IEntityTypeConfiguration<T>` found via `grep -r "IEntityTypeConfiguration<"`, the only entity with **no explicit configuration class** is:
- **`AnalyticsSnapshot`** (`GMS.Core\Entities\AnalyticsSnapshot.cs`) — has a global tenant query filter (`GymFlowProDbContext.cs:291-292`) but no `HasIndex`/constraint configuration at all; relies entirely on EF conventions. Contrast with its sibling `GymAnalyticsSnapshot`, which **does** have `GymAnalyticsSnapshotConfiguration.cs` including a `HasIndex(s => new { s.TenantId, s.SnapshotDate }).IsUnique()` (line 24) — `AnalyticsSnapshot` has no equivalent uniqueness guard.

### 3.2 Cascade delete behavior
Grep of `OnDelete(DeleteBehavior.*)` across all configurations: **166 `Restrict`, 25 `SetNull`, 17 `Cascade`.** The large majority of relationships are `Restrict` (delete blocked if children exist) — a deliberately conservative default for a financial/gym-ops system. `Cascade` is used in 14 files where a clear parent/child lifecycle exists, e.g. `SaleLineConfiguration.cs` (line items die with their sale), `MembershipConfiguration.cs`, `GymAttendanceConfiguration.cs`, `CashMovementConfiguration.cs`, `MemberOrderConfiguration.cs`, `StockAdjustmentConfiguration.cs`, `StockCountConfigurations.cs`, `StockTransferConfigurations.cs`, `PurchasingConfigurations.cs`, `HrConfigurations.cs`, `HrPayrollConfigurations.cs`, `ImportRowConfiguration.cs`, `GymMemberConfiguration.cs`, `Identity\RefreshTokenConfiguration.cs`.

### 3.3 Concurrency tokens (RowVersion)
Exactly **4 entities** carry an explicit optimistic-concurrency `RowVersion` (`byte[]`, `.IsRowVersion()`):
- `StockBalance.cs:16` (configured `StockBalanceConfiguration.cs:23` — `builder.Property(b => b.RowVersion).IsRowVersion()`)
- `MemberOrder.cs:41`
- `MemberAppActivationCode.cs:24`
- `EmployeeAppActivationCode.cs:24`

No other entity (including `Sale`, `Membership`, `Shift`, `PaymentTransaction`) has a concurrency token — those rely on transaction isolation level instead (see §5).

### 3.4 Notable indexes / unique constraints (non-exhaustive, representative sample)
- Tenant-scoped natural keys made unique per tenant via composite `(TenantId, X)` indexes: `GymMember(TenantId, MemberNumber)`, `GymMember(TenantId, PhoneNumber)` (filtered), `GymMember(TenantId, ReferralCode)` (filtered) — `GymMemberConfiguration.cs:34-35,54-55,113-114`; `Product(TenantId, Sku)`, `Product(TenantId, Barcode)` — `ProductConfiguration.cs:55-60`; `AppUser(TenantId, UserId)`, `AppUser(TenantId, StaffNumber)` — `AppUserConfiguration.cs:34-35,80-81`; `Employee(TenantId, EmployeeNumber)` and unique `Employee.AppUserId` / `Employee.EmployeeAppUserId` — `HrConfigurations.cs:85-94`; `Warehouse(TenantId, Code)` plus a filtered unique on `TenantId` alone to enforce "one default warehouse per tenant" — `WarehouseConfiguration.cs:30-36`.
- Idempotency-style unique indexes: `Sale(TenantId, IdempotencyKey)` — `SaleConfiguration.cs:51-52`; `CashExpense(TenantId, IdempotencyKey)` — `CashExpenseConfiguration.cs:52-53`; `SaleIdempotencyKey(TenantId, Key)` — `SaleIdempotencyKeyConfiguration.cs:26`; `StockMovement(TenantId, ReferenceType, ReferenceId, Reason[, BatchId])` — two unique composite indexes, `StockMovementConfiguration.cs:39-45`; `MemberOrder(TenantId, SaleId)` unique — `MemberOrderConfiguration.cs:67-68`; `ActivityBooking.SaleId` unique — `ActivityConfigurations.cs:86-87`.
- Business-invariant uniqueness: `Shift(TenantId, UserId)` unique — enforces one open cash-drawer shift per staff member per tenant (`ShiftConfiguration.cs:39-41`); `StockBalance` has two filtered unique indexes (with vs without `BatchId`, `StockBalanceConfiguration.cs:29-36`, working around SQL Server's multi-NULL uniqueness quirk); `GymAttendance(TenantId, MemberId, AttendanceDateCairo)` unique, scoped to floor check-ins (`GymAttendanceConfiguration.cs:59-61`).
- `Tenant.GymCode` and `Tenant.Email` are globally unique (not tenant-scoped, by definition) — `TenantConfiguration.cs:36-37,96-97`.
- Check constraints exist at least on the Platform side: `PlatformSubscription` has 3 `HasCheckConstraint` calls enforcing `PlanTier`/`Status`/`BillingCycle` enum values at the DB level (`SubscriptionConfigurations.cs:13-21`) — this pattern was **not** observed on the tenant side (tenant-side enums like `Sale.Status`, `Membership.Status` are plain `string` columns with no DB check constraint found).

---

## 4. Migrations

### 4.1 Counts
- **`GMS.Infrastructure\Persistence\Migrations`: 67 migrations** (plus `GymFlowProDbContextModelSnapshot.cs`), spanning `20260506000801_AddAuthEntities` through `20260907114724_AddQrAttendanceHardening`.
- **`GMS.Platform\Persistence\Migrations`: 10 migrations** (plus `PlatformDbContextModelSnapshot.cs`), spanning `20260726141603_InitialPlatform` through `20260828002621_AddCommercialPlans`.

### 4.2 10 most recent — `GMS.Infrastructure`
1. `20260831190000_AddSaleLineCostSnapshots`
2. `20260831191024_FinancialRemediationFoundation`
3. `20260831191505_CashExpenseStructuredFields`
4. `20260831192646_PayrollPaymentDisbursements`
5. `20260831194142_SupplierLedgerEffectiveDate`
6. `20260831195201_SaleAdjustments`
7. `20260901140704_CashExpenseSourceTypeDriftFix`
8. `20260905220000_AddMemberOrderSaleId`
9. `20260906133517_AddPtSessionDurationMinutes`
10. `20260907114724_AddQrAttendanceHardening`

### 4.3 All 10 migrations — `GMS.Platform` (fewer than 10 exist, so this is the complete list)
1. `20260726141603_InitialPlatform`
2. `20260726142452_AddPlatformSubscriptions`
3. `20260728091413_AddPlatformBillingCore`
4. `20260728093005_AddPlatformBillingPayments`
5. `20260730105926_AddUsageEnforcementCp4`
6. `20260730131813_AddAutomationDunningCp5`
7. `20260730133337_AddPlatformConsoleCp6`
8. `20260730135337_AddTenantHealthScoresCp7`
9. `20260827222838_AllowSubscriptionCancelUndoChangeType`
10. `20260828002621_AddCommercialPlans`

### 4.4 Seed-data mechanism
No `HasData(...)` calls anywhere in the codebase (only hits were compiled `.dll` binaries and a false-positive match on the identifiers `GymAttendanceConfiguration`/`StockBalanceConfiguration`/`StockMovementConfiguration`/`WarehouseConfiguration`, which don't actually contain `HasData`). Seeding is done entirely in **imperative C# seeder classes**, run at application startup, not via EF's declarative model-seeding:
- **`DataSeeder`** (`GMS.Infrastructure\Persistence\DataSeeder.cs`) — tenant-side. `SeedAsync()` (lines 33-70) is idempotent (returns early if any `Tenant` row exists, line 39). Seeds: a demo `Tenant` ("Iron Zone Gym", `GymCode = GYM-TEST-01`), 6 Identity roles (`EnsureIdentityRolesAsync`, runs unconditionally on every boot, line 77), 4 fixed-GUID staff users (Owner/Manager/Trainer/Receptionist, hardcoded credentials `Test@1234`, lines 118-190) each paired with a matching domain `AppUser` row, 3 `MembershipPlan`s, and one sample `GymMember` with an active `Membership`. A separate `EnsureDemoOffersAsync()` (lines 307-457) seeds demo `PromoCode`/`Offer` rows, gated to the `GYM-TEST-01` tenant only. **All of this only runs when `app.Environment.IsDevelopment()`** (`Program.cs:281`), except `EnsureIdentityRolesAsync()`, which always runs.
- **`PlatformDataSeeder`** (`GMS.Platform\Persistence\PlatformDataSeeder.cs`) — runs unconditionally (not gated to Development). Migrates the platform schema (§1.3), then seeds `TierFeatureMap` rows (`TierFeatureMapSeed.BuildAll()`) and `CommercialPlan` rows (`CommercialPlanSeed.BuildAll()`) idempotently (diff-and-insert-missing pattern, lines 99-139), then seeds exactly one `PlatformAdminUser` **only if** `PlatformSeed:Email`/`PlatformSeed:Password` configuration values are set (lines 44-53) — this is the production bootstrap path for the first platform-admin account, MFA intentionally left disabled for first-login setup.

### 4.5 Model snapshot sync
- `GMS.Infrastructure\Persistence\Migrations\GymFlowProDbContextModelSnapshot.cs` — 293,089 bytes, last modified **Sep 7** (same calendar day as the most recent migration, `20260907114724_AddQrAttendanceHardening`) — consistent with the migration history, no stale-snapshot smell.
- `GMS.Platform\Persistence\Migrations\PlatformDbContextModelSnapshot.cs` — 32,724 bytes, last modified **Aug 28** — matches the most recent Platform migration date (`20260828002621_AddCommercialPlans`). Both snapshots appear in sync with their migration histories; no deep diff performed per task scope.

---

## 5. Cross-cutting data concerns

### 5.1 Soft delete
Confirmed uniform pattern: every `BaseEntity`-derived entity has `bool IsDeleted` (`BaseEntity.cs:11`), and `GymFlowProDbContext.SaveChangesAsync` (lines 454-457) intercepts every `EntityState.Deleted` transition and rewrites it to `IsDeleted = true` + `EntityState.Modified` — **there is no hard-delete path for any `BaseEntity` type through this context**. Combined with the global query filters (§1.1), soft-deleted rows disappear from all normal queries automatically. `IgnoreQueryFilters()` is used deliberately in ~21 service files (grep count) plus the seeder, for admin/cross-tenant/lookup scenarios (e.g. `DataSeeder.cs:310,316,323` looks up a tenant/promo/offer by natural key while ignoring both the tenant and soft-delete filters, then re-checks `!IsDeleted` manually at line 317).

### 5.2 Audit fields
`CreatedAtUtc`/`UpdatedAtUtc` are consistent across all `BaseEntity` types (base class + `SaveChangesAsync` auto-stamp, §1.1) — this part is centralized, not ad hoc. However, "who did it" is **not** centralized: individual entities carry their own bespoke actor columns where needed (`RecordedByUserId` on `CashExpense`, `ApprovedByUserId`/`SubmittedByUserId`/`ReceivedByUserId` on `PurchaseOrder`/`StockCount`/`GoodsReceipt`/`StockTransfer`, `CreatedByAppUserId` on `EmployeeAttendance`, `SoldByUserId` on `Sale`) — there is no single `CreatedBy`/`ModifiedBy` convention or interceptor; each entity/service adds the actor field it needs, ad hoc.
Platform-side entities (`PlatformSubscription`, `SubscriptionChange`, etc.) do **not** inherit `BaseEntity` and set `CreatedAtUtc`/`UpdatedAtUtc` manually via property initializers (e.g. `PlatformSubscription.cs:31-32`) — no centralized stamping exists there since `PlatformDbContext` has no `SaveChanges` override.

### 5.3 Tenant isolation at the data layer
Two coexisting patterns, both confirmed with concrete evidence:
- **Global query filter (safer, majority pattern):** `GymFlowProDbContext.cs:202-203` — `modelBuilder.Entity<GymMember>().HasQueryFilter(m => m.TenantId == _tenantContext.TenantId && !m.IsDeleted);` (and 61 more like it, §1.1). Any LINQ query against `_db.GymMembers` is automatically tenant-scoped without the caller doing anything.
- **Manual `.Where(x => x.TenantId == ...)` (more error-prone, used where the global filter deliberately does not apply):** `GMS.Application\Services\AdminService.cs:482-486` —
  ```csharp
  private async Task<ApplicationUser?> FindStaffUserAsync(Guid tenantId, Guid id)
  {
      return await _dbContext.Users
          .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId);
  }
  ```
  This is necessary because `ApplicationUser`/`AspNetUsers` is intentionally excluded from the global filter set (`GymFlowProDbContext.cs:407-408`), so every caller that queries Identity users for a specific tenant must remember to add the `TenantId` predicate by hand — the exact "scattered and error-prone" pattern the task description asked to identify. A second class of manual filtering exists in the ~21 files using `IgnoreQueryFilters()` (§5.1) — each such call site re-implements whatever tenant/soft-delete predicate it still needs.

### 5.4 Concurrency handling
- Optimistic concurrency tokens exist on exactly 4 entities (`StockBalance`, `MemberOrder`, `MemberAppActivationCode`, `EmployeeAppActivationCode` — §3.3).
- Concrete conflict-handling example: `GMS.Application\Services\StockLedgerService.cs:80-190` (`PostAsync`, "the only writer of movements and balances"). Wraps the balance update in up to **3 retry attempts** (line 81: `const int maxAttempts = 3`), each in its own transaction when it owns one (`_db.Database.BeginTransactionAsync`, line 88), catching `DbUpdateConcurrencyException`: retries silently while `attempt < maxAttempts` (line 156-163), returns a user-facing "Concurrent stock update — retry" failure on the final attempt (line 164-170), and additionally catches unique-index violations on the idempotency key to return the already-posted result instead of erroring (line 171-186) — a fairly mature manual retry/idempotency pattern, not something EF or SQL Server provides automatically.
- All other high-write entities (`Sale`, `Membership`, `Shift`, `PaymentTransaction`) have **no** concurrency token — conflicting concurrent writes there are prevented (if at all) purely by transaction isolation level and application-level uniqueness constraints (§5.5), not optimistic concurrency.

### 5.5 Explicit multi-entity transactions
Confirmed via `BeginTransactionAsync`/`ExecuteInTransactionAsync` usage across **18 files**, including `SaleService.cs`, `RefundService.cs`, `CashExpenseService.cs`, `StockLedgerService.cs`, `SaleAdjustmentService.cs`, `PurchaseOrderService.cs`, `PaymentService.cs`, `SessionBookingService.cs`, `AuthService.cs`, `InvoiceService.cs`, `TenantProvisioningService.cs`, `StockAdjustmentService.cs`, `StockTransferService.cs`, `StockCountService.cs`, `TrialService.cs`, plus Platform's `SubscriptionWriteRepository.cs` and `PlatformBillingServices.cs`. Two concrete, cited examples:
- `GMS.Application\Services\SaleService.cs:329-332` — POS sale creation: `await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted)`, guarded by `_dbContext.Database.IsRelational()`. This is the atomic unit that touches `Sale` + `SaleLine`s + (via `StockLedgerService`) inventory movements + payment recording described in the task — confirmed as one explicit transaction, not several independent `SaveChanges` calls.
- `GMS.Application\Services\SaleService.cs:736-737` — collecting a payment against an already-partially-paid sale uses a **stricter** `IsolationLevel.Serializable` transaction, re-reading the `Sale` row inside the transaction (`_dbContext.Sales.FirstOrDefaultAsync(...)`, line 739) before checking `Status == "partially_paid"` and `AmountDue > 0` — a correct pattern to prevent double-collection races.

### 5.6 What's not covered
Not all money-moving flows were traced end-to-end in this pass (task scope is data-layer audit, not full service-layer review) — e.g. whether `RefundService`/`PaymentService` transactions also wrap the corresponding `StockLedgerService`/`CashMovement` writes in every code path is UNKNOWN — REQUIRES VERIFICATION beyond the two cited examples.

---

## 6. Cloud dependency check specific to data layer

### 6.1 No Azure-specific or cloud-only SQL Server features found
- Grep for `EnableRetryOnFailure`, `AlwaysEncrypted`, `Elastic`, `Managed Identity`, `KeyVault`, `SqlServerRetryingExecutionStrategy`, `Azure` across `GMS.Infrastructure` returned **no functional hits** — only a stale doc comment ("Azure SQL compatible (DATETIME2, NVARCHAR for Unicode)", `GymFlowProDbContext.cs:22`) and an unrelated "Store PDF in Azure Blob Storage" TODO comment in `GMS.Infrastructure\Jobs\TrainerCommissionReportJob.cs:34` (a **future/unimplemented** feature, not active code).
- **No `EnableRetryOnFailure`** (Azure SQL's recommended connection-resiliency pattern) is configured on either `GymFlowProDbContext` (`InfrastructureServiceExtensions.cs:40-44`) or `PlatformDbContext` (`PlatformServiceExtensions.cs:25-32`) — both use bare `UseSqlServer(connectionString, sqlOptions => sqlOptions.MigrationsAssembly(...))` with no execution-strategy customization. This means the app has no built-in transient-fault retry either way — a neutral fact for local deployment (LocalDB/Express also don't need it) but worth noting it isn't something to strip out for a local edition; it was never there.
- Hangfire job storage also targets plain SQL Server (`UseSqlServerStorage(connectionString, ...)`, `InfrastructureServiceExtensions.cs:114-122`) with a custom `HangFire` schema, same connection string, no cloud-specific options (`UseRecommendedIsolationLevel = true`, `DisableGlobalLocks = true` are generic Hangfire tuning, not Azure-specific).
- **Actual production target is not Azure.** `GMS.Api\Extensions\ProductionConfigurationValidator.cs:3,15,18` explicitly names **MonsterASP** (a Windows/IIS shared-hosting + "Cloud SQL" provider) as the production host, checking for placeholder strings `"YOUR_MONSTERASP"` and `"your-azure-server"` — the latter suggesting an *earlier* Azure-targeted iteration that was since migrated away from, leaving a stray doc comment (§ above) as the only remaining trace. This further reduces cloud-lock-in risk for a local edition.

### 6.2 Would this work unmodified against local SQL Server Express / LocalDB?
**Yes, with high confidence, based on what was found:**
- The connection string format used everywhere (`appsettings.Development.json:12`: `Server=(localdb)\mssqllocaldb;Database=GymFlowProDb;Trusted_Connection=true;Encrypt=false;`) is already a LocalDB connection string, actively used for local development today — this is not a hypothetical, it's the checked-in dev configuration.
- `GMS.Platform\Persistence\PlatformDbContextFactory.cs:24` independently hardcodes the same LocalDB fallback for design-time `dotnet ef` tooling, reinforcing that LocalDB is the project's own default dev target, not something bolted on for this audit.
- No feature used (schemas, filtered/composite indexes, check constraints, `NEWSEQUENTIALID()`-style GUID PKs per the doc comment, `RowVersion` concurrency tokens, Hangfire SQL storage) is Azure-SQL-only; all are standard on-prem SQL Server features supported by Express/LocalDB (LocalDB has some feature gaps around service-broker/CLR/full-text, none of which are used here based on this review).
- The one real risk to local packaging is **operational, not schematic**: three schemas (`dbo`, `platform`, `HangFire`) in **one database**, two independently-migrated `DbContext`s with **different auto-migrate-on-startup behavior** (§1.3 — tenant migrations gated behind a config flag defaulting to off, platform migrations always run) — a "Local Lifetime Edition" installer would need to explicitly force `DatabaseConfig:ApplyMigrationsOnStartup = true` (or run both `dotnet ef database update` commands) to guarantee both schemas are current on first run, since today only the platform schema self-heals automatically.

### 6.3 `start-local.bat` — live and wired to the same config, not stale
`D:\GMS\GMS\start-local.bat` (repo root):
- Checks `sqllocaldb info mssqllocaldb` / starts LocalDB (lines 17-29).
- Checks for a `GymFlowProDb` database via `sqlcmd -S "(localdb)\mssqllocaldb"` (line 34) — **matches** the `Database=GymFlowProDb` name in `appsettings.Development.json:12` exactly.
- If missing, runs `dotnet ef database update --startup-project ..\GMS.Api` from the `GMS.Infrastructure` folder (lines 37-39) — this targets `GymFlowProDbContext` **only** (EF's `database update` defaults to the context found via that project's design-time services, i.e. the tenant context; there is no equivalent step here for `PlatformDbContext`). Given §1.3's finding that `PlatformDataSeeder` migrates the `platform` schema automatically on every app start regardless, this omission is **not fatal** — the platform schema self-creates on first `dotnet run` — but it does mean `start-local.bat`'s own migration step, taken in isolation, only guarantees the `dbo` schema is current.
- Finally runs `dotnet run --configuration Debug` from `GMS.Api` (line 76), which is the same executable/`Program.cs` used in every other environment — **this script is live and correctly wired to the real `DefaultConnection`/`GymFlowProDbContext`, not stale or disconnected from production code paths**, contrary to the possibility raised in the task brief.
