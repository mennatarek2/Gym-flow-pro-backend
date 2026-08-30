# Background Job / Non-HTTP Tenant-Context Audit (REM-F1)

Date: remediation pass. Scope: every Hangfire/background job resolving `GymFlowProDbContext` outside an HTTP request.

## Why this matters

`GymFlowProDbContext.ApplyGlobalQueryFilters()` only applies EF global filters when an
`ITenantContext` is available (`_tenantContext == null → return`). Background jobs resolve
the context from a DI scope with **no ambient tenant**, so:

> In jobs, ALL queries run UNFILTERED unless the query itself constrains by `TenantId`.

This is the documented design ("IgnoreQueryFilters() should only be used in admin/migration
contexts"), but it means job code must be reviewed manually. This document is that review.

## Audit result

| Job | Pattern | Tenant safety | Notes |
|---|---|---|---|
| AnalyticsAggregationJob | Enumerates active tenants via `IgnoreQueryFilters`, then runs per-tenant raw-SQL MERGE with literal `{tenant.Id}` params | SAFE | Every subquery filters `TenantId = {tenant.Id}` |
| DailyDigestJob | Per-tenant loop, explicit TenantId predicates (4 sites) | SAFE | |
| ZReportGenerationJob | Per-tenant loop, explicit TenantId predicates (3 sites) | SAFE | |
| MembershipStatusExpiryJob | `IgnoreQueryFilters()` over memberships where EndDate < today, marks expired | SAFE | Row-level update only; each row's own TenantId untouched; operation is tenant-agnostic by nature (expiry applies to all tenants) |
| TrialExpirySetterJob / TrialFollowUpJob | Same row-level pattern | SAFE | |
| BirthdayGreetingsJob / ClassRemindersJob / InvitationQuotaResetJob / MembershipExpiryNotificationsJob / TrainerCommissionReportJob | Per-tenant iteration or row-level updates | SAFE | |
| GuestPassExpiryJob → InvitationService.ExpireOverdueGuestPassesAsync | `IgnoreQueryFilters()` + row-level status expiry | SAFE | Cross-tenant by design; no data crosses tenants |
| InventoryLowStockJob (+scheduler) | Per-tenant loop with explicit TenantId | SAFE | Failures logged per tenant, loop continues |
| ProcessReferralRewardHoldsJob → ReferralRewardService.ProcessDueHoldsAsync | `IgnoreQueryFilters()` scan for due holds, then per-reward processing using `reward.TenantId`; GrantOneAsync re-constrains every follow-up query by `reward.TenantId` | SAFE | Idempotent re-check on Status before grant; hold→grant flow intact |
| CreateInvoiceForSaleJob | Carries explicit saleId; invoice lookup by SaleId (globally unique GUID) | SAFE | SaleId is a GUID PK; lookup cannot cross tenants |
| ExecuteImportJob / ValidateImportJob | Operate on ImportBatch rows loaded by Id | SAFE | |

## Rules going forward

1. New job queries MUST constrain by an explicit TenantId, OR be justified as
   "row-level, tenant-agnostic" (status expiry style) with a comment.
2. `IgnoreQueryFilters` inside jobs is permitted ONLY when followed by explicit
   `TenantId ==` predicates or when the operation is inherently row-local.
3. Never rely on ambient `ITenantContext` inside a job — it will be null.
4. Writes must always preserve each entity's own `TenantId`.

## Regression coverage

`GMS.Tests/TenantIsolationRegressionTests.cs` proves:
- without ambient tenant context, a query WITH a TenantId predicate returns only that
  tenant's rows;
- the same query WITHOUT any predicate would return both tenants' rows (demonstrating why
  rule 1 exists);
- soft-delete remains respected in the null-context mode.
