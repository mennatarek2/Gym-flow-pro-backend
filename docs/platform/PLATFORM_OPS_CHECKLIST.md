# Platform Ops Checklist (CP0)

Control-plane foundation for GymFlow managing gym-owner customers. Separate from tenant product auth.

## Prerequisites

- Same SQL Server database as tenant plane (`DefaultConnection`)
- Schema `platform` created via EF migrations (`__PlatformMigrationsHistory` in schema `platform`)
- Authenticator app for MFA (mandatory)

## First admin seed

Configure in `GMS.Api/appsettings.json` (or User Secrets / env vars):

```json
"PlatformSeed": {
  "Email": "platform.admin@gymflow.local",
  "Password": "ChangeMe-Platform-Admin-1!",
  "FullName": "Platform Admin"
}
```

On API startup, `PlatformDataSeeder`:

1. Runs `PlatformDbContext` migrations
2. Creates `platform_admin` if email does not exist
3. Leaves `MfaEnabled = false` so **first login forces MFA setup** (no access token until setup completes)

**After first successful MFA enrollment:** change the seed password and remove plaintext credentials from committed config (use User Secrets / Key Vault).

## Login flow

1. `POST /platform-api/auth/login` `{ "email", "password" }`
   - No MFA yet → **403** `MFA_SETUP_REQUIRED` + `setupToken` + `otpAuthUri` + `mfaManualKey`
   - MFA enabled without code → **401** `MFA_REQUIRED`
   - MFA enabled with code → **200** + `accessToken` (aud=`gymflow-platform`, 10 min)
2. `POST /platform-api/auth/mfa/setup` `{ "setupToken", "mfaCode" }` → enables MFA + returns access token

## Isolation rules (non-negotiable)

| Token | `/api/*` (tenant) | `/platform-api/*` |
|---|---|---|
| Tenant JWT (`aud: GymFlowPro.Clients`) | OK (policies) | **401** |
| Platform JWT (`aud: gymflow-platform`) | **401** (wrong audience) | OK |

- Platform controllers use `AuthenticationSchemes = PlatformBearer` only
- `TenantMiddleware` skips `/platform-api/`
- `PlatformDbContext` has **no** tenant global query filters

## Subscriptions (CP1)

- Tables: `platform.subscriptions`, `platform.subscription_changes`
- Partial unique index `UX_subscriptions_tenant_live`: one live row per tenant (`trialing|active|past_due`)
- FK: `platform.subscriptions.TenantId` → `dbo.tenants.Id`
- All writes go through `ISubscriptionWriteRepository.SaveWithChangeAsync` (subscription + change in one transaction) — no silent subscription updates
- Hot path: `ISubscriptionService.GetStatusAsync` / `GET /platform-api/tenants/{id}/subscription` — Redis key `platform:sub:status:{tenantId:N}` (invalidate on write)
- Mutations (`PlatformAdminOnly`): `POST .../change-tier`, `POST .../cancel`
- Upgrade → immediate + CP2 stub proration invoice; downgrade → `subscription_changes` scheduled at `current_period_end` (renewal job applies later)
- Onboarding: **Production** — `POST /platform-api/tenants/provision` (Platform Ops+) via `ITenantProvisioningService` (Tenant + Owner + defaults, then `StartTrialAsync`; response includes `trialStarted` / `trialError`). **Dev only** — `DataSeeder` + `StartTrial` in `Program.cs` when DB empty. Do not use DataSeeder for production gyms.
- **Orphan repair (no silent list backfill):** if `dbo.tenants` exists but `platform.subscriptions` has no live row (`trialing|active|past_due`), Ops+ calls:
  - `POST /platform-api/tenants/{tenantId}/start-trial` body optional `{ "tier": "growth" }` (default growth)
  - **200** → `SubscriptionStatusDto`; **404** `TENANT_NOT_FOUND`; **409** `LIVE_SUBSCRIPTION_EXISTS`; **400** invalid tier / other
  - Console `/tenants` shows a non-blocking callout when every visible row has null status/tier — use this endpoint (or provision a fresh gym), not fake badges
- **Existing DBs** (tenant already seeded before CP1): run `start-trial` once per orphan, or reset LocalDB — never auto-backfill in Production list reads

## Platform Console read APIs (Stage 1 → CP6)

Policy: `PlatformSupportOrAbove` (support | ops | admin). Mutations: `PlatformOpsOrAbove` (see CP6).

| Method | Route | Notes |
|---|---|---|
| `GET` | `/platform-api/tenants?status=&tier=&riskBand=&search=&page=&pageSize=` | Paged join of `dbo.tenants` + display subscription + health + last-login |
| `GET` | `/platform-api/tenants/{id}` | Full console detail (CP6) |
| `GET` | `/platform-api/tenants/{id}/subscription/changes` | Newest-first `subscription_changes` |
| `GET` | `/platform-api/tenants/{id}/invoices` | `platform_invoices` for the tenant |
| `POST` | `/platform-api/tenants/provision` | **Ops+** — create Tenant + Owner + default plans/settings + StartTrial |
| `POST` | `/platform-api/tenants/{id}/start-trial` | **Ops+** — orphan repair: StartTrial when no live subscription |

Response shapes: `PlatformPagedResult<T>` / `PlatformTenantDetailDto` / `SubscriptionChangeDto` / `PlatformInvoiceDto` (camelCase JSON).
## Audit

Every later CP write must call `IPlatformAuditService.LogAsync(...)`. Missing audit = code-review blocker.
Every subscription mutation must also write `subscription_changes` via the write repository — missing change row = code-review blocker.

## Billing (CP2)

- Tables: `platform.platform_invoices`, `platform.platform_invoice_sequences`
- Invoice number format: `GFP-YYYY-000001` (global by year, not per tenant)
- Gap-free allocation reuses the tenant WS3 `UPDATE ... WITH (UPDLOCK) ... OUTPUT` sequence pattern
- Idempotency key for renewals: unique `(SubscriptionId, PeriodStart)` on `platform.platform_invoices`
- Shared renderer/storage path: same `IInvoicePdfRenderer` + `IFileStorageService`; renderer labels were generalized to biller/customer so platform invoices do not fork a second PDF stack
- Renewal job: `platform-subscription-renewals` at `02:00` Cairo via `PlatformRenewalJobScheduler`
- CP3: `IPlatformBillingPaymentService` / `PlatformBillingPaymentService` — card auto-charge only with saved token **and** `AutoRenewOptIn`; else Fawry ref + Instapay WhatsApp; webhooks at `POST /platform-api/webhooks/paymob|fawry`
- Config: `PlatformBilling:*`, `PlatformPaymob:*`, `PlatformFawry:*`

## Usage / caps / features (CP4)

- Tables: `platform.usage_counters`, `platform.feature_overrides`, `platform.tier_feature_map`
- Single feature gate: `IFeatureAccessService` (tier map → overrides → Phase A `Tenant.Settings.feature_flags` deny overlay). `[FeatureFlag]` + Import/Trial jobs all call it — no parallel flag system.
- Caps: `ITierEnforcementService.CheckCapAsync`
  - `staff_seats` — hard block (`PLAN_LIMIT_EXCEEDED`, HTTP 402) on staff create / reactivation
  - `active_members` — soft warning only (`X-Plan-Soft-Cap` / `Result.Message`); never blocks create (incl. trial confirm)
  - `whatsapp_messages` — soft overage; counted from successful `dbo.notifications` WhatsApp sends (Cairo month); billed at rollup
  - `branches` — **seeded + rolled up only**; no write-time enforcement in CP4
- Nightly job: `platform-usage-rollup` at `01:30` Cairo (`PlatformUsageJobScheduler` / `IRollUpTenantUsageJob`)
- Renewal invoices store `LinesSnapshot` JSON; WhatsApp overage from prior period’s `usage_counters.overage_billed_egp` is appended as a line
- Config: `PlatformBilling:WhatsAppOverageEgpPerMessage` (default `0.35`)
- Seeded starter defaults: members 200 / seats 3 / branches 1 / WA 500 (see `TierFeatureMapSeed`)

> TODO(CP-branches): when Branch CRUD lands, call `ITierEnforcementService.CheckCapAsync(tenantId, "branches")` as a hard block on create (same pattern as staff seats). Cap already exists in `tier_feature_map` / `usage_counters`.

## Dunning / automation (CP5)

- Table: `platform.automation_enrollments` — generic sequence engine (`sequence_key`, `subject_type` = `member|platform_invoice`, `subject_id`, `step`, `next_run_at`, `halted_reason`). Unique active row per subject.
- Runner: Hangfire `platform-automation-enrollments` every minute (`IProcessAutomationEnrollmentsJob`)
- Platform sequence: `platform_invoice_dunning` — T+0 due WA → T+2 reminder → T+5 `past_due` + escalate → grace (`PlatformBilling:DunningGraceDaysAfterPastDue`, default 5) → `suspended` (+ `SuspendedAtUtc`)
- Enroll on renewal invoice create; **halt on CP3 payment webhook** (`HaltAsync` — event-driven, not waiting for the runner)
- Suspension gates (must stay separate):
  - `AuthService` login/refresh → `402 SUBSCRIPTION_SUSPENDED` for Owner/Manager/Trainer/Receptionist
  - `TenantMiddleware` → allows only `/api/attendance/qr-checkin|manual-checkin|search` while within `PlatformBilling:SuspensionCheckinBufferHours` (default 72); after buffer, all tenant APIs blocked
- Config: `PlatformBilling:DunningGraceDaysAfterPastDue`, `PlatformBilling:SuspensionCheckinBufferHours`

## Platform Console API (CP6)

Backend for the internal Platform Console (thin React consumer is separate). Policy baseline: `PlatformSupportOrAbove`; mutations use `PlatformOpsOrAbove` (coupon / trial / suspend / feature overrides). Impersonation stays `PlatformSupportOrAbove`.

| Method | Route | Notes |
|---|---|---|
| `GET` | `/platform-api/tenants?status=&tier=&riskBand=&search=&page=&pageSize=` | Joins subscriptions + `tenant_health_scores` (`healthy\|watch\|at_risk\|critical`) + last-login proxy (`MAX(AspNetUsers.UpdatedAtUtc)`) |
| `GET` | `/platform-api/tenants/{id}` | Detail: subscription, changes, invoices, current-period usage, health, active feature_overrides, price_overrides, recent `platform_audit_log` |
| `POST` | `.../coupon` | Time-boxed `platform.price_overrides` (percent\|fixed) — consumed on next renewal invoice; expired rows ignored (no cleanup job) |
| `POST` | `.../extend-trial` | Extends `TrialEndsAtUtc` + `CurrentPeriodEnd`; change type `trial_extend` |
| `POST` | `.../force-suspend` / `.../force-reactivate` | Via `SaveWithChangeAsync` + audit |
| `GET/POST/DELETE` | `.../feature-overrides` | CRUD; invalidates feature access cache |
| `POST` | `.../impersonate` | 30-min tenant JWT with `impersonated_by_platform_user_id` + `token_use=tenant_impersonation`; **no refresh token** |

Impersonation rules:
- Lifetime clamped to ≤ 30 minutes; cannot be renewed/refreshed
- Tenant JWT validation surfaces the claim on `HttpContext.Items` for the UI banner
- Every tenant `audit_events` row under impersonation sets `ImpersonatedByPlatformUserId`
- Exclusion list (real owner identity required — `[RejectImpersonation]` → 403 `IMPERSONATION_FORBIDDEN`):
  - `POST /api/admin/staff/{id}/reset-password`
  - `DELETE /api/admin/staff/{id}`

Every CP6 write calls `IPlatformAuditService.LogAsync` (actor, before/after, reason).

Migrations: `AddPlatformConsoleCp6` (platform) + `AddAuditImpersonationCp6` (tenant `audit_events`).

Automated: `GMS.Tests/Platform/Cp6PlatformConsoleTests.cs`

## Tenant Health Score (CP7)

Rules-based churn early-warning (**no ML** — Phase 6 deferral). Nightly Hangfire job after usage rollup/renewals.

- Table: `platform.tenant_health_scores` — `score`, `risk_band` (`healthy|watch|at_risk|critical`), `contributing_factors` JSON, `computed_at`, optional `AssignedPlatformUserId`
- Outcome log: `platform.risk_queue_outcomes` (call-sheet shape: actor + outcome + note)
- Job: `platform-tenant-health-scores` at **03:00 Cairo** (`IComputeTenantHealthScoresJob`) — scores every `active`/`past_due` tenant
- Six signals (missing → lower confidence, never crash): login frequency, feature breadth, payment health, member-base trend, support tickets (**stubbed unavailable**), usage-vs-cap
- Weights/bands live in config `PlatformHealth:Weights` / `PlatformHealth:Bands` (logged each run — not magic numbers)

| Method | Route | Notes |
|---|---|---|
| `GET` | `/platform-api/risk-queue?band=at_risk,critical` | Default bands at_risk+critical; sorted by score ascending |
| `POST` | `/platform-api/risk-queue/{tenantId}/assign` | Ops+; body `{ assignedPlatformUserId }` (null clears) |
| `POST` | `/platform-api/risk-queue/{tenantId}/outcome` | `{ outcome: contacted\|retained\|churned\|no_answer\|watching, note? }` |

`contributing_factors` includes per-signal score/weight/summary/detail plus human `summary` for the tenant detail pane.

Automated: `GMS.Tests/Platform/Cp7TenantHealthScoreTests.cs`

## Business reporting (CP8)

Read-side SaaS metrics for Platform Console / weekly ops. Redis snapshot cache (~10 min), fail-open.

| Method | Route | Notes |
|---|---|---|
| `GET` | `/platform-api/metrics/mrr?asOf=` | MRR/ARR; annual `PriceEgp ÷ 12`; Cairo calendar day |
| `GET` | `/platform-api/metrics/movement?from=&to=` | new / expansion / contraction / churned; `Reconciles` flag |
| `GET` | `/platform-api/metrics/churn?from=&to=` | gross churn rate + signup-month cohorts |
| `GET` | `/platform-api/metrics/conversion?from=&to=` | trial_start → paid |
| `GET` | `/platform-api/metrics/tier-distribution?asOf=` | paying count + MRR by `plan_tier` |

Invariant: `starting + new + expansion − contraction − churned = ending` (within rounding). Paying = active/past_due (and historically still-alive cancelled/suspended after as-of). Trialing never contributes MRR.

Automated: `GMS.Tests/Platform/Cp8PlatformMetricsTests.cs`

Renewal behavior at `current_period_end = today`:

1. `trialing` with no payment method on file → cancel
2. `active` with `cancel_at_period_end = 1` → cancel, no next renewal invoice
3. Scheduled downgrade effective today → apply before next invoice
4. Otherwise issue next invoice, advance period, attempt payment stub, mark `past_due` when still unpaid at due date

## EF commands (platform only)

```bash
dotnet ef migrations add <Name> \
  --context PlatformDbContext \
  --project GMS.Platform \
  --startup-project GMS.Api \
  --output-dir Persistence/Migrations

dotnet ef database update \
  --context PlatformDbContext \
  --project GMS.Platform \
  --startup-project GMS.Api
```

Do **not** use the tenant `GymFlowProDbContext` for platform tables.

## Smoke test

```bash
# Must be 401 with a tenant staff token
curl -i -H "Authorization: Bearer <TENANT_JWT>" https://localhost:<port>/platform-api/ping

# Must be 200 with a platform token
curl -i -H "Authorization: Bearer <PLATFORM_JWT>" https://localhost:<port>/platform-api/ping
```

Automated: `GMS.Tests/Platform/PlatformIsolationTests.cs`
