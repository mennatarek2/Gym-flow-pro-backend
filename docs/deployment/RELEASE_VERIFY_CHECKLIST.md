# Release verify checklist — DevOps (DO-01 / DO-02)

## DO-01 — CashExpense `SourceType` on non-local envs

**Migrations:**

- `20260831145918_AddProfitabilityLedger` (adds `SourceType`)
- `20260901140704_CashExpenseSourceTypeDriftFix` (drift fix)

**Per environment (staging, prod):**

1. Apply pending EF migrations.
2. Confirm column: `cash_expenses.SourceType` exists (`varchar(40)`).
3. Post a Running Cost (manual) → insert succeeds; `SourceType` = `running_cost` (or catalog default).
4. Confirm payroll disbursement rows (if any) use `SourceType = payroll_payment` and are **excluded** from OpEx / Running Costs.

| Env | Migrated | Insert OK | Verified by | Date |
| --- | -------- | --------- | ----------- | ---- |
| Local | Yes (baseline) | Yes | | |
| Staging | | | | |
| Production | | | | |

## DO-02 — HTTPS API + feature flags

1. API reachable over HTTPS (no staff-web Network error to wrong port/scheme).
2. Local desk: `dotnet run --launch-profile https` → `:5001` (see `docs/getting-started/STAFF_WEB_LOCAL_HTTPS.md`).
3. Staging login succeeds.
4. Feature flags as required for release (`inventory` / store, etc.).
5. Member Classes + Member Orders endpoints reachable for a test member JWT.

| Check | Staging | Prod | Notes |
| ----- | ------- | ---- | ----- |
| HTTPS login | | | |
| Flags | | | |
| `/api/member/classes` | | | |
| `/api/member/orders` | | | |

## Local evidence (2026-09-02)

- `cash_expenses.SourceType` length = 40
- Migrations present: AddProfitabilityLedger, CashExpenseStructuredFields, CashExpenseSourceTypeDriftFix
- Sample rows use `SourceType = running_cost`
- Staff web default API: `https://localhost:5001/api` (`dotnet run --launch-profile https`)
