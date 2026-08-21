# GymFlowPro — Launch Checklist

Pre-launch verification checklist covering schema state, tenant isolation, background jobs,
external messaging approvals, feature-flag defaults, and per-module rollback procedures.

## 1. Migrations

Apply in order (`dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api`).
17 migrations currently exist, from `GMS.Infrastructure/Persistence/Migrations/`:

| # | Migration | Adds |
|---|-----------|------|
| 1 | `20260505230815_InitialCreate` | Core schema: tenants, gym members, plans, memberships |
| 2 | `20260506000801_AddAuthEntities` | AppUsers, RefreshTokens, Identity |
| 3 | `20260508141934_AddAnalyticsSnapshotAndHangfire` | AnalyticsSnapshot, Hangfire storage tables |
| 4 | `20260508155311_Phase6_PaymentTransactions` | PaymentTransactions |
| 5 | `20260510_AddAnalyticsSnapshots` | GymAnalyticsSnapshots |
| 6 | `20260510003143_AddNotificationsTable` | Notifications |
| 7 | `20260710235617_AddPermissionsOverrideAndReceptionistRole` | Permission overrides, Receptionist role |
| 8 | `20260711190143_AddAuditEvents` | AuditEvents |
| 9 | `20260711194245_AddCommercialDataModel` | PromoCodes, Sales, SaleLines |
| 10 | `20260711210453_AddInvoiceEngine` | Invoices, InvoiceSequences |
| 11 | `20260711215441_AddNotificationAppUserRecipient` | Notification → AppUser recipient FK |
| 12 | `20260711225341_AddShiftCashDrawer` | Shifts, CashMovements |
| 13 | `20260712005735_AddZReports` | ZReports |
| 14 | `20260712015544_AddTrialAndDayPassSupport` | Trial/day-pass plan support |
| 15 | `20260712142911_AddRefundsAndMemberCredits` | Refunds, MemberCredits |
| 16 | `20260712144900_WidenSaleStatusColumn` | Widens `sales.Status` column |
| 17 | `20260712153723_AddCallOutcomes` | CallOutcomes |
| 18 | `20260712165924_AddImportBatches` | ImportBatches, ImportRows |

**Verify before launch:**
- [ ] `dotnet ef migrations list --project GMS.Infrastructure --startup-project GMS.Api` shows no pending migrations against the target environment.
- [ ] `dotnet ef migrations script --project GMS.Infrastructure --startup-project GMS.Api --idempotent` reviewed for any destructive column drops/renames before running against production data.

## 2. Tenant isolation verification

**Important:** this app does **not** use SQL Server Row-Level Security (RLS) policies. Multi-tenancy
is enforced entirely in the application layer via EF Core global query filters
(`GymFlowProDbContext.ApplyGlobalQueryFilters`, combined with a per-entity `IsDeleted` filter). There
is no database-level backstop — a raw SQL query, a new `DbSet` missing its `HasQueryFilter` call, or
a call built on a `DbContext` with no `ITenantContext` will NOT be blocked at the database. Treat the
queries below as the closest available substitute for an RLS audit, and treat "add a query filter"
as a mandatory step of onboarding any new tenant-scoped entity.

Tenant-scoped tables added in the last three phases (verify each has a working filter):

```sql
-- refunds — should return 0 rows for a tenant you are not scoped to
SELECT COUNT(*) FROM refunds WHERE TenantId = '<other-tenant-guid>';

-- member_credits
SELECT COUNT(*) FROM member_credits WHERE TenantId = '<other-tenant-guid>';

-- call_outcomes
SELECT COUNT(*) FROM call_outcomes WHERE TenantId = '<other-tenant-guid>';

-- import_batches / import_rows
SELECT COUNT(*) FROM import_batches WHERE TenantId = '<other-tenant-guid>';
SELECT COUNT(*) FROM import_rows ir
  JOIN import_batches ib ON ir.ImportBatchId = ib.Id
  WHERE ib.TenantId = '<other-tenant-guid>';
```

Run each of the above via the **application's own repository/service layer** (not raw `sqlcmd`) while
authenticated as a different tenant, and confirm the API returns zero cross-tenant rows. Raw SQL
against the database will always return rows for every tenant — that's expected and not a bug; the
isolation guarantee only exists above the DB.

- [ ] `ReconciliationInvariantTests.BusyDay_TwoTenants_AllMoneyPathInvariantsHold` passing (covers cross-tenant leakage for sales/payments/members).
- [ ] Run `GMS.Tests/ReconCheck` against the target environment's connection string for a recent business day before go-live (see §6).

## 3. Hangfire recurring jobs

All 10 jobs run on the Egypt Standard Time (Africa/Cairo) zone. 9 are registered by
`GMS.Infrastructure/Jobs/JobScheduler.cs`; job #7 (Z-Report) is registered separately by
`GMS.Application/Jobs/ZReportJobScheduler.cs`.

| # | Job ID | Class | Cron | Schedule |
|---|--------|-------|------|----------|
| 1 | `membership-expiry-notifications` | `MembershipExpiryNotificationsJob` | `0 7 * * *` | 7:00 AM daily |
| 2 | `birthday-greetings` | `BirthdayGreetingsJob` | `0 6 * * *` | 6:00 AM daily |
| 3 | `analytics-aggregation` | `AnalyticsAggregationJob` | `0 0 * * *` | Midnight daily |
| 4 | `class-reminders` | `ClassRemindersJob` | `*/30 * * * *` | Every 30 min |
| 5 | `invitation-quota-reset` | `InvitationQuotaResetJob` | `0 0 1 * *` | 1st of month, midnight |
| 6 | `trainer-commission-report` | `TrainerCommissionReportJob` | `0 0 1 * *` | 1st of month, midnight |
| 7 | `z-report-generation` | `ZReportGenerationJob` | `59 23 * * *` | 11:59 PM daily |
| 8 | `trial-followup` | `TrialFollowUpJob` | `0 9 * * *` | 9:00 AM daily |
| 9 | `trial-expiry-setter` | `TrialExpirySetterJob` | `0 0 * * *` | Midnight daily |
| 10 | `daily-digest` | `DailyDigestJob` | `0 9 * * *` | 9:00 AM daily |

- [ ] Confirm the Hangfire dashboard (or `hangfire.RecurringJob` table) shows all 10 job IDs registered after first deploy.
- [ ] Confirm a live Hangfire worker/server process is running in the target environment — recurring jobs only fire if a worker dequeues them (this bit the reconciliation test suite in dev: `Hangfire.InMemory` storage stops enqueue calls from throwing, but nothing dequeues without a real worker).
- [ ] `trials`, `imports` job-level feature-flag no-op guards verified (see §5) — a disabled feature's job should log "disabled — no-op" and exit cleanly, not throw.

## 4. WhatsApp templates (4jawaly.com)

Two message paths exist:
- **Free-form session messages** (`SendExpiryReminderAsync`, `SendBirthdayGreetingAsync`, `SendClassReminderAsync`, `SendGuestInvitationAsync`, `SendRenewalConfirmationAsync`, `SendDocumentAsync`) — posted to 4jawaly's plain `api/v1/messages/whatsapp[/document]` endpoints. These do **not** need template pre-approval, but are subject to WhatsApp's 24-hour customer-service-window policy outside of which Meta will reject them — confirm 4jawaly's account is provisioned for whichever messaging tier your send patterns need.
- **Named templates** (`SendTemplateAsync`) — posted to `api/v1/messages/whatsapp/template`, and **do** require pre-approval as WhatsApp Business templates before launch:

| Template name | Used by | Trigger |
|---|---|---|
| `refund_confirmed` | `RefundService.ApproveAsync` | Refund approved/executed |
| `payment_reminder` | `DebtorsService.SendReminderAsync` | Front-desk debtor reminder |
| `shift_open_warning` | `ZReportGenerationJob` | Shift left open past Z-report time |
| `trial_last_day` | `TrialFollowUpJob` | Trial expires today |
| `trial_followup_offer` | `TrialFollowUpJob` | Post-trial conversion offer |
| `daily_digest` | `DailyDigestJob` | Daily front-desk digest |

- [ ] All 6 templates submitted and approved in the 4jawaly.com dashboard before launch.
- [ ] `FourJawaly:ApiKey` configured in the target environment (User Secrets in dev, Key Vault in prod) — every `SendTemplateAsync`/`SendMessageAsync` call silently no-ops with a warning log if this is missing, so a misconfiguration will NOT surface as an error, only as absent messages.

## 5. Feature flags

Per-tenant feature flags live in `Tenant.Settings`'s nested `"feature_flags"` JSON key
(`GMS.Core/Utilities/FeatureFlagReader.cs`), one bool per module:

| Flag key | Default | Guards |
|---|---|---|
| `sales` | `true` | `SalesController` (`[FeatureFlag("sales")]`) |
| `shifts` | `true` | `ShiftsController` |
| `trials` | `true` | `TrialController` + `TrialFollowUpJob`/`TrialExpirySetterJob` no-op guards |
| `refunds` | `true` | `RefundsController` |
| `debtors` | `true` | `DebtorsController` |
| `imports` | `true` | `ImportsController` + `ImportService.ValidateAsync`/`ExecuteAsync` no-op guards |

A missing/malformed `feature_flags` key, or a missing tenant `Settings` value entirely, defaults to
**enabled** (`true`) for every flag — a tenant is never accidentally locked out by absence of config.

- [ ] Confirm all tenants launch with the above defaults (no flag explicitly set to `false`) unless a specific tenant has requested a module disabled.
- [ ] Disabling a controller-level flag returns `404` with `ProblemDetails.Title = "FEATURE_DISABLED"` — confirm the frontend handles this distinctly from a generic 404 (e.g. hides the nav item rather than showing a broken-link error).
- [ ] Disabling a flag does **not** stop that module's Hangfire recurring job from being *scheduled* — only guarded modules (`trials`, `imports`) no-op inside the job. Confirm this is the desired behavior for any newly-flagged module before relying on the flag as a full kill switch.

## 6. Reconciliation smoke check

Run the standalone `ReconCheck` CLI against the target database before and periodically after launch:

```
dotnet run --project GMS.Tests/ReconCheck -- --connectionString="<target connection string>" --date=<yyyy-MM-dd>
```

Checks (per tenant, scoped to the given Cairo business day):
- (a) `Σ payments − Σ non-credit refunds == Σ invoices − Σ credit notes`
- (b) per shift: `Σ cash_movements == ExpectedCash − OpeningFloat`
- (c) per member: `Σ member_credits >= 0` (no negative store-credit balance)
- (d) per sale: `AmountDue == Total − Σ payments received`

Exit code `0` = all invariants held; `1` = a violation was found or usage was invalid — safe to wire
into a daily cron/alert.

- [ ] Run once manually against a copy of production data (or a comparable staging dataset) before go-live.
- [ ] Schedule as a recurring ops check post-launch (daily, previous business day).

## 7. Load test baseline

`GMS.Tests/LoadTests` (NBomber — k6 was not installed in the dev environment this was built in;
swap in a k6 script later if the ops environment standardizes on it) exercises:

```
dotnet run --project GMS.Tests/LoadTests -- \
  --baseUrl=<target base URL> \
  --staffToken="<JWT from POST /api/auth/login>" \
  --memberToken="<JWT from POST /api/auth/member-verify>" \
  --tenantId=<guid> --planId=<guid> --gymCode=<gym code> \
  --connectionString="<target connection string>"
```

- 20 VUs × 2 min against `POST /api/sales` (cash, cycling through existing member IDs) — threshold p95 < 800ms, error rate < 1%.
- 50 VUs × 2 min against `POST /api/attendance/qr-checkin` — threshold p95 < 300ms, error rate < 1%.
- Post-run: queries `Invoices` for duplicate `InvoiceNumber` per tenant (would indicate the invoice-sequence allocator broke under concurrent load).

Bearer tokens must be obtained out-of-band (real login / member OTP against the target environment)
— the script does not perform auth itself, since OTP delivery is environment-specific.

- [ ] Run once against staging under expected peak concurrency before launch.
- [ ] No duplicate invoice numbers found after the run.

## 8. Rollback procedure per module

| Module | To disable | Hangfire jobs to pause | Notes |
|---|---|---|---|
| Sales / POS | Set tenant's `feature_flags.sales = false` | None job-driven | In-flight sales already committed are unaffected; new `POST /api/sales` calls 404 |
| Shifts | Set `feature_flags.shifts = false` | None job-driven | Open shifts remain open; closing must happen before disabling to avoid stuck drawers |
| Trials | Set `feature_flags.trials = false` | Remove/pause `trial-followup`, `trial-expiry-setter` from Hangfire dashboard if a full stop is needed (flag alone only no-ops these jobs per-tenant, not globally) | |
| Refunds | Set `feature_flags.refunds = false` | None job-driven | Already-requested refunds awaiting approval are unaffected by the flag; only new `POST` requests are blocked |
| Debtors | Set `feature_flags.debtors = false` | `daily-digest` still runs globally (not flag-guarded) — front-desk digest content for a disabled tenant should be reviewed | |
| Imports | Set `feature_flags.imports = false` | `ImportService.ValidateAsync`/`ExecuteAsync` no-op per-tenant automatically | In-flight `ImportBatch` rows are left in whatever status they were at disable time — resuming requires re-enabling the flag |

**Full module rollback (schema-level):** if a module needs to be fully reverted (not just flagged
off), roll back to the migration immediately before that module's `Add*` migration (§1) via
`dotnet ef database update <PreviousMigrationName>`. This is destructive to any data created by the
module — confirm a recent backup exists before running it against production.

- [ ] Confirm a database backup/snapshot exists immediately before launch.
- [ ] Confirm the on-call rotation knows where this checklist lives and how to reach the feature-flag JSON (`Tenant.Settings`) without a deploy.
