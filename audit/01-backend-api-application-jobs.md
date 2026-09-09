# Backend API, Application Layer & Background Jobs Audit

Scope: `GMS.Api`, `GMS.Application`, `GMS.Core`, `GMS.Infrastructure`, `GMS.Platform` (net8.0). Read-only audit — no code was changed. All claims cite `file:line` where feasible; anything not directly verifiable in code is marked **UNKNOWN — REQUIRES VERIFICATION**.

---

## 0. GMS.Platform — what it is and how it relates to GMS.Api / GMS.Infrastructure

`GMS.Platform` is a **second, independent EF Core `DbContext`** (`GMS.Platform.Persistence.PlatformDbContext`) representing the SaaS **control plane** (cross-tenant admin/billing/licensing data), separate from the per-gym operational data in `GMS.Infrastructure.Persistence.GymFlowProDbContext`.

Evidence:
- Registered separately in `GMS.Platform/PlatformServiceExtensions.cs:25-32` via `services.AddDbContext<PlatformDbContext>(...)`, using the **same SQL Server connection string** as the tenant DB but its own migrations assembly and a dedicated migrations-history table: `MigrationsHistoryTable(MigrationsHistoryTable, Schema)` where `MigrationsHistoryTable = "__PlatformMigrationsHistory"` and `Schema = "platform"` (`PlatformServiceExtensions.cs:15-16`).
- Its own `Persistence/Migrations` folder (18 migration files, `GMS.Platform/Persistence/Migrations/*`), independent of `GMS.Infrastructure/Persistence/Migrations` (60+ files).
- Entities are platform/billing-oriented, not gym-operational: `PlatformAdminUser`, `PlatformSubscription`, `PlatformInvoice`, `PlatformAuditLog`, `AutomationEnrollment`, commercial-plan/pricing/usage-enforcement entities (`GMS.Platform/Entities/*.cs`).
- `Program.cs:216` wires it in explicitly: `builder.Services.AddGymFlowPlatform(connectionString, configuration);` with a comment: *"Control plane (platform schema, separate migrations history, no tenant filters)"*.
- Authentication is segregated: a second JWT scheme `PlatformAuthConstants.AuthenticationScheme` (`Program.cs:200-213`) with its own audience, and platform-only authorization policies (`PlatformSupportOrAbove`, `PlatformOpsOrAbove`, `PlatformAdminOnly` — `Program.cs:229-243`) that hard-lock `AddAuthenticationSchemes(PlatformAuthConstants.AuthenticationScheme)` so a tenant Bearer token can never satisfy them.
- Platform controllers live in a separate folder/namespace, `GMS.Api/Platform/Controllers/*.cs`, all routed under `platform-api/*` (see §1), fully distinct from tenant `api/*` controllers.
- Startup seeds tenant DB migrations first, then Platform schema (`Program.cs:260-271`), because `platform.subscriptions` has an FK to `dbo.tenants` (comment at `Program.cs:259`).

**Conclusion**: `GMS.Platform` is the multi-tenant SaaS billing/licensing/ops control plane (subscriptions, commercial plans, usage metering/enforcement, tenant health scoring, platform admin users/audit, dunning automation, platform-side payment webhooks). It is not a second copy of gym data — it's the layer above all tenants. For a single-gym offline "Local Lifetime Edition," this entire project (DbContext, migrations, controllers, jobs) is presumptively the SaaS-only layer that would be removed or stubbed, since a single perpetually-licensed local install has no multi-tenant subscription/billing/usage-metering need — **UNKNOWN — REQUIRES VERIFICATION** whether any Platform-layer service (e.g. `IFeatureAccessService`, `ISubscriptionAccessService`) is called from tenant-side hot paths that would need a replacement stub (see §3, `TenantMiddleware` and `TrialExpirySetterJob` do call into it).

---

## 1. API Layer (GMS.Api)

### 1.1 Controllers (tenant-facing, `GMS.Api/Controllers/*.cs`)

All inherit `BaseApiController : ControllerBase` (`GMS.Api/Controllers/BaseApiController.cs:11`), which itself carries `[ApiController]` + `[Route("api/[controller]")]` — but every concrete controller overrides the route with an explicit `[Route(...)]`. 64 controllers total, listed with route prefix:

| Controller | Route |
|---|---|
| ActivitiesController | `api/activities` |
| ActivitySchedulesController | `api/activity-schedules` |
| ActivitySessionsController | `api/activity-sessions` |
| ActivityBookingsController | `api/activity-bookings` |
| AdminController | `api/admin` |
| AnalyticsController | `api/analytics` |
| AttendanceController | `api/attendance` |
| AuditController | `api/audit` |
| AuthController | `api/auth` |
| CallSheetController | `api/call-sheet` |
| CashExpensesController | `api/expenses` |
| DashboardController | `api/dashboard` |
| DebtorsController | `api/debtors` |
| HealthController | `api/[controller]` → `api/Health` |
| HrDashboardController | `api/hr/dashboard` |
| HrDepartmentsController | `api/hr/departments` |
| HrEmployeeAttendanceController | `api/hr/employee-attendance` |
| HrEmployeeDocumentsController | `api/hr` |
| HrEmployeeSchedulesController | `api/hr/employee-schedules` |
| HrEmployeeShiftsController | `api/hr/employee-shifts` |
| HrEmployeesController | `api/hr/employees` |
| HrLeaveBalancesController | `api/hr/leave-balances` |
| HrLeaveRequestsController | `api/hr/leave-requests` |
| HrPayrollAdjustmentsController | `api/hr/payroll-periods/{periodId:guid}/adjustments` |
| HrPayrollPeriodsController | `api/hr/payroll-periods` |
| HrPositionsController | `api/hr/positions` |
| ImportsController | `api/imports` |
| InventoryAdjustmentsController | `api/inventory/adjustments` |
| InventoryCatalogController | `api/inventory` |
| InventoryCountsController | `api/inventory/counts` |
| InventorySuppliersController | `api/inventory/suppliers` |
| InventoryPurchaseOrdersController | `api/inventory/purchase-orders` |
| InventoryGoodsReceiptsController | `api/inventory/goods-receipts` |
| InventoryReportsController | `api/inventory/reports` |
| InventoryStockController | `api/inventory` |
| InventoryTransfersController | `api/inventory/transfers` |
| InventoryWarehousesController | `api/inventory/warehouses` |
| InvitationController | `api/invitation` |
| InvoicesController | `api/invoices` |
| MemberAttendanceController | `api/member/attendance` |
| MemberBookingController | `api/member/activity-bookings` |
| MemberClassesController | `api/member/classes` |
| MemberMyOrdersController | `api/member/orders` |
| MemberOccupancyController | `api/member/occupancy` |
| MemberOffersController | `api/member/offers` |
| MemberOrdersController | `api/member-orders` |
| MemberStoreController | `api/member-store` |
| MembersController | `api/members` |
| MembershipPlansController | `api/membership-plans` |
| MembershipsController | `api/memberships` |
| NotificationsController | `api/notifications` |
| OffersController | `api/offers` |
| PaymentsController | `api/payments` |
| PromoCodesController | `api/promo-codes` |
| RefundsController | `api/refunds` |
| ReportsController | `api/reports` |
| SaleAdjustmentsController | `api/sales/adjustments` |
| SalesController | `api/sales` |
| ShiftsController | `api/shifts` |
| StaffNotificationsController | `api/staff-notifications` |
| TenantSettingsController | `api/settings` |
| TrialController | `api/trials` |
| ZReportController | `api/reports/z` |

Note: `InventoryCatalogController` and `InventoryStockController` both claim `api/inventory` (differentiated by action-level sub-routes) — worth flagging as a naming/overlap smell but not necessarily a bug.

### 1.2 Platform controllers (`GMS.Api/Platform/Controllers/*.cs`)

These do **not** inherit `BaseApiController`; each is a bare `ControllerBase` with its own `[ApiController]`, all under `platform-api/*`:

| Controller | Route |
|---|---|
| PlatformAuditController | `platform-api/audit` |
| PlatformAuthController | `platform-api/auth` |
| PlatformPingController | `platform-api` |
| PlatformMetricsController | `platform-api/metrics` |
| PlatformPaymentWebhooksController | `platform-api/webhooks` |
| PlatformPlansController | `platform-api/plans` |
| PlatformRiskQueueController | `platform-api/risk-queue` |
| PlatformSubscriptionController | `platform-api/tenants/{tenantId:guid}/subscription` |
| PlatformTenantUsersController | `platform-api/tenants/{tenantId:guid}/users` |
| PlatformTenantsController | `platform-api/tenants` |
| PlatformUsageController | `platform-api/usage` |
| PlatformUsersController | `platform-api/platform-users` |

### 1.3 Minimal API endpoints

None beyond framework wiring. `Program.cs:334-336` only calls `app.MapHealthChecks("/health")`, `app.MapHub<AttendanceHub>("/hubs/attendance")`, and `app.MapControllers()`. No `app.MapGet/MapPost/...` minimal-API routes exist anywhere in the solution (confirmed via repo-wide search).

### 1.4 Middleware pipeline (order, from `Program.cs` and `ProductionHostingExtensions.cs`)

Actual runtime order, in `app.UseGymFlowProductionPipeline()` (`GMS.Api/Extensions/ProductionHostingExtensions.cs:91-123`) followed by the rest of `Program.cs`:

1. `UseForwardedHeaders()` — `ForwardedHeaders.XForwardedFor|XForwardedProto|XForwardedHost`, with `KnownNetworks`/`KnownProxies` cleared (`ProductionHostingExtensions.cs:21-28`) — i.e. **any** proxy is trusted for forwarded headers (no allow-list), a hardening gap worth flagging but out of scope to fix here.
2. Dev only: `UseSwagger()` + `UseSwaggerUI()` at root (`RoutePrefix = string.Empty`); Prod: `UseHsts()` (`ProductionHostingExtensions.cs:95-107`).
3. `UseHttpsRedirection()`.
4. Conditionally `UseMiddleware<WebDashboardMiddleware>()` if `Hosting:ServeWebDashboard` is true and a `wwwroot/dashboard` folder exists (`ProductionHostingExtensions.cs:111-115`) — serves the bundled web dashboard SPA from the API host.
5. `UseStaticFiles()`.
6. `UseCors(corsPolicy)` — `"ProductionCors"` in Production, `"AllowAll"` otherwise (`ProductionHostingExtensions.cs:119-120`).
7. Back in `Program.cs:320`: `app.UseRateLimiter()`.
8. `app.UseAuthentication()` (`Program.cs:323`).
9. `app.UseMiddleware<TenantMiddleware>()` (`Program.cs:324`) — explicitly commented `// ORDER IS CRITICAL` (must run after auth, before authorization).
10. `app.UseAuthorization()` (`Program.cs:325`).
11. `app.UseHangfireDashboard("/hangfire", ...)` (`Program.cs:328-332`).
12. `app.MapHealthChecks("/health")`, `app.MapHub<AttendanceHub>("/hubs/attendance")`, `app.MapControllers()`.

**CORS exact configuration** (`ProductionHostingExtensions.cs:33-71`, `AddGymFlowCors`):
- Policy `"AllowAll"`: `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader().WithExposedHeaders("Token-Expired")` — used whenever not `IsProduction()`.
- Policy `"ProductionCors"`: if `Cors:AllowedOrigins` config array is non-empty, locks to `WithOrigins(origins)`; **else, if `env.IsProduction()`, falls back to `SetIsOriginAllowed(_ => true)`** (i.e. reflects any origin) — functionally open CORS in production if the origins list is left unconfigured. This is a real gap: a misconfigured/empty `Cors:AllowedOrigins` in production silently degrades to allow-any-origin (with credentials-less requests only, since no `AllowCredentials()` is set, but still broad).

**Rate limiting** (`Program.cs:90-129`, `AddRateLimiter`): three named **fixed-window** limiters, no global default limiter:
- `checkin-policy`: 30 req/min per limiter partition (partition key not shown at registration; applied via `[EnableRateLimiting("checkin-policy")]` on specific actions — **UNKNOWN — REQUIRES VERIFICATION** exact controller actions decorated with each policy name, not confirmed by this pass), `QueueLimit = 0` (reject immediately, no queueing).
- `member-activate-policy`: 10 req/min, same queueing behavior.
- `employee-activate-policy`: 10 req/min, same.
- `OnRejected` returns HTTP 429 with a bilingual JSON body (`Program.cs:121-128`).
- No global/default rate limiter is configured — any endpoint not explicitly decorated with one of the three named policies is unthrottled at this layer.

**Health checks**: `builder.Services.AddHealthChecks()` (`Program.cs:253`) with no custom checks registered (no DB/Hangfire/Redis health probes added) — bare `app.MapHealthChecks("/health")` only confirms the process is up, not DB/Hangfire connectivity. There is also a separate `HealthController` (`api/Health`, 17 lines) — **UNKNOWN — REQUIRES VERIFICATION** of its exact body since it was only line-counted, not read, in this pass.

**Swagger/OpenAPI**: `AddSwaggerGen` configured with title "HyMotion API", JWT Bearer security scheme (`Program.cs:36-64`); UI only exposed in Development (`ProductionHostingExtensions.cs:97-103`), not reachable in Production.

**API versioning**: none. No `AddApiVersioning`/versioned route templates found anywhere in the solution.

**SignalR**: `AddSignalR()` with optional Redis backplane gated by `SignalR:EnableRedisBackplane` config (`Program.cs:132-143`); one hub mapped, `AttendanceHub` at `/hubs/attendance` (`GMS.Api/Hubs/AttendanceHub.cs`, `Program.cs:335`). JWT is also accepted via `?access_token=` query string specifically for `/hubs` paths (`Program.cs:173-182`).

### 1.5 Global exception handling strategy

**No centralized exception-handling mechanism exists.** Specifically confirmed absent across the whole solution:
- No `app.UseExceptionHandler(...)` call.
- No `IExceptionFilter` implementation.
- No `IExceptionHandler` (the .NET 8 `Microsoft.AspNetCore.Diagnostics.IExceptionHandler` interface) implementation.
- No `AddProblemDetails()` call.

Error handling is instead **per-controller / per-service, via a `Result`/`Result<T>` pattern** (`GMS.Application/Common/Result.cs`) that services return, translated to HTTP responses by helper methods on `BaseApiController` (`GMS.Api/Controllers/BaseApiController.cs:30-39`):
```csharp
protected IActionResult FailureResult(Result result, int statusCode = StatusCodes.Status400BadRequest)
{
    if (result.IsSuccess) return Ok(new { message = result.Message });
    return StatusCode(statusCode, new { error = result.Error, message = result.Message ?? result.Error });
}
```
There is also an `AppError`/`AppMessageCatalog` pair in `GMS.Application/Common/` for bilingual (EN/AR) error bodies (`AppError.ToAnonymousBody()`, referenced at `BaseApiController.cs:24-28`). Any **unhandled** exception (a real bug, a null-ref, a DB timeout not caught by a controller/service `try/catch`) will fall through to the default ASP.NET Core behavior (bare 500, or the Developer Exception Page in Development) since no custom middleware/filter intercepts it. Individual jobs (e.g. `ZReportGenerationJob.cs:57-75`, `AnalyticsAggregationJob.cs:52-100`) do wrap their per-tenant loop bodies in local `try/catch` + `_logger.LogError`, but that is job-specific, not a framework-level policy.

### 1.6 Model validation approach

**FluentValidation is the dominant approach.** `builder.Services.AddFluentValidationAutoValidation()` (`Program.cs:67`) plus `services.AddValidatorsFromAssembly(typeof(ApplicationServiceExtensions).Assembly)` (`GMS.Application/ApplicationServiceExtensions.cs:112`) auto-registers all validators in the Application assembly. 31 validator files live under `GMS.Application/Validators/` (one subfolder: `Validators/Inventory`).

DataAnnotations are used in exactly one place found in this pass: `GMS.Application/DTOs/Provisioning/ProvisionTenantDtos.cs` (uses `[Required]`/similar attributes) — everywhere else in `GMS.Application/DTOs/*` (160 DTO files across 29 feature folders: Activities, Admin, Analytics, Attendance, Audit, Auth, CallSheet, Dashboard, Debtors, Expenses, Hr, Imports, Inventory, Invitation, Invoices, MemberStore, Members, Memberships, Notifications, Offers, Plans, Promo, Provisioning, Refunds, Reports, Sales, Shifts, Trials, ZReports) validation is delegated to FluentValidation validators, not attributes. This is a mostly-consistent (not 100%) FluentValidation-first approach.

### 1.7 Serialization config

No global `AddJsonOptions`/`JsonSerializerOptions` customization was found for `AddControllers()` in `Program.cs` — default System.Text.Json settings apply (camelCase property naming, default enum-as-number behavior, etc., per ASP.NET Core defaults). One custom converter exists: `GMS.Application/Serialization/NullableDateOnlyJsonConverter.cs` — **UNKNOWN — REQUIRES VERIFICATION** whether/where it is actually registered into the pipeline's `JsonSerializerOptions`, since no `AddJsonOptions` call was found wiring it in; it may only be used for manual `JsonSerializer.Serialize` calls elsewhere rather than MVC's default serializer.

---

## 2. Application Layer (GMS.Application)

### 2.1 Service inventory (`GMS.Application/Services/*.cs`, 82 files, ~30,452 lines total)

Sorted by size; "monolithic?" flags anything materially >500 lines or visibly mixing unrelated concerns.

| Service | Lines | What it does | Monolithic? |
|---|---:|---|---|
| ReportsService.cs | 1255 | General business reports (revenue, membership, attendance, etc.) | **Yes** — largest service by far, likely several report families in one class |
| SaleService.cs | 1009 | POS sale lifecycle: create/void/pay/line items | **Yes** — core commerce logic, very large |
| AuthService.cs | 896 | Login, OTP, JWT issuance, staff/member/employee auth flows | **Yes** — mixes multiple auth flows in one class |
| CallSheetService.cs | 886 | Follow-up call queue / lead management | **Yes** |
| ImportService.cs | 861 | Bulk member import (upload/validate/execute/rollback) | **Yes** |
| MembershipService.cs | 799 | Membership lifecycle (create/renew/freeze/expire) | **Yes** |
| AdminService.cs | 787 | Tenant/staff admin operations | **Yes** |
| CheckinService.cs | 762 | QR/manual check-in, occupancy gating | **Yes** |
| MemberStoreService.cs | 748 | Member-facing storefront (browse/order) | **Yes** |
| RefundService.cs | 720 | Refund processing across sale/membership/product | **Yes** |
| StockLedgerService.cs | 689 | Inventory stock ledger movements | **Yes** |
| SessionBookingService.cs | 663 | Activity/PT session booking | **Yes** |
| PurchaseOrderService.cs | 645 | Inventory purchase orders | **Yes** |
| TenantSettingsService.cs | 643 | Tenant configuration key/value settings | **Yes** |
| EmployeeService.cs | 635 | HR employee CRUD + provisioning | **Yes** |
| StockTransferService.cs | 607 | Inter-warehouse stock transfers | **Yes** |
| DashboardService.cs | 604 | Owner/manager dashboard KPIs | **Yes** |
| InventoryReportService.cs | 555 | Inventory-specific reporting/alerts | borderline |
| InvoiceService.cs | 545 | Invoice generation from sales | borderline |
| MemberService.cs | 525 | Member CRUD/profile | borderline |
| NotificationService.cs | 513 | Staff notification publishing | borderline |
| ReferralRewardService.cs | 509 | Referral reward computation/granting | borderline |
| ProfitabilityService.cs | 505 | Margin/profitability analytics | borderline |
| InvitationService.cs | 502 | Guest-pass/referral invitations | borderline |
| ProductCatalogService.cs | 467 | Inventory product catalog | — |
| ShiftService.cs | 459 | Cashier shift open/close | — |
| OfferService.cs | 443 | Promotional offers | — |
| AnalyticsService.cs | 433 | Analytics aggregation reads | — |
| StockAdjustmentService.cs | 392 | Manual stock adjustments | — |
| TenantProvisioningService.cs | 375 | New-tenant setup | — |
| StockCountService.cs | 372 | Physical stock counts | — |
| MembershipPlanService.cs | 372 | Membership plan CRUD | — |
| MemberBookingService.cs | 363 | Member-side activity booking | — |
| InvoiceDocumentHtmlBuilder.cs | 359 | Invoice HTML→PDF template building | — |
| EmployeeAttendanceService.cs | 342 | HR clock-in/out | — |
| TrialService.cs | 324 | Free-trial membership logic | — |
| LeaveRequestService.cs | 315 | HR leave requests | — |
| PromoService.cs | 315 | Promo code redemption | — |
| SupplierService.cs | 313 | Inventory suppliers | — |
| PayrollPeriodService.cs | 310 | Payroll period lifecycle | — |
| DebtorsService.cs | 305 | Outstanding balances / debtor tracking | — |
| CashExpenseService.cs | 302 | Cash drawer expense entries | — |
| PaymentService.cs | 299 | Payment transaction recording | — |
| ActivityService.cs | 279 | Activities/facilities catalog | — |
| MemberAppActivationService.cs | 266 | Member mobile-app activation codes | — |
| MemberClassService.cs | 262 | Member class enrollment | — |
| ReferralAttributionService.cs | 260 | Referral source attribution | — |
| DropInService.cs | 259 | Walk-in/drop-in session sales | — |
| EmployeeAppActivationService.cs | 222 | Employee mobile-app activation | — |
| WarehouseService.cs | 221 | Inventory warehouses | — |
| ActivityEntitlementService.cs | 211 | Membership-plan activity entitlements | — |
| SessionGenerationService.cs | 211 | Recurring→concrete session generation | — |
| RolePermissionService.cs | 205 | Role/permission CRUD | — |
| AuditService.cs | 203 | Audit log writes/reads | — |
| InvoicePdfModelFactory.cs | 204 | Invoice PDF view-model mapping | — |
| PlatformTenantStaffService.cs | 193 | Platform-side staff visibility into a tenant | — |
| SaleAdjustmentService.cs | 190 | Post-sale price/line adjustments | — |
| EmailOtpDeliveryStrategy.cs | 181 | OTP delivery via email | — |
| InventoryReorderCalculator.cs | 178 | Reorder-point math | — |
| EmployeeScheduleService.cs | 178 | HR shift schedules | — |
| EmployeeDocumentService.cs | 169 | HR document storage refs | — |
| Code128SvgRenderer.cs | 157 | Barcode SVG rendering | — |
| RolePermissionResolver.cs | 154 | Effective-permission resolution | — |
| AccessCardHtmlBuilder.cs | 151 | Access-card HTML template | — |
| PositionService.cs | 148 | HR job positions | — |
| EmployeeShiftService.cs | 133 | HR employee shift assignment | — |
| PayrollPaymentService.cs | 131 | Payroll disbursement recording | — |
| LeaveBalanceService.cs | 121 | HR leave balance tracking | — |
| DepartmentService.cs | 116 | HR departments | — |
| RenderAndDeliverInvoiceJob.cs | 113 | Invoice PDF render+deliver (stub, see below) | — |
| PlatformImpersonationService.cs | 106 | Platform-support "log in as tenant" | — |
| GymOccupancyService.cs | 100 | Real-time floor occupancy count | — |
| HrDashboardService.cs | 98 | HR summary KPIs | — |
| GymQrTokenService.cs | 96 | QR check-in token generation | — |
| PayrollAdjustmentService.cs | 93 | Payroll manual adjustments | — |
| MembershipRenewalDating.cs | 87 | Renewal date math helper | — |
| AttendanceCalculator.cs | 76 | Attendance duration/summary math | — |
| StaffAppUserProvisioner.cs | 71 | Creates AppUser for staff | — |
| HrAuditSnapshots.cs | 37 | HR audit snapshot helper | — |
| GymFloorBootstrap.cs | 36 | Seeds default floor/zones | — |
| StaffNotificationPublisher.cs | 31 | Publishes staff notification events | — |
| NullStaffNotificationRealtimeNotifier.cs | 10 | No-op notifier (DI default) | — |

11 services exceed the ~500-line threshold outright, with `ReportsService` (1255) and `SaleService` (1009) standing out as the two most monolithic — both mix several sub-domains of logic (multiple report types; sale creation + void + payment + line-item math, respectively) in one class rather than being split by responsibility.

### 2.2 DTOs and Validators organization

- DTOs: `GMS.Application/DTOs/<Feature>/*.cs`, 29 feature subfolders, 160 files total (§1.6). Organized by bounded feature area, generally 1 file per request/response family per feature — reasonably consistent.
- Validators: `GMS.Application/Validators/*.cs` + one `Validators/Inventory` subfolder, 31 files — thinner than the DTO surface, meaning many DTOs (esp. simple filter/query DTOs) have no dedicated validator, which is expected/normal, not necessarily a gap.

### 2.3 Duplicated business logic across services

**Stringly-typed status comparisons are repeated across many services rather than centralized** (see §2.4) — e.g., membership status checks (`m.Status == "active"`) appear independently in `CheckinService.cs:455,548,573`, `AnalyticsService.cs:395,398`, `DashboardService.cs:134,143-144`, `CallSheetService.cs:291,417,512,532`, `StaffNotificationReminderJob.cs:76`, `GMS.Infrastructure/Jobs/DailyDigestJob.cs:46,51`, `GMS.Infrastructure/Jobs/MembershipExpiryNotificationsJob.cs:50`, `GMS.Infrastructure/Jobs/MembershipStatusExpiryJob.cs:38`, `GMS.Infrastructure/Jobs/TrialExpirySetterJob.cs` (via `TrialOutcome` string) — each call site re-derives "what does active/expired/frozen mean" rather than going through one shared query helper/spec object. `MembershipOperational` (`GMS.Core.Utilities`, referenced at `MembershipStatusExpiryJob.cs:7,33,45` and `TrialExpirySetterJob.cs`) centralizes *some* of this (e.g. `TodayCairo()`, `TryMarkExpired(...)`), but the read-side `Status == "..."` filters are duplicated ad hoc rather than routed through it.

**Sale/debt "partially_paid" status logic** is repeated identically in `DebtorsService.cs:107,156,214` and `CallSheetService.cs:493` and `GMS.Infrastructure/Jobs/DailyDigestJob.cs:55` (`s.Status == "partially_paid" && s.AmountDue > 0`) — same predicate, four independent call sites.

**Cairo-timezone lookup** (`TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time")`) is repeated as a local `static readonly` field in at least 8 separate job classes (`DailyDigestJob.cs:17`, `TrialExpirySetterJob.cs:16`, `TrialFollowUpJob.cs:17`, `InventoryLowStockJob.cs:18`, `StaffNotificationReminderJob.cs:19`, `RollUpTenantUsageJob.cs:20`, plus the hardcoded string literal `"Egypt Standard Time"` re-declared in `GMS.Infrastructure/Jobs/JobScheduler.cs:14` and `PlatformHealthJobScheduler.cs:22` and `PlatformUsageJobScheduler`/`PlatformAutomationJobScheduler`-adjacent code) instead of one shared constant/helper — a portability risk too, since `"Egypt Standard Time"` is the **Windows** timezone ID; this would need to become an IANA ID (`Africa/Cairo`) or use `TimeZoneInfo.FindSystemTimeZoneById` with ICU support if this code ever runs on Linux (relevant to a future non-Windows local deployment target, though the stated target here is Windows).

**Money rounding**: `Math.Round`/`MidpointRounding` calls appear independently across 22 files (`GymOccupancyService.cs`, `ProfitabilityService.cs`, `PayrollPaymentService.cs`, `AnalyticsService.cs`, `MemberStoreService.cs`, `CashExpenseService.cs`, `SaleService.cs`, `SaleAdjustmentService.cs`, `PaymentService.cs`, `EmployeeAttendanceService.cs`, `CheckinService.cs`, `MemberClassService.cs`, `CallSheetService.cs`, `InventoryReportService.cs`, `PayrollPeriodService.cs`, `EmployeeAppActivationService.cs`, `AttendanceCalculator.cs`, `ReportsService.cs`, `MemberAppActivationService.cs`, `InventoryReorderCalculator.cs`, `ReferralRewardService.cs`, `PromoService.cs`, plus `RollUpTenantUsageJob.cs:82` in Platform) — no single shared "Money"/rounding value-object or helper was found being used consistently (`GMS.Core/ValueObjects` exists but **UNKNOWN — REQUIRES VERIFICATION** whether a Money VO is defined there and simply under-adopted, or absent entirely — not opened in this pass). Each call site picks its own rounding call directly.

### 2.4 "Stringly typed" statuses

Confirmed extensive use of raw string status fields instead of enums. `GMS.Core/Enums/` contains only **three** enums total: `GymMembershipType.cs`, `ManualCheckinReason.cs`, `UserRole.cs`. Everything else — membership status (`active`/`expired`/`frozen`), sale status (`partially_paid`, etc.), shift status (`open`), cash-expense status (`open`/`posted`), import-batch status (`validating`/`importing`/`dry_run_ready`/`rolled_back`/`failed`/`completed`), call-sheet follow-up status (`pending`/`completed`/`cancelled`/`in_progress`/`no_answer`), trial outcome (`active_trial`/`expired`), drop-in session status (`cancelled`/`completed`/`success`/`settled`) — is a bare `string` compared via literal (`Status == "active"`, `Status = "posted"`, etc.). Representative hits (60+ found in `GMS.Application/Services/*.cs` alone; not exhaustive): `CashExpenseService.cs:55,91,183,188,216`, `CallSheetService.cs:142,249,291,399,417,493,512,532,587,592,595,597,622,665,694,717,829`, `ImportService.cs:111,125,157,187,229,254,294,346,468,474,479,490,501,543`, `DropInService.cs:71,163,175,176`, `AdminService.cs:360,361,369,370,420,421` (uses string literals even in audit-log payloads, not just entity fields), `EmployeeService.cs:459`, plus the job-layer hits already cited in §2.3.

Partial mitigation: `GMS.Core/Constants/*.cs` defines **string-constant catalogs** for many of these domains (e.g. `MemberOrderStatuses.cs`, `PurchaseOrderStatuses.cs`, `StockCountStatuses.cs`, `StockTransferStatuses.cs`, `ActivityBookingStatuses.cs`) — so some newer/narrower features do route status literals through a named constant rather than a bare string, but core high-traffic domains (membership, sale, shift, call-sheet, cash-expense, import) still compare directly against inline string literals rather than even those constants in the call sites enumerated above.

### 2.5 Magic values / hardcoded config-like literals

- Cron schedules are hardcoded string literals inline at each `RecurringJob.AddOrUpdate` call site (`JobScheduler.cs:31,38,45,52,59,66,73,80,87,94`; `SessionGenerationJob.cs:80`; `RollUpTenantUsageJob.cs:131`; `PlatformHealthJobScheduler.cs:26`) rather than sourced from configuration — changing any schedule requires a code change + redeploy, not a config edit.
- `"Egypt Standard Time"` Windows timezone ID hardcoded in ~8+ files (§2.3) rather than one named constant.
- Rate-limit thresholds (30/min, 10/min, 10/min) are hardcoded in `Program.cs:96,105,114` rather than configuration-bound.
- `TenantMiddleware.cs:20`: `TenantCacheDuration = TimeSpan.FromMinutes(10)` hardcoded (not config-bound), though the related suspension buffer *is* config-bound: `configuration.GetValue("PlatformBilling:SuspensionCheckinBufferHours", 72)` (`TenantMiddleware.cs:119`) — inconsistent pattern (some tunables are config, some are constants).
- `RollUpTenantUsageJob.cs:42`: WhatsApp overage rate has a config-with-fallback default: `_configuration.GetValue("PlatformBilling:WhatsAppOverageEgpPerMessage", 0.35m)` — good pattern, contrast with the above.
- `ExecuteImportJob`/`ImportService`: "chunks of 500" (per `ExecuteImportJob.cs` doc-comment) — **UNKNOWN — REQUIRES VERIFICATION** of the exact literal location inside `ImportService.cs`, not traced to a line number in this pass, but the constant is described as inline rather than configurable.
- `PlatformPaymentServices.cs:326`: `_configuration["PlatformBilling:InstapayBaseUrl"] ?? "https://instapay.example/pay"` — config-bound with an obviously-placeholder fallback URL (`instapay.example` is a reserved/example domain, not live) — flagged in §2.6 too.

### 2.6 Hardcoded URLs

All `http(s)://` literals found solution-wide, with disposition:

| File:line | URL | Real external dependency or doc/example? |
|---|---|---|
| `GMS.Infrastructure/InfrastructureServiceExtensions.cs:85` | `https://api.4jawaly.com/` | **Real** — typed `HttpClient.BaseAddress` for WhatsApp provider (4jawaly) |
| `GMS.Infrastructure/InfrastructureServiceExtensions.cs:94` | `https://accept.paymob.com/` | **Real** — typed `HttpClient.BaseAddress` for Paymob |
| `GMS.Infrastructure/InfrastructureServiceExtensions.cs:103` | `https://www.atfawry.com/` | **Real** — typed `HttpClient.BaseAddress` for Fawry |
| `GMS.Platform/PlatformServiceExtensions.cs:43` | `https://accept.paymob.com/` | **Real** — Platform-side merchant Paymob client |
| `GMS.Platform/PlatformServiceExtensions.cs:49` | `https://www.atfawry.com/` | **Real** — Platform-side merchant Fawry client |
| `GMS.Infrastructure/Services/PaymobService.cs:13` | `https://docs.paymob.com/` | Doc-comment only (API reference link) |
| `GMS.Infrastructure/Services/PaymobService.cs:100` | `https://accept.paymob.com/api/acceptance/iframes/{iframeId}?...` | **Real** — constructs the live checkout redirect URL |
| `GMS.Platform/Services/PlatformPaymentServices.cs:326` | `https://instapay.example/pay` (fallback default) | **Placeholder/example** — reserved `.example` domain, used only if `PlatformBilling:InstapayBaseUrl` config is unset |
| `GMS.Platform/Services/PlatformPaymentServices.cs:431,435` | `https://accept.paymob.com/mock-platform?...` | **Mock** — explicitly named "mock-platform" in a stubbed method |
| `GMS.Application/Services/Code128SvgRenderer.cs:41` | `http://www.w3.org/2000/svg` | Not external dependency — SVG XML namespace URI (required boilerplate, not a network call) |
| `GMS.Application/Services/AccessCardHtmlBuilder.cs:145-146` | `https://`/`http://` prefix check | Not a hardcoded target — runtime `string.StartsWith` guard against an already-variable URL |
| `GMS.Application/Services/InvoiceDocumentHtmlBuilder.cs:353-354` | same as above | Same — runtime guard, not a hardcoded endpoint |
| `GMS.Tests/Platform/PlatformBillingCoreTests.cs:464,467` | `https://instapay.test/pay`, `https://example.test/` | Test-only fixtures |
| `GMS.Tests/LoadTests/LoadTest.cs:8` | `https://localhost:5001` | Doc-comment / usage example in a load-test script |

**Relevant to the offline feasibility study**: three real, hardcoded third-party HTTP dependencies (4jawaly WhatsApp, Paymob, Fawry) are wired at DI-registration time with no environment/config-based swap to a local/no-op implementation (see §3 for the operational consequence on background jobs).

### 2.7 Domain events / messaging pattern

**No domain-event or mediator pattern is in use.** Repo-wide search for `DomainEvent`, `IEventHandler`, `MediatR`, `IDomainEvent`, `INotification` found only unrelated hits — all are the **staff/member "Notification" feature** (`NotificationService.cs`, `INotificationService.cs`, `StaffNotificationPublisher.cs`, `NotificationsController.cs`, `StaffNotificationsController.cs`, `StaffNotificationReminderJob.cs`, `RenderAndDeliverInvoiceJob.cs`, `Program.cs`'s DI registrations, `InventoryReportService.cs`), which is an application-level "in-app/WhatsApp notification" feature, not an architectural domain-events/pub-sub mechanism. There is no MediatR package reference and no custom `IDomainEvent`/handler dispatch pipeline anywhere in the solution.

### 2.8 Transaction handling

`BeginTransactionAsync`/`IDbContextTransaction`/`IExecutionStrategy` usage found in 18 files: `InvitationService.cs`, `RefundService.cs`, `CashExpenseService.cs`, `SaleService.cs`, `StockLedgerService.cs`, `SaleAdjustmentService.cs`, `PurchaseOrderService.cs`, `PaymentService.cs`, `SessionBookingService.cs`, `AuthService.cs`, `InvoiceService.cs`, `GMS.Platform/Persistence/SubscriptionWriteRepository.cs`, `GMS.Platform/Services/PlatformBillingServices.cs`, `TenantProvisioningService.cs`, `StockAdjustmentService.cs`, `StockTransferService.cs`, `StockCountService.cs`, `TrialService.cs`. This is a **per-service, ad hoc explicit-transaction pattern** — each service that needs multi-step atomicity opens its own `BeginTransactionAsync`/commit around its own `SaveChangesAsync` calls. There is no repository-level Unit-of-Work abstraction wrapping this uniformly: `GMS.Infrastructure/Repositories/Repository.cs` is a generic `IRepository<T>` (confirmed to exist at `GMS.Infrastructure/Repositories/Repository.cs`) but **UNKNOWN — REQUIRES VERIFICATION** whether it exposes any shared `SaveChangesAsync`/transaction-scoping surface, or whether services simply inject `GymFlowProDbContext` directly alongside/instead of the repository (the latter appears to be the dominant pattern based on the job classes reviewed in §3, which all resolve `GymFlowProDbContext` directly via `IServiceScopeFactory`).

---

## 3. Background Jobs

### 3.1 Hangfire configuration

- Package/config confirmed in `GMS.Infrastructure/InfrastructureServiceExtensions.cs:110-127`:
  ```csharp
  services.AddHangfire(config => config
      .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
      .UseSimpleAssemblyNameTypeSerializer()
      .UseRecommendedSerializerSettings()
      .UseSqlServerStorage(connectionString, new SqlServerStorageOptions { ... SchemaName = "HangFire" }));
  services.AddHangfireServer(options => options.WorkerCount = configuration.GetValue("Hangfire:WorkerCount", 2));
  services.AddHostedService<JobScheduler>();
  ```
  Storage is SQL Server (same DB as tenant data, dedicated `HangFire` schema), `WorkerCount` config-bound (default 2), `CommandBatchMaxTimeout = 5min`, `SlidingInvisibilityTimeout = 5min`, `QueuePollInterval = 15s`, `UseRecommendedIsolationLevel = true`, `DisableGlobalLocks = true`.
- Dashboard: `app.UseHangfireDashboard("/hangfire", ...)` (`Program.cs:328-332`) gated by `HangfireDashboardAuthFilter` (`GMS.Api/Filters/HangfireDashboardAuthFilter.cs`) — open in Development, restricted to `PlatformRoles.Admin` (via the separate `PlatformBearer` JWT scheme, never the tenant scheme) in Production.
- `GMS.Application` and `GMS.Platform` both also reference Hangfire directly (`RecurringJob.AddOrUpdate`, `BackgroundJob.Enqueue`, `[AutomaticRetry]`) — confirmed via repo-wide search; it is used as the single job engine across all three layers, not just Infrastructure.

### 3.2 IHostedService / BackgroundService implementations

11 `IHostedService` implementations found (no `BackgroundService` subclasses — all are raw `IHostedService`, used purely to register Hangfire recurring jobs on startup, not to run long-lived loops themselves):

- `GMS.Infrastructure.Jobs.JobScheduler` — registers 10 recurring jobs (§3.3) + enqueues one immediate catch-up job on boot.
- `GMS.Application.Jobs.ZReportJobScheduler`, `GuestPassExpiryJobScheduler`, `SessionGenerationJobScheduler`, `ProcessReferralRewardHoldsJobScheduler`, `InventoryLowStockJobScheduler`, `StaffNotificationReminderJobScheduler` — each registers one recurring job; split out from `GMS.Infrastructure.Jobs.JobScheduler` specifically because these jobs depend on Application-layer interfaces and `GMS.Infrastructure` does not reference `GMS.Application` (explicit comment, `ApplicationServiceExtensions.cs:102-103`).
- `GMS.Platform.Services.PlatformUsageJobScheduler`, `PlatformAutomationJobScheduler`, `PlatformHealthJobScheduler`, `PlatformRenewalJobScheduler` — Platform-layer recurring-job registration, same pattern.

### 3.3 Every recurring/scheduled job found

All times are Cairo (Egypt Standard Time, Windows TZ ID) unless noted. "Offline?" assesses whether the job's *current* implementation could run with zero internet access as-is.

| Job | Schedule | What it does | External/Internet dependency | Offline-capable as-is? |
|---|---|---|---|---|
| `MembershipExpiryNotificationsJob` (`GMS.Infrastructure/Jobs/MembershipExpiryNotificationsJob.cs`) | 07:00 daily | Finds memberships expiring in 7/3/1/0 days, enqueues one WhatsApp job per member | `IWhatsAppService` → real 4jawaly HTTP client (no mock swap registered) | **No** — will fail/error per member without internet; job itself completes (send calls are fire-and-forget enqueues to a child job, so the parent job "succeeds" but child WhatsApp jobs would fail/retry-exhaust) |
| `BirthdayGreetingsJob` (`.../BirthdayGreetingsJob.cs`) | 06:00 daily | Finds today's-birthday members, enqueues WhatsApp greeting + discount code | Same as above | **No**, same caveat |
| `AnalyticsAggregationJob` (`.../AnalyticsAggregationJob.cs`) | 00:00 daily | Raw-SQL MERGE upsert of per-tenant KPI snapshot (`gym_analytics_snapshots`) | None — pure DB | **Yes** |
| `ClassRemindersJob` (`.../ClassRemindersJob.cs`) | every 30 min | **Placeholder/no-op** — logs only; "Class/Booking entities not yet implemented — skipping" (doc-comment + code, `ClassRemindersJob.cs:9,24-26`) | N/A (not implemented) | **Yes** (trivially, since it does nothing) |
| `InvitationQuotaResetJob` (`.../InvitationQuotaResetJob.cs`) | 1st of month, 00:00 | **No-op by design** — just logs a period rollover; actual quota is computed live from data, not reset | None | **Yes** |
| `TrainerCommissionReportJob` (`.../TrainerCommissionReportJob.cs`) | 1st of month, 00:00 | **Placeholder/no-op** — "Trainer/Commission entities not yet implemented — skipping" (`TrainerCommissionReportJob.cs:9,27-28`) | N/A (not implemented) | **Yes** (does nothing) |
| `TrialFollowUpJob` (`.../TrialFollowUpJob.cs`) | 09:00 daily | Sends "last day" + "2-days-expired follow-up" WhatsApp nudges for trial memberships; also flips `TrialOutcome` to `expired` as a side effect | `IWhatsAppService` (real) + `IFeatureAccessService` (Platform-layer feature-flag check) | **Partially** — the DB status-flip (`TrialOutcome = "expired"`) happens regardless of WhatsApp send success; only the notification send needs internet |
| `TrialExpirySetterJob` (`.../TrialExpirySetterJob.cs`) | 00:00 daily | Safety-net sweep: flips trial memberships past `EndDate` from `active_trial` to `expired` | `IFeatureAccessService` (Platform-layer call to check per-tenant "trials" flag) — DB-only otherwise | **Yes** for the DB work, but depends on the Platform feature-flag service being present/stubbed in an offline build |
| `MembershipStatusExpiryJob` (`.../MembershipStatusExpiryJob.cs`) | 00:05 daily (+ once on every app boot) | Flips `active`/`frozen` memberships past `EndDate` to `expired` | None — pure DB | **Yes** |
| `DailyDigestJob` (`.../DailyDigestJob.cs`) | 09:00 daily | Per-tenant WhatsApp digest to Owner/Manager: expiring memberships + debtor totals | `IWhatsAppService` (real) | **No** — skipped/fails without internet (job itself won't throw — it iterates tenants and only sends if `IsNullOrWhiteSpace(recipient.PhoneNumber)` is false — but the actual send will fail against the real HTTP client) |
| `ZReportGenerationJob` (`GMS.Application/Jobs/ZReportGenerationJob.cs`, scheduled by `ZReportJobScheduler`) | 23:59 daily (per doc-comment; scheduler file itself not opened in this pass to confirm cron string — **UNKNOWN — REQUIRES VERIFICATION**) | Builds/stores nightly Z-Report per tenant, renders+uploads PDF (`IFileStorageService` — `LocalFileStorageService` per DI registration, so this part is local-disk, not cloud), WhatsApps it to Owners, flags shifts open >8h to Managers/Owners (never auto-closes) | `IWhatsAppService` (real) for delivery/alerts; PDF storage itself is local | **Partially** — report building + PDF generation + local storage all work offline; only the WhatsApp delivery leg needs internet, and its failure is caught per-tenant (`catch (Exception ex)` at job level) so it won't crash the whole run |
| `GuestPassExpiryJob` (`GMS.Application/Jobs/GuestPassExpiryJob.cs`, via `GuestPassExpiryJobScheduler`) | Recurring (cron in scheduler file, not opened — **UNKNOWN — REQUIRES VERIFICATION** exact cron) | Expires overdue pending guest-pass invitations via `IInvitationService.ExpireOverdueGuestPassesAsync()` | None — pure DB via Application service | **Yes** |
| `InventoryLowStockJob` (`GMS.Application/Jobs/InventoryLowStockJob.cs`, via `InventoryLowStockJobScheduler`) | Daily (cron in scheduler file — **UNKNOWN — REQUIRES VERIFICATION** exact time) | Per-tenant low-stock + batch-expiry alerts via `IInventoryReportService.RunDailyAlertsAsync`, gated by `IFeatureAccessService` inventory feature flag | Platform `IFeatureAccessService` check; alert delivery mechanism itself **UNKNOWN — REQUIRES VERIFICATION** (not traced into `RunDailyAlertsAsync`'s internals in this pass — may or may not call WhatsApp) | **Likely yes** for the DB/alert-record side; delivery channel unconfirmed |
| `ProcessReferralRewardHoldsJob` (`GMS.Application/Jobs/ProcessReferralRewardHoldsJob.cs`, via `ProcessReferralRewardHoldsJobScheduler`) | Recurring (cron not opened — **UNKNOWN — REQUIRES VERIFICATION**) | Grants due referral rewards (credit/free-days) via `IReferralRewardService.ProcessDueHoldsAsync()` | None found — pure DB via Application service | **Yes** |
| `SessionGenerationJob` (`GMS.Application/Jobs/SessionGenerationJob.cs`, via `SessionGenerationJobScheduler`) | Hourly at :05 (`SessionGenerationJob.cs:80`, confirmed cron `"5 * * * *"`) | Generates upcoming activity sessions from recurring schedules; finalizes elapsed sessions, marks no-shows | None — pure DB via `ISessionGenerationService` | **Yes** |
| `StaffNotificationReminderJob` (`GMS.Application/Jobs/StaffNotificationReminderJob.cs`, via `StaffNotificationReminderJobScheduler`) | Hourly (per doc-comment; exact cron in scheduler file not opened — **UNKNOWN — REQUIRES VERIFICATION**) | Publishes in-app staff notifications (trial ending, membership expiring/expired, follow-up due/overdue) via `INotificationService.PublishStaffAsync` — idempotent via dedupe keys | None confirmed at this layer — `INotificationService`'s internal delivery mechanism (in-app only vs. also WhatsApp) **UNKNOWN — REQUIRES VERIFICATION** (not opened) | **Likely yes** if delivery is in-app-only; unconfirmed |
| `CreateInvoiceForSaleJob` (`GMS.Application/Jobs/CreateInvoiceForSaleJob.cs`) | One-off, enqueued per sale (not recurring) | Creates invoice for a sale, enqueues `IInvoiceDeliveryJob` | None directly — DB + enqueue | **Yes** |
| `RenderAndDeliverInvoiceJob` (`GMS.Application/Services/RenderAndDeliverInvoiceJob.cs`, `IInvoiceDeliveryJob`) | One-off, enqueued by `CreateInvoiceForSaleJob` | Per its own doc-comment referenced elsewhere, "(currently stubbed, P7)" | **UNKNOWN — REQUIRES VERIFICATION** (file not opened in this pass beyond the cross-reference in `CreateInvoiceForSaleJob.cs:13`) | **UNKNOWN** |
| `ExecuteImportJob` (`GMS.Application/Jobs/ExecuteImportJob.cs`) | One-off, enqueued by `IImportService.EnqueueExecuteAsync` | Processes a validated import batch's rows in chunks of 500, creates Member+Membership rows, marks batch `completed` | None — pure DB via `IImportService.ExecuteAsync` | **Yes** |
| `ValidateImportJob` (`GMS.Application/Jobs/ValidateImportJob.cs`) | One-off, enqueued by `IImportService.UploadAsync`/etc. | Re-validates/maps all rows of an import batch, sets `dry_run_ready` | None — pure DB via `IImportService.ValidateAsync` | **Yes** |
| `RollUpTenantUsageJob` (`GMS.Platform/Services/RollUpTenantUsageJob.cs`, via `PlatformUsageJobScheduler`) | 01:30 daily (`RollUpTenantUsageJob.cs:131`, confirmed cron `"30 1 * * *"`) | Platform-plane nightly usage-counter upsert (WhatsApp message counts, overage billing) per subscribed tenant | Platform DB only — no outbound network call itself (it *counts* WhatsApp usage that other jobs already sent) | **Yes** — but conceptually irrelevant to a single-gym offline edition (no multi-tenant billing) |
| `ProcessAutomationEnrollmentsJob` (`GMS.Platform/Services/ProcessAutomationEnrollmentsJob.cs`, via `PlatformAutomationJobScheduler`) | Every minute (`Cron.Minutely`, `ProcessAutomationEnrollmentsJob.cs:106`) | Advances dunning/automation-sequence enrollments (e.g. payment-reminder cadences) via `IAutomationSequenceHandler` | Depends on the handler (`PlatformInvoiceDunningHandler` — **UNKNOWN — REQUIRES VERIFICATION** whether it sends WhatsApp/email) | Platform-only concern, N/A to a local single-gym edition |
| `TenantHealthScoreService` (as `IComputeTenantHealthScoresJob`, via `PlatformHealthJobScheduler`) | 03:00 daily (`PlatformHealthJobScheduler.cs:26`) | Computes rules-based tenant health scores (explicitly "no ML" per doc-comment) | Platform DB only | Platform-only concern, N/A to a local single-gym edition |
| `ProcessSubscriptionRenewalsJob` (via `PlatformRenewalJobScheduler`, `GMS.Platform/Services/PlatformBillingServices.cs:724-`) | 02:00 daily (per `PlatformHealthJobScheduler`'s comment ordering; exact cron **UNKNOWN — REQUIRES VERIFICATION**, not read past the class declaration in this pass) | Subscription renewal/billing processing | Likely calls Paymob/Fawry payment services (real HTTP) — **UNKNOWN — REQUIRES VERIFICATION**, not traced | Platform-only concern, N/A to a local single-gym edition |

**Summary for the Local Lifetime Edition feasibility angle**: the tenant-side recurring jobs split roughly into three buckets:
1. **Pure-DB, fully offline-capable as-is**: `AnalyticsAggregationJob`, `MembershipStatusExpiryJob`, `SessionGenerationJob`, `GuestPassExpiryJob`, `ProcessReferralRewardHoldsJob`, `ExecuteImportJob`, `ValidateImportJob`, `CreateInvoiceForSaleJob`, `InvitationQuotaResetJob` (no-op), `ClassRemindersJob` (no-op placeholder), `TrainerCommissionReportJob` (no-op placeholder).
2. **Mixed — DB work succeeds, only the notification-send leg needs internet, and that leg's failure is already isolated by try/catch or fire-and-forget enqueue**: `TrialFollowUpJob`, `TrialExpirySetterJob` (depends on Platform feature-flag call, not internet), `ZReportGenerationJob`.
3. **Would need a config/DI swap to a no-op or local channel (SMS/email/print) to be meaningfully useful offline**: `MembershipExpiryNotificationsJob`, `BirthdayGreetingsJob`, `DailyDigestJob` — all three call `IWhatsAppService`, which is unconditionally bound to the real 4jawaly HTTP client in every environment (§2.6, §3.4) — a config change alone (not just environment) would be needed since there is no existing "local/offline" WhatsApp implementation registered anywhere, only `MockWhatsAppService` (used for local dev testing, not wired as a fallback) and the real client.

Platform-plane jobs (`RollUpTenantUsageJob`, `ProcessAutomationEnrollmentsJob`, tenant health scoring, subscription renewals) are conceptually inapplicable to a single-gym, non-SaaS local deployment regardless of their offline-capability, since they all serve the multi-tenant billing/subscription model described in §0.

### 3.4 Queue-based processing

No custom in-process queue abstraction exists (`IBackgroundTaskQueue`, `System.Threading.Channels`, or similar patterns: zero matches repo-wide). All asynchronous/deferred work goes through **Hangfire** (`BackgroundJob.Enqueue<T>(...)` for one-off child jobs, `RecurringJob.AddOrUpdate<T>(...)` for scheduled jobs) backed by SQL Server storage — i.e., Hangfire's SQL-backed queue is the only queueing mechanism in the codebase.

### 3.5 Retry policies

Two independent retry mechanisms, used in different layers:

1. **Polly**, for the three outbound typed `HttpClient`s (4jawaly WhatsApp, Paymob, Fawry) registered in `GMS.Infrastructure/InfrastructureServiceExtensions.cs:24-32,83-107`:
   ```csharp
   private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
       HttpPolicyExtensions.HandleTransientHttpError()
           .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), ...);
   ```
   3 retries, exponential backoff (2s/4s/8s), applied via `.AddPolicyHandler(GetRetryPolicy())` on each `AddHttpClient<...>(...)` registration. The `onRetry` callback is a no-op (comment: *"Logged per-service, no logger access here"* — i.e., retry attempts are not actually logged at this layer, `InfrastructureServiceExtensions.cs:29-32`). Note: `GMS.Platform/PlatformServiceExtensions.cs:41-52` registers its own Paymob/Fawry typed clients for platform merchant billing **without** `.AddPolicyHandler(...)` — i.e., the Platform-side payment HTTP clients have **no Polly retry policy** attached, an inconsistency versus the tenant-side clients.
2. **Hangfire's `[AutomaticRetry]`** attribute, applied per-job-method, with job-specific attempt counts/delays: most jobs use `[AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]` (1min/5min/15min backoff) — seen on `BirthdayGreetingsJob`, `DailyDigestJob`, `MembershipExpiryNotificationsJob`, `MembershipStatusExpiryJob`, `TrialExpirySetterJob`, `TrialFollowUpJob`, `AnalyticsAggregationJob`, `ZReportGenerationJob`, `GuestPassExpiryJob`, `InventoryLowStockJob`, `ProcessReferralRewardHoldsJob`, `SessionGenerationJob`, `CreateInvoiceForSaleJob`, `ExecuteImportJob`, `ValidateImportJob`; `ClassRemindersJob` uses `Attempts = 2` (no explicit delays); `InvitationQuotaResetJob` uses `Attempts = 1` (no retry, consistent with it being a no-op log); `StaffNotificationReminderJob` uses `Attempts = 2, DelaysInSeconds = new[] { 60, 300 }` (lighter, since it runs hourly). This is a consistent, deliberate per-job policy rather than a single global default.

---

## 4. Technical debt observations relevant to this section

### 4.1 Large/monolithic classes (>~500 lines)

**GMS.Api/Controllers** — none exceed 500 lines; largest are `TenantSettingsController.cs` (386), `InventoryPurchasingControllers.cs` (361, holds 2 controllers in one file), `PlatformTenantsController.cs` (362), `AdminController.cs` (358).

**GMS.Application/Services** — 11 files exceed 500 lines (full list and per-file assessment in §2.1): `ReportsService.cs` (1255), `SaleService.cs` (1009), `AuthService.cs` (896), `CallSheetService.cs` (886), `ImportService.cs` (861), `MembershipService.cs` (799), `AdminService.cs` (787), `CheckinService.cs` (762), `MemberStoreService.cs` (748), `RefundService.cs` (720), `StockLedgerService.cs` (689), plus several borderline 500-660 line files (`SessionBookingService.cs` 663, `PurchaseOrderService.cs` 645, `TenantSettingsService.cs` 643, `EmployeeService.cs` 635, `StockTransferService.cs` 607, `DashboardService.cs` 604).

**GMS.Core/Entities** — none exceed ~115 lines; entities are thin POCOs, not a debt concern.

**GMS.Core** overall — three enum files only (§2.4); the near-total absence of enums for status-like domain concepts, spread across dozens of call sites (§2.3, §2.4), is itself a structural debt item distinct from raw file size.

### 4.2 Tight coupling observed

- **Direct `DbContext` injection into jobs bypassing any repository/service abstraction**: nearly every job (`BirthdayGreetingsJob`, `DailyDigestJob`, `MembershipExpiryNotificationsJob`, `MembershipStatusExpiryJob`, `TrialExpirySetterJob`, `AnalyticsAggregationJob`, `ZReportGenerationJob`, `SessionGenerationJob`, `StaffNotificationReminderJob`) resolves `GymFlowProDbContext` directly via `IServiceScopeFactory.CreateScope()` and writes raw LINQ queries (including inline `IgnoreQueryFilters()` tenant-filter bypasses) rather than going through the Application-layer services those same features expose to controllers. This means membership-status logic, for instance, is expressed independently in the job (`MembershipStatusExpiryJob.cs`, via `MembershipOperational.TryMarkExpired`) and in multiple `Services/*.cs` classes (§2.3) rather than in one place jobs and controllers both call through.
- **`TenantMiddleware` reaching directly into `GymFlowProDbContext`** (`TenantMiddleware.cs:48,84-87`) rather than through a tenant-repository abstraction — acceptable for a piece of infra-level middleware, but it does mean tenant-resolution logic (including the `IgnoreQueryFilters().FirstOrDefaultAsync(t => t.GymCode == gymCode && !t.IsDeleted)` query) exists only here, duplicated conceptually wherever else tenant lookup-by-code might be needed — **UNKNOWN — REQUIRES VERIFICATION** whether such duplication actually exists elsewhere; not traced.
- **Jobs cross-referencing `IFeatureAccessService`/`ISubscriptionAccessService` from `GMS.Platform`** directly inside per-tenant loops (`TrialExpirySetterJob.cs:32,45`, `TrialFollowUpJob.cs`, `InventoryLowStockJob.cs:35,54`, `TenantMiddleware.cs:50,116`) — this is the main concrete coupling point between the tenant-operational codebase and the SaaS control plane flagged in §0; any "Local Lifetime Edition" that removes `GMS.Platform` wholesale would need to stub/replace `IFeatureAccessService` and `ISubscriptionAccessService` (always-enabled, never-suspended) rather than simply deleting the project, since tenant-side code paths call them directly and unconditionally.
- **No Unit-of-Work abstraction** — 18 different service files independently open/manage `BeginTransactionAsync`/`IDbContextTransaction` (§2.8) rather than sharing one transactional-boundary helper; this is duplication of infrastructure-level concern, not just business logic.
- **Hardcoded Windows timezone ID string repeated in 10+ places** (§2.3) rather than a single named constant — small thing individually, but it means a hypothetical timezone/locale change (or Linux port) requires editing every job file.

### 4.3 Hidden dependencies

- **`HangfireDashboardAuthFilter`** reaches into `HttpContext.RequestServices` directly inside `Authorize(DashboardContext context)` (`HangfireDashboardAuthFilter.cs:16-17`) rather than receiving its dependencies (`IWebHostEnvironment`, auth) via constructor injection — required by Hangfire's `IDashboardAuthorizationFilter` contract (no DI-friendly alternative in that library), so this is a library-imposed pattern rather than an avoidable choice, but it is still a service-locator-style hidden dependency by shape.
- **Static `TimeZoneInfo` fields computed at class-load time** (`private static readonly TimeZoneInfo CairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById(...)`, repeated per job class, §2.3/§4.2) are static ambient state rather than injected configuration — functionally harmless (timezone doesn't change at runtime) but is static state nonetheless, and means a test can't easily override the "current timezone" per job without an OS-level TZ override.
- **`ImpersonationPrincipal` static helper class** (`RejectImpersonationAttribute.cs`, bottom of file) reads claims directly off `ClaimsPrincipal` via static methods rather than an injected claims-resolution service — reasonable for a pure claim-parsing helper, low risk, noted for completeness.
- No evidence of services reaching into `HttpContext.Current`-style ambient state outside the two cases above (the codebase consistently uses constructor-injected `ITenantContext`/`IHttpContextAccessor` elsewhere, e.g. `TenantMiddleware`, `builder.Services.AddHttpContextAccessor()` at `Program.cs:33`).

---

*End of report. Compiled by static/read-only review of the repository at commit state as of 2026-09-08 (branch `rebrand/hymotion`, per repo git status at audit time — note the audited backend projects (`GMS.Api`, `GMS.Application`, `GMS.Core`, `GMS.Infrastructure`, `GMS.Platform`) show no uncommitted changes; the pending changes on this branch are frontend-only).*
