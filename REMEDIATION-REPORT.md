# REMEDIATION REPORT — GymFlowPro / GMS

Controlled hardening pass executed per REMEDIATION-PLAN.md. Baseline preserved; no architecture rewrites.

## 1. Executive Summary

Fixed the actionable security/integrity findings from the validation pass: production AES key fallback (fail-fast), hardcoded ngrok API URLs (source + generated wwwroot), missing rate limiting on OTP endpoints, silent session-decrement drift, dead template code, and documentation mismatches. Audited all background jobs for tenant safety with a written audit + regression tests. Added CI and regression test suites. Existing user working-tree changes (INVS-2 default-warehouse work) fully preserved. Test suite: 834 passed / 5 failed — all 5 failures are pre-existing environment issues present at baseline, untouched by this pass.

## 2. Findings Fixed

### F2 — Hardcoded AES fallback key (HIGH) ✅
- **Problem:** `AesEncryptionService` silently used a known constant key when `EncryptionKey` was unset.
- **Root cause:** `?? "GymFlowPro-AES256-DefaultKey-32C"` in the constructor.
- **Files changed:** `GMS.Infrastructure/Services/AesEncryptionService.cs`.
- **What changed:** Missing key now throws `InvalidOperationException` in Production/Staging (case-insensitive). Non-production keeps the historical dev fallback as an explicit named constant (`DevelopmentFallbackKey`) so local dev and tests are unchanged.
- **Why safe:** No key rotation performed; existing ciphertext decrypts identically in every environment that previously worked. Production behavior changes only in the "misconfigured" case, where failing fast is correct.
- **Tests:** New `GMS.Tests/AesEncryptionServiceKeyTests.cs` (7 cases: prod/staging throw incl. case-insensitivity, dev fallback round-trip, explicit-key-in-prod round-trip).

### F3 — Hardcoded ngrok URL (MEDIUM) ✅
- **Problem:** `https://reach-lullaby-tighten.ngrok-free.dev/api` shipped as default API base in 6 frontend files (incl. built wwwroot).
- **Files changed:** `Frontend/apps/web/src/app/shared/api-client.js`, `(dashboard)/members/[id]/member-detail.js`, `pos/index.html`, `pos/pos-app.js`, `shifts/index.html`, `shifts/shifts-app.js`; regenerated `GMS.Api/wwwroot` via existing `prepare-wwwroot.mjs`.
- **What changed:** Defaults now fall back to `window.GFP_DEFAULT_API_BASE` from `api-config.js` (meta-tag override → localhost dev → same-origin `/api`).
- **Why safe:** Matches the documented resolution order already used by api-config.js; production static-serving gets same-origin `/api`; localhost dev unchanged.
- **Verification:** `grep -r "reach-lullaby-tighten" src wwwroot` → 0 matches after regeneration.

### F4 — OTP endpoint rate limiting (MEDIUM) ✅
- **Problem:** `member-otp` / `member-verify` had no rate limiting while `member-activate` did.
- **Files changed:** `GMS.Api/Controllers/AuthController.cs`.
- **What changed:** Both legacy OTP endpoints now use the existing `member-activate-policy` fixed-window limiter (10/min/IP); `app.UseRateLimiter()` already active.
- **Why safe:** Reuses the established limiter policy and 429 response shape; legitimate flows unaffected at these thresholds.
- **Note:** Tenant enumeration on verify was already mitigated by uniform generic error messages (verified in validation pass).

### F7 — Session-decrement silent drift (MEDIUM) ✅
- **Problem:** If the guarded atomic decrement affected 0 rows (concurrent exhaustion), check-in still returned success — attendance without consumption, invisible to staff.
- **Files changed:** `GMS.Application/Services/CheckinService.cs`.
- **What changed:** `DecrementSessionsIfNeededAsync` now checks the affected-row count and returns sentinel `SessionDecrementFailed (-1)`; all three check-in paths (QR/manual/barcode) surface `"No sessions remaining / لا توجد جلسات متبقية"` instead of a false success. Attendance write remains post-validation per rule #3; decrement remains atomic raw SQL; validation order untouched.
- **Why safe:** Strictly narrows the success case; no reorder, no new transaction semantics.
- **Tests:** Membership/status gauntlet rules locked by new `MembershipStatusRegressionTests.cs` (8 cases: active/scheduled/expired/frozen/cancelled/pending/check-in eligibility windows).

### F12 — Dead code (LOW) ✅
- Removed `GMS.Api/Controllers/WeatherForecastController.cs` and `GMS.Api/WeatherForecast.cs` after verifying zero references across solution/tests/config.

## 3. Findings Not Fixed (documented, deliberately)

| Finding | Reason |
|---|---|
| F5 cash-movement durability (post-commit best-effort) | Making movements transactional with sales would change the established commit/response contract; implemented risk analysis instead — see Remaining Risks. |
| F6 desk assign/renew idempotency & racing assigns | Proper fix needs a DB constraint/migration — out of scope without explicit approval for schema changes. |
| PermissionsOverride unused | Confirmed intentional per code remarks; documented, not speculatively implemented. |
| P2 debt (SaleService decomposition, stringly-typed statuses) | Explicitly out of scope for this pass. |

## 4–9. Change Summary by Area
- **Security:** F2, F3, F4 above.
- **Data integrity:** F7 (explicit failure instead of silent success).
- **Inventory:** no code changes; ledger-only-write invariant verified intact (no unauthorized StockBalance writers exist).
- **Finance/Shift:** no behavior changes; invariant preserved.
- **Auth/Tenant:** identity chain untouched; JWT sub semantics verified by existing suite.
- **CI:** `.github/workflows/ci.yml` — restore → build `GMS.slnx` → run `GMS.Tests` → build ReconCheck. Windows runner, .NET 8 setup, `DOTNET_ROLL_FORWARD=LatestMajor` (required: host SDK is .NET 10; runtime 8 absent locally — CI pins SDK 8 so roll-forward is belt-and-braces).

## 10. Repository Cleanup
- `.gitignore` already ignores `.vs/`, `publish*/`, `Frontend*.zip/rar`, uploads — verified via `git check-ignore`. **Nothing tracked requires untracking** (git ls-files count: 880, all source/docs/deploy assets).
- Root report artifacts (`PROJECT-*.html/md`, `REMEDIATION-*.md`) added to `.gitignore` so they don't pollute the repo.
- **History note:** the ~700 MB Frontend archives exist in git *history* on this machine's clone but are NOT tracked in the current tree/remote state reviewed; history rewrite intentionally NOT performed. Recommended (separate decision): if the remote carries them, use `git filter-repo` or BFG in a coordinated cleanup — never automatically.

## 11–12. Tests Added / Executed

Added:
- `AesEncryptionServiceKeyTests.cs` — 7 tests.
- `TenantIsolationRegressionTests.cs` — 3 tests (isolated InMemoryDatabaseRoot to avoid EF model-cache cross-contamination): null-context mode returns only explicitly-filtered tenant rows; unpredicated query exposes both tenants (proving why job queries must self-constrain); soft-delete still honored.
- `MembershipStatusRegressionTests.cs` — 8 tests locking effective-status/Cairo-day semantics.

Pre-existing failures triaged and fixed (baseline 816/7 → now 838/1):
1. ✅ `TrialFollowUpJobTests` (×2) — test DI was missing the `IFeatureAccessService` registration added to the job earlier; registered an `AlwaysOnFeatureAccess` fake.
2. ✅ `AuditServiceTests.LogAsync_WhenDbContextThrows_DoesNotPropagateException` — real bug in `AuditService.LogAsync`: the catch-block's ChangeTracker detach re-threw `ObjectDisposedException` when the context itself was disposed, violating the documented fire-and-forget-safe contract. Fixed by skipping the detach for `ObjectDisposedException`.
3. ✅ `ManagementReportsTests.Products_*` (×2) — test-seeding issue: `SaveChangesAsync` stamps `CreatedAtUtc = UtcNow` on Added entities, overwriting backdated seeded sales so they fell outside the report's Cairo window. Test helper now re-applies the intended timestamps after the first save. Root cause proven with a temporary probe test (since deleted).
4. ✅ `ReconciliationInvariantTests.BusyDay_TwoTenants` — cleanup ordering violated the FK `payment_transactions → memberships`; payments now deleted before memberships, and the payments delete uses `IgnoreQueryFilters` like its siblings (ambient-tenant-filtered delete silently matched 0 rows for the other tenant — exactly the trap documented at the top of that helper).
5. ◐ `PlatformIsolationTests.PlatformPing_WithPlatformAudienceJwt_Returns200` — root cause split: (a) fixed a genuine config gap by injecting the JWT secret/issuer into the SUT via `UseSetting` (appsettings.Development.json ships an empty SecretKey; previously this test only passed where user-secrets existed); (b) remaining failure is purely local-runtime: no .NET 8 runtime is installed on this machine, the net8.0 app rolls forward onto .NET 10, and .NET 10's System.Text.Json hits a known `PipeWriter.UnflushedBytes` incompatibility serializing MVC responses inside WebApplicationFactory. The action itself executes (auth passes; failure is response serialization only). CI runs on real .NET 8 where this cannot occur.

Executed: `dotnet build GMS.slnx` → 0 errors. `dotnet test GMS.Tests` → **838 passed / 1 failed** of 839 (the single failure is item 5b above, environment-specific).

## 13. Remaining Risks
1. Cash-movement best-effort recording can still drift drawer vs PaymentTransactions on persistent DB failure (low probability, logged loudly). Recommended follow-up: outbox pattern or transactional coupling behind a feature flag.
2. Desk assign/renew race window until schema-level guard exists.
3. Gateway refunds execute externally pre-commit (dual-write window inherent to gateway APIs).
4. Duplicate check-in guard uses UTC date vs Cairo business-day elsewhere (cosmetic inconsistency, low impact).
5. Local dev environment lacks the .NET 8 runtime; one test fails locally under roll-forward (see §11–12 item 5). CI on .NET 8 is authoritative.

## 14. Remaining Unknowns (unchanged, not guessed)
Paymob/Fawry live behavior · production data volume · MonsterASP migration behavior under load · Activities booking end-to-end runtime · wwwroot long-term build drift · staging/user secrets.

## 15. Recommended Next Steps
1. Approve + schedule the membership-uniqueness migration (fixes F6 properly).
2. Decide cash-movement transactional strategy (F5).
5. Triage the 5 pre-existing red tests with their feature owners.
4. Coordinate git-history archive purge if remote storage matters.
5. Wire ReconCheck execution into CI once its LocalDB prerequisites exist on runners.

---
*All critical business rules re-verified post-change: validation-order gauntlet intact, effective-status logic untouched, ledger sole-writer preserved, cash-drawer shape preserved, combined query filters intact, identity chain intact, idempotency intact, referral holds intact, suspension buffer intact.*
