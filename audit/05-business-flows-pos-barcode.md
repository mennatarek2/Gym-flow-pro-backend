# Business-Critical Flow Audit (POS, Barcode/Attendance, Membership, Refund, Inventory, HR, Expenses)

Scope: read-only code trace of GMS.Api / GMS.Application / GMS.Core / GMS.Infrastructure for the
"Local Lifetime Edition" feasibility study. All claims are cited `file:line`. Where a flow or
detail could not be located, it is marked `UNKNOWN — REQUIRES VERIFICATION` with what was searched.

---

## Flow 1: Barcode Check-in / Attendance

### Entry points (member gym-floor check-in — NOT HR employee attendance, see below)

All four routes live on one controller: `GMS.Api\Controllers\AttendanceController.cs`, routed at
`api/attendance` (`AttendanceController.cs:16`):

| Route | Method | Auth | Purpose |
|---|---|---|---|
| `GET /api/attendance/qr/token` | `GetQrToken` | `HasAnyPermission(MembersView, AttendanceView)` | staff screen mints a fresh signed QR (`AttendanceController.cs:38-52`) |
| `POST /api/attendance/qr-checkin` | `QrCheckin` | `Authorize(Policy="AuthenticatedMember")`, rate-limited `checkin-policy` | member's own phone scans the displayed QR (`AttendanceController.cs:54-76`) |
| `POST /api/attendance/manual-checkin` | `ManualCheckin` | `HasPermission(CheckinManual)` | staff picks a member from search and checks them in (`AttendanceController.cs:78-96`) |
| `POST /api/attendance/barcode-checkin` | `BarcodeCheckin` | `HasPermission(CheckinManual)`, rate-limited `checkin-policy` | desk scans a physical access-card/barcode printed with the member's `MemberNumber` (`AttendanceController.cs:101-120`, doc-comment "Desk access-card check-in — exact MemberNumber (MAC-P0 Phase 2)") |

All four delegate to `ICheckinService` / `GMS.Application\Services\CheckinService.cs`.

### Member identification format

- **QR check-in**: the QR image encodes an **opaque signed token**, not the member's identity at
  all. The *member* is identified from the caller's JWT (`AuthenticatedMember` policy), not from
  the QR. `QrCheckinRequest.GymCode` (wire field, kept for backward compat) actually carries this
  token (`GMS.Application\DTOs\Attendance\QrCheckinRequest.cs:10-16`, `CheckinService.cs:72-99`).
- **Manual check-in**: `ManualCheckinRequest.MemberId` — a `Guid` selected via
  `GET /api/attendance/search-members` (`CheckinService.cs:166-190`, `AttendanceController.cs:122-136`).
- **Barcode check-in**: `BarcodeCheckinRequest.Code` — the member's human-readable `MemberNumber`
  (e.g. `GYM-042`), matched by **exact equality only** — comment explicitly forbids `Contains`
  matching (`GMS.Application\DTOs\Attendance\BarcodeCheckinRequest.cs:8`,
  `CheckinService.cs:276-283`: `_memberRepo.GetByMemberNumberAsync(code, tenantId)`). So the
  "barcode" is not a scanned binary/EAN barcode payload — it's whatever string the desk's barcode
  scanner emits for the member's printed card, expected to equal `MemberNumber` verbatim.

### GymQrTokenService — rotation scheme and external dependencies

`GMS.Application\Services\GymQrTokenService.cs` / `IGymQrTokenService.cs`:

- **Stateless, no DB round-trip.** Token = `Base64Url({gymCode}:{expUnixSeconds}:{nonce})` + `.` +
  `Base64Url(HMAC-SHA256(payload))`, signed with `JwtSettings:SecretKey` — the **same** secret
  already used for access tokens (`GymQrTokenService.cs:16-23,25-39`, interface doc at
  `IGymQrTokenService.cs:8-16`).
- Default lifetime 45s, ±5s clock-skew tolerance (`GymQrTokenService.cs:11-14`).
- Validation (`GymQrTokenService.cs:41-79`) is pure in-memory HMAC verification + expiry check —
  **no network call, no DB call**. The token is a shared display code (same QR scanned by many
  people while valid), not single-use/consumed.
- `CheckinService.ProcessQrCheckinAsync` then resolves the embedded gym code to a `Tenant` row via
  a **local DB** lookup (`CheckinService.cs:84-95`, `GetTenantByGymCodeAsync` at `:688-694`) and
  compares it to the caller's ambient tenant — this is a local-DB check, not external.
- Migration `AddQrAttendanceHardening` (`GMS.Infrastructure\Persistence\Migrations\20260907114724_AddQrAttendanceHardening.cs`)
  added `gym_attendance.AttendanceDateCairo` and a **unique index**
  `IX_gym_attendance_TenantId_MemberId_AttendanceDateCairo_Unique` on
  `(TenantId, MemberId, AttendanceDateCairo)` filtered to `MemberId IS NOT NULL AND SessionId IS
  NULL AND IsDeleted = 0` (lines 34-39) — this is the DB-level backstop behind the duplicate-checkin
  exception (below). Nothing in the QR/attendance hardening depends on external time sync beyond
  the server's own system clock — it uses `DateTime.UtcNow` and the "Egypt Standard Time" IANA/COM
  timezone conversion done locally via `TimeZoneInfo` (`GMS.Core\Utilities\MembershipOperational.cs:11-19`).

**Conclusion: the QR/barcode/manual check-in mechanism itself needs no internet** — token minting
and validation are pure local HMAC computation, member/membership lookups are local SQL Server
queries, and the Cairo-day math uses the local machine's `TimeZoneInfo` database, not an external
time service.

### Duplicate check-in protection

Two layers:
1. **Application-level** query in `ValidateMembershipGauntletAsync` — checks for an existing
   `GymAttendance` row for this member/tenant/Cairo-day with `SessionId == null`
   (`CheckinService.cs:514-527`).
2. **Database-level** unique index (see migration above) enforced by
   `AttendanceRepository.CreateCheckinAsync` (`GMS.Infrastructure\Repositories\AttendanceRepository.cs:30-49`):
   catches `DbUpdateException` whose message contains the index name and rethrows as
   `DuplicateCheckinException` (`GMS.Core\Exceptions\DuplicateCheckinException.cs:9-18`), which all
   three check-in methods in `CheckinService` catch and convert into a friendly `Result` failure
   (`CheckinService.cs:119-126, 213-220, 304-311`). This is the documented "last line of defense
   against a genuine race" (`DuplicateCheckinException.cs:4-8`).

Class/session-booking check-ins (`SessionId != null`, handled by `SessionBookingService`, not
traced in depth here) are explicitly exempt from this uniqueness constraint by design
(`CheckinService.cs:515-517`).

### Membership validation gauntlet (`ValidateMembershipGauntletAsync`, `CheckinService.cs:439-530`)

Executed identically for QR, manual, and barcode check-in. In order:

1. **Member.IsActive** — inactive member account → immediate fail (`:443-444`).
2. **Active membership lookup**, 5-minute `IMemoryCache` cache keyed `membership:{tenantId}:{memberId}`
   (`GetActiveMembershipCachedAsync`, `:536-562`) — query requires `Status IN ('active','frozen')`
   AND `StartDate <= today AND EndDate >= today` (Cairo). If none found → staff notification
   `ExpiredMembershipCheckin` fired (best-effort, `:742-761`) and fail "Your membership has expired"
   (`:447-452`).
3. **Frozen check** — `Status == "frozen"` → explicit "membership is currently frozen" failure
   (`:455-456`).
4. **Not-active status** (e.g. cancelled/expired stored status slipping through) → expired-message
   fail (`:458-462`).
5. **Future-dated (not-yet-started) membership** — `today < StartDate` → "Your membership has not
   started yet" (`:466-467`). Note: because step 2's query already filters `StartDate <= today`,
   this branch is effectively dead code for the cached path — a genuinely future-dated membership
   would simply not be returned by `GetActiveMembershipCachedAsync` and would instead fall into the
   "no active membership" branch at step 2. **Potential inconsistency**: a future-dated membership
   check-in attempt gets the generic "membership has expired" message rather than "has not started
   yet" — a real (minor) UX/observability gap, not a security issue.
6. **Past-end (expired) membership** — `today > EndDate` → expired-membership notification +
   fail; trial plans get a distinct `TRIAL_EXPIRED_JOIN_OFFER` code (`:469-476`).
7. **Time-restricted plans** (e.g. "morning pass") — current Cairo wall-clock time must fall inside
   `Plan.TimeRestrictionStart`/`End` (`:479-493`).
8. **Session-pack plans** — `SessionsRemaining` must be `> 0` (`:496-500`).
9. **Trial visit-limit plans** — counts prior `GymAttendance` rows since `StartDate`; blocks with
   `TRIAL_VISITS_EXHAUSTED` once `TrialVisitLimit` reached (`:502-512`).
10. **Duplicate-check-in-today** (see above) (`:514-527`).

### Attendance write + session decrement (steps 7-8 of the class doc-comment, `CheckinService.cs:17-32`)

- `GymAttendance` row created with `TenantId, MemberId, MembershipId, CheckInAtUtc=UtcNow,
  EntryMethod ("qr"|"manual"|"barcode")`, plus `StaffUserId`/`ManualReason` for
  manual/barcode (`:110-117, 202-211, 293-302`).
- `DecrementSessionsIfNeededAsync` (`:588-635`) only applies to `PlanType == "session_pack"`. On a
  relational DB it does an **atomic guarded UPDATE**:
  `UPDATE memberships SET SessionsRemaining = SessionsRemaining - 1 WHERE Id=@id AND
  SessionsRemaining > 0` (`:600-601`) — this is not wrapped in an explicit
  `BeginTransactionAsync`/`CommitAsync` pair; the attendance INSERT (via
  `AttendanceRepository.CreateCheckinAsync` → `SaveChangesAsync`) and this session-decrement UPDATE
  are **two separate round-trips, not one atomic transaction**.
- **Compensation, not rollback**, is the strategy for the resulting race window: if the guarded
  UPDATE affects 0 rows (session exhausted concurrently between the read at step 8 of the gauntlet
  and the decrement), `CompensateFailedSessionCheckinAsync` (`:641-658`) **soft-deletes** the
  already-inserted `GymAttendance` row (`IsDeleted = true`) rather than using a DB transaction to
  undo the insert. If that compensating soft-delete itself throws, the exception is only logged
  (`:651-657`) — **the attendance row can be left behind, un-compensated, with no session actually
  consumed**, a real failure mode under load (documented in-code as "REM-F7").
- pt_credits (PRIVATE/personal-training) session consumption is **not** part of check-in at all —
  it's a separate explicit staff action, `MembershipService.ConsumePrivateSessionAsync`
  (`GMS.Application\Services\MembershipService.cs:535-629`), using the identical atomic-UPDATE +
  re-read pattern.

### Failure-point summary (Flow 1)

- Attendance-insert and session-decrement are not one DB transaction — see above; mitigated by
  best-effort soft-delete compensation, which itself can fail silently (logged only).
- The future-dated-membership code path (`:466-467`) is unreachable given the caching query's own
  `WHERE StartDate <= today` filter — future-dated members see a generic "expired" message instead
  of "not started yet" (cosmetic, not a security gap).
- `IMemoryCache` 5-minute TTL on membership status means a membership frozen/cancelled by another
  staff action can remain check-in-eligible in the cache for up to 5 minutes on a given app
  instance — `InvalidateMembershipCache` (`:663-666`) only clears it after check-in's own
  session-decrement, not when the membership is frozen/cancelled/renewed elsewhere unless that
  code path also happens to call the same invalidation (not verified beyond `CheckinService`).
- `StaffNotifications` publisher is injected as **nullable** (`IStaffNotificationPublisher? =
  null`, `CheckinService.cs:41,56`) — expired-checkin alerts silently no-op if not configured; not
  a functional break for the check-in itself.

### HR employee attendance — a DISTINCT system (do not conflate with the above)

`GMS.Api\Controllers\HrEmployeeAttendanceController.cs`, routed at `api/hr/employee-attendance`
(`:15`), explicit doc-comment: *"Separate from member `AttendanceController` (gym check-in) — this
tracks working hours, not gym visits"* (`:13-14`). Backed by
`GMS.Application\Services\EmployeeAttendanceService.cs`, a completely different entity
(`EmployeeAttendance`, not `GymAttendance`) with its own unique index
(`IX_employee_attendances...`, checked at `EmployeeAttendanceService.cs:171-175`) on
`(TenantId, EmployeeId, AttendanceDate)`, its own duplicate-checkin exception handling
(`:74-84`), and its own two-step QR flow (`ValidateQrCheckinAsync` preview, then
`QrCheckInAsync` which reuses `CheckInAsync`, `:92-141`) that reuses the **same**
`IGymQrTokenService` for the physical-presence signal (`:18,145-169`) but writes to a
payroll-facing table, not the gym-floor attendance table. See Flow 6 for more.

### Flow 1 internet/external-dependency verdict

**NO** — the entire barcode/manual/QR gym-floor check-in path (token mint, token validate, member
lookup, membership-gauntlet, attendance insert, session decrement, duplicate-guard) is pure local
HMAC computation plus local SQL Server queries. The only externally-facing side effects are
fire-and-forget, non-blocking, and do not gate success: `_notifier.NotifyCheckinAsync(...)` is
invoked un-awaited (`_ = _notifier...`, `CheckinService.cs:148-150, 241-243, 329-331`) for a
real-time dashboard push (SignalR — local network, not internet), and
`_staffNotifications.TryPublishAsync(...)` for expired-membership alerts (`:742-761`) is
best-effort. No payment gateway, SMS, or other internet call sits anywhere in this flow.

---

## Flow 2: POS (Point of Sale)

### Entry points

`GMS.Api\Controllers\SalesController.cs`, routed `api/sales`, gated by `[FeatureFlag("sales")]`
(`:17-20`):
- `POST /api/sales` → `CreateSale` → `ISaleService.CreateSaleAsync` (`:61-84`)
- `POST /api/sales/{id}/payments` → `RecordPayment` → `RecordPaymentAsync` (`:87-102`, for
  collecting the remainder on a `partially_paid` sale)
- `POST /api/sales/validate-promo`, `GET /api/sales/{id}/invoice` — supporting endpoints.

Implementation: `GMS.Application\Services\SaleService.cs`.

### CreateSaleAsync walk-through (`SaleService.cs:66-676`)

1. **Idempotency**: if `IdempotencyKey` supplied, checks `SaleIdempotencyKeys` table first and
   replays the prior response if found — read-only, before opening a transaction (`:70-78`).
2. Resolves staff `AppUser`, normalizes the cart, and **rejects any payment method that is not
   `cash` or `account_credit`** at the desk-create step:
   ```
   if (request.Payments.Any(payment => !IsDeskPaymentMethod(payment.Method) || payment.Amount <= 0m))
       return Fail("PAYMENT_METHOD_NOT_READY", ...)
   ```
   (`SaleService.cs:91-96`, `IsDeskPaymentMethod` at `:702-704` — only `"cash"` and
   `"account_credit"` pass). This is the key finding for the payment-method question below.
3. Resolves/creates the member, prices membership + retail lines against live `MembershipPlan`/
   `Product` rows, checks stock availability via `IStockLedgerService.GetAvailableAsync`
   (`:221-242`), applies promo + manual discount (permission-gated:
   `Permissions.SalesDiscountOverride`, `:274-284`), computes VAT from tenant settings, and
   determines `saleStatus` (`completed` / `partially_paid`, or reject on overpay/underpay without a
   `PartialPayment` block) (`:296-320`).
4. Requires an **open cash-drawer shift** if any payment leg is `cash`
   (`_shiftService.GetCurrentOpenShiftIdAsync`, fails `OPEN_SHIFT_REQUIRED` otherwise) (`:322-327`).
5. Opens an explicit **relational transaction** at `IsolationLevel.ReadCommitted`
   (`:329-332`) — `null` (no-op) on non-relational (test) providers.
6. Inside the transaction: validates `account_credit` balance with an `UPDLOCK, HOLDLOCK` raw-SQL
   `SUM` (`GetMemberCreditBalanceLockedAsync`, `:941-957`) to close a concurrent-double-spend race;
   inserts `Sale`, optionally a new `Membership` (active, dated from "today Cairo",
   `EndDate = day_pass ? today : today.AddDays(plan.DurationDays)`, `SessionsRemaining` seeded for
   `session_pack`/`pt_credits`, `:379-405`), `SaleLine`s per cart line (`:411-441`), a negative
   `MemberCredit` entry if credit was spent (`:443-454`), and one `PaymentTransaction` per payment
   leg (`Status="success"`, `SettlementStatus = cash ? "settled" : "pending"`, `:456-477`).
7. Consumes the promo code atomically (`_promoService.TryConsumeAsync`) — rolls back the whole
   transaction and fails `PROMO_RACE_LOST` if the usage cap was hit concurrently (`:479-490`).
8. `SaveChangesAsync()` to materialize IDs, then **inventory decrement** per retail line: for each
   stocked product, `IStockLedgerService.AllocateSaleAsync` (FEFO batch allocation) then
   `PostAsync` per allocated slice with `Reason = StockMovementReasons.Sale`,
   `ReferenceType = SaleLine` (`:495-543`) — **inside the same DB transaction** as the sale/payment
   rows, so a stock-insufficiency failure here rolls back the whole sale (`:503-508, 531-536`).
   `SaleLine.CogsAmount`/`UnitCost` are back-filled from the actual allocated batch unit costs
   (`:544-549`) — i.e. COGS is captured **at sale time** from FIFO/FEFO batch cost, not from the
   product's current `CostPrice`.
9. Idempotency key row persisted, `SaveChangesAsync()`, **transaction committed** (`:557-570`).
10. **Post-commit, non-transactional side effects** (failures here are logged, not propagated —
    the sale itself has already succeeded): referral conversion attempt (`:572-583`), inline
    invoice creation + Hangfire enqueue (`:603-619`), WhatsApp renewal confirmation
    (fire-and-forget, `:621-625`), and cash-drawer movement recording via
    `_shiftService.RecordMovementAsync` (`:627-640`) — if this last step throws, it's caught and
    logged only (`:635-639`), meaning **a completed cash sale can fail to post its cash-drawer
    movement without the caller ever being told**, silently desyncing the shift's expected-cash
    total from actual sales. This is a concrete failure point: no compensating write, no surfaced
    warning to the response.
11. On `DbUpdateException`/any other exception inside the try block: `transaction.RollbackAsync()`,
    and for idempotency-keyed requests, attempts to find the winning replay row before failing
    outright (`:644-670`).

### Payment methods — internet dependency (the important finding)

- **`cash` and `account_credit`** are the only methods `CreateSaleAsync`/`RecordPaymentAsync` will
  accept directly at the desk (`IsDeskPaymentMethod`, `SaleService.cs:702-704`, enforced at
  `:91-96` and `:730-734`). Both are pure local-DB operations — no external call.
- **`card_paymob`, `fawry`** (and, per `RefundService`'s method lists, `vodafone`/`instapay` too,
  `RefundService.cs:672-679, 692-698`) are **never created synchronously by the desk sale
  endpoint**. They arrive exclusively through **asynchronous gateway webhooks**:
  `GMS.Api\Controllers\PaymentsController.cs`, `POST /api/payments/paymob-webhook` and
  `/fawry-webhook` (`:45-123, 129-197`), `[AllowAnonymous]`, authenticated by HMAC/signature
  verification against the raw request body (Paymob: `X-Hmac` header + HMAC-SHA512 via
  `IPaymobService.VerifyWebhookSignature`; Fawry: `X-Fawry-Signature` + `IFawryService`).
  On a verified success event, `PaymentService.HandleSuccessfulPaymentAsync`
  (`GMS.Application\Services\PaymentService.cs:40-199`) locates the **pre-existing** `Sale` by an
  identity string encoded in the gateway's `merchant_order_id`/`merchantRefNum`
  (`saleId|memberId|tenantId`, parsed by `ParsePaymentIdentity`,
  `PaymentsController.cs:208-227`), records the `PaymentTransaction` (`Status="success"`,
  `SettlementStatus="settled"`), reduces `Sale.AmountDue`, and activates the linked pending
  `Membership` once `AmountDue == 0` (`PaymentService.cs:140-158`).
  → **Confirmed factually**: `card_paymob`/`fawry`/etc. are not "just a label with no live
  integration" — Paymob and Fawry are real external gateways whose webhook must round-trip over
  the internet before the sale is considered paid via that method. But the **desk POS UI itself**
  cannot originate or complete such a payment inline — it can only display `PAYMENT_METHOD_NOT_READY`
  if a caller tries to pass one of those methods straight into `CreateSaleAsync`. The actual
  checkout-link/gateway-initiation code path that produces the `merchant_order_id` and redirects a
  customer to Paymob/Fawry was **not located inside `SaleService`/`SalesController`** — it is
  `UNKNOWN — REQUIRES VERIFICATION` where (likely a member-app/self-serve controller) initiates
  the actual Paymob/Fawry checkout session; searched `GMS.Api\Controllers` for `Paymob`/`Fawry` and
  only found the webhook receivers in `PaymentsController.cs` and refund-side calls in
  `RefundService.cs` (`IPaymobService.RefundAsync`/`IFawryService.RefundAsync`,
  `RefundService.cs:292-295`).

### Inventory decrement — atomicity

Yes — atomic with the sale. `StockLedgerService.PostAsync` (`GMS.Application\Services\StockLedgerService.cs:25-204`)
is called from inside `SaleService.CreateSaleAsync`'s own open transaction (no nested
`BeginTransactionAsync` — `ownsTransaction` is `false` when `_db.Database.CurrentTransaction !=
null`, `StockLedgerService.cs:82`), so a stock failure mid-loop rolls back the whole sale
(`SaleService.cs:501-508, 529-536`). `PostAsync` itself is a guarded balance update with a
`newQty < 0` insufficient-stock check (`StockLedgerService.cs:115-121`) and a retry loop for
`DbUpdateConcurrencyException` (up to 3 attempts, `:81-200`) when it **does** own the transaction
(i.e., when called standalone from `PurchaseOrderService`/`RefundService`, not from `SaleService`).

### Ledger / accounting

**No dedicated general-ledger entity exists.** Financial figures are computed at report time by
`GMS.Application\Services\ProfitabilityService.cs` from `PaymentTransactions`, `Refunds`, `Sales`,
`SaleLines` (for `CogsAmount`), `SaleAdjustments`, and `CashExpenses`
(`ProfitabilityService.cs:29-127`, `:127` confirms `_db.CashExpenses` feeds `OperatingExpenses`)
— i.e. the "ledger" is derived, not stored, exactly matching the audit prompt's second hypothesis.
`DashboardService.BuildFinancialAsync` (`GMS.Application\Services\DashboardService.cs:189-269`)
just re-shapes `IProfitabilityService.GetAsync`'s output for the dashboard.

### Flow 2 internet/external-dependency verdict

**PARTIAL** — a cash or account-credit sale (the desk's actual `POST /api/sales` path) is 100%
local: no external call anywhere in `CreateSaleAsync`/`RecordPaymentAsync` gates success (the
WhatsApp confirmation is fire-and-forget and does not block or fail the sale). A sale whose
customer intends to pay by `card_paymob`/`fawry`/`vodafone`/`instapay` genuinely **requires
internet** to reach completion, because the desk cannot record that payment leg itself —
completion depends on an external gateway webhook (`PaymentsController.cs`) reaching the server
over the internet. In an offline "Local Lifetime Edition," those payment methods would not be
completable through this code path unless a local/offline settlement mechanism is substituted for
them.

---

## Flow 3: Membership Lifecycle

### Plan → end-date computation

`MembershipPlan.PlanType` drives the calculation, always evaluated from Cairo "today"
(`MembershipOperational.TodayCairo()`):
- `day_pass` → `EndDate = today` (same-day only) — `SaleService.cs:388`, `MembershipService.cs:186`.
- everything else → `EndDate = today.AddDays(plan.DurationDays)` — a **fixed calendar-duration**
  model, not session-count-based, for `monthly_unlimited`/`time_limited`/`family`/`trial`/etc.
  (`MembershipService.cs:186`, `SaleService.cs:388`).
- `session_pack` and `pt_credits` additionally seed `SessionsRemaining = plan.SessionCount`
  (`MembershipService.cs:219, 336`, `SaleService.cs:390`) — the calendar `EndDate` still applies in
  parallel to session count (a session-pack plan can still calendar-expire before sessions run
  out, or vice versa; `CheckinService`'s gauntlet checks both independently, see Flow 1 step 6/8).

### Renewal

**Entirely staff-initiated (manual), no automatic renewal execution.** `Membership.AutoRenew`
exists as a boolean field (`GMS.Core\Entities\Membership.cs:37`) and is carried forward
(`MembershipService.cs:340`), but no code path that actually re-bills or auto-creates a renewal
membership when `AutoRenew == true` was found — grepped for `AutoRenew` usage; it is only read/
copied, never branched on to trigger an action.
`MembershipService.RenewMembershipAsync` (`:267-416`) requires an explicit staff call with a
`TransitionMode` (`cancel_and_switch | queue_next | manual_rollover`, normalized/validated by
`PlanTransitionModes.TryNormalize`), computing new start/end dates via
`MembershipRenewalDating.Calculate` (not traced line-by-line here) and applying prior-membership
expiry/queuing per that mode.

### Background expiry job

`GMS.Infrastructure\Jobs\MembershipStatusExpiryJob.cs` — Hangfire job, doc-comment: **"Runs daily
at 00:05 Cairo"** (`:11-13`). It queries all `active`/`frozen` memberships with `EndDate < today`
(Cairo) and flips them to `expired` via `MembershipOperational.TryMarkExpired`
(`:35-47`, shared helper at `GMS.Core\Utilities\MembershipOperational.cs:167-178`). This is
**purely a housekeeping/read-model job** — it does not gate check-in eligibility (Flow 1's gauntlet
independently re-checks `EndDate >= today` on every check-in regardless of whether this job has run
yet), but it does keep list/KPI/search screens' stored `Status` column in sync with the date-aware
effective status. Hangfire itself is configured with **`UseSqlServerStorage`** against the same
tenant database (`GMS.Infrastructure\InfrastructureServiceExtensions.cs:110-124`) — i.e. the job
scheduler's persistence is local SQL Server, not a cloud queue.
The same `TryMarkExpired` helper is also invoked inline (not just by the nightly job) inside
`MembershipService.AssignMembershipAsync` (`:156-164`) and `CancelMembershipAsync` (`:435-437`)
before those operations reason about "does this member already have an active plan" — so stale
`Status` values get self-healed opportunistically on any staff touch, not just once a day.

### Flow 3 internet/external-dependency verdict

**NO** — plan pricing, date math, renewal transition logic, and the nightly expiry job are all
local computation against the local SQL Server database (Hangfire storage included). The only
network-adjacent step is the same fire-and-forget WhatsApp renewal-confirmation call seen in Flow 2
(`SaleService.cs:621-625`), which does not gate the renewal's success.

---

## Flow 4: Refund

### Entry points

`GMS.Api\Controllers\RefundsController.cs`, routed `api/refunds`, `[FeatureFlag("refunds")]`:
- `POST /api/refunds` → `RequestRefund` → `IRefundService.RequestAsync`, permission
  `Permissions.PaymentsRefundRequest` (`:31-46`).
- `POST /api/refunds/{id}/approve` → `Approve` → `ApproveAsync`, permission
  `Permissions.PaymentsRefundApprove` (`:49-63`).
- `POST /api/refunds/{id}/reject` → `Reject` → `RejectAsync`, same approve permission (`:66-79`).
- `GET /api/refunds` — list/filter, same approve permission (`:82-94`).

Implementation: `GMS.Application\Services\RefundService.cs`. This is the backend the frontend's
shared refund-action modal (`.st.requested/.approved/.rejected/.executed` CSS states mentioned in
the task) targets — the service's `Refund.Status` values are literally `"requested"`,
`"executed"`, and `"rejected"` (no separate stored `"approved"` state — approval and execution are
the same atomic step, see below), matching three of those four CSS classes; `"approved"` as a CSS
class most likely represents the same UI moment as `"executed"` in this backend (worth flagging as
a naming mismatch to verify against the frontend source directly — **not fully confirmed**, since
this is a backend-only trace).

### Two-step flow: request → approve (executes immediately) / reject

**`RequestAsync`** (`RefundService.cs:62-174`): validates the sale exists, resolves which specific
`PaymentTransaction` leg is being reversed (single-payment sales infer it automatically; split
sales require the caller to pass `paymentTransactionId` — `:95-115`), computes the refundable
remainder as `paidAmount - alreadyExecutedRefunds` for that leg (or the whole sale for legacy rows
with no payment leg, `:117-138`), rejects if the requested amount exceeds that remainder, then
inserts a `Refund` row with `Status = "requested"` — **no side effects yet**, just a DB insert
(`:149-167`).

**`ApproveAsync`** (`:176-477`) does the actual execution, wrapped in an explicit
`IsolationLevel.Serializable` transaction (`:178-181`):
1. Re-validates refund is still `"requested"`, resolves the approver `AppUser`.
2. **Self-approval guard**: `if (!isOwner && approver.Id == refund.RequestedByUserId) → fail
   SELF_APPROVAL_FORBIDDEN` (`:200-203`) — i.e. **Owners may approve their own refund requests;
   everyone else (including Manager) may not approve a refund they personally requested.** Who can
   approve at all is governed by the `PaymentsRefundApprove` permission — per
   `GMS.Infrastructure\Services\DefaultPermissionProvider.cs:16-23`, only the **Owner** and
   **Manager** roles hold it by default (`Receptionist`/`Trainer` do not); `Manager` gets it via
   `Permissions.All.Except(PlansManage, SettingsManage)`.
3. Re-validates the source payment's trusted-settlement status (`IsRefundablePayment` — settled, or
   cash with corroborating `CashMovement` evidence, `:656-658`) and re-derives the refundable
   remainder against **both** the specific payment leg and the whole-sale trusted-paid total
   (`:212-263`) — defense against a stale/raced remainder.
4. Executes the reversal per `refund.Method`:
   - **`cash`**: requires the approver to have an **open cash-drawer shift**
     (`OPEN_SHIFT_REQUIRED` otherwise) and posts a signed `CashMovement` of type `"refund"`
     (`:270-284`).
   - **`gateway`**: calls `IPaymobService.RefundAsync`/`IFawryService.RefundAsync` against the
     original `ExternalRef` (`:286-304`) — **this is a real external API call inside the
     transaction**; if it returns `false` (gateway doesn't support refunds, or the call fails), the
     whole refund approval fails with `GATEWAY_REFUND_UNSUPPORTED` and the transaction is rolled
     back by the outer `catch` (`:464-471`) — **no partial state persists**, which is correct
     behavior, but it does mean a **DB transaction is held open across a live network call to
     Paymob/Fawry**, which is a lock-contention / long-transaction risk under load (not examined
     further here beyond flagging it).
   - **`credit`**: adds a positive `MemberCredit` ledger entry — pure local write (`:306-324`).
5. Marks `refund.Status = "executed"`, recomputes `Sale.Status` (`"refunded"` if the newly-executed
   total covers the whole paid total, else `"partially_refunded"`, `:327-346`).
6. **On full refund** (`sale.Status == "refunded"`): cancels the linked `Membership`
   (`Status = "cancelled"`, `:348-366`) **and** restores retail stock via
   `RestoreRetailStockAsync` (`:368-384`, detailed below) — **both inside the same transaction**,
   so a stock-restore failure rolls back the membership cancellation and the refund execution too
   (`:373-381`).
7. Commits, then **explicitly disposes** the transaction before any post-commit call that opens
   its own transaction on the same shared `DbContext` (`:387-395`, comment explains why).
8. **Post-commit, non-transactional**: referral-reward reversal (`:425-436`, logged-only on
   failure), credit-note invoice creation for non-credit refunds (`:445-450`, logged-only on
   failure), WhatsApp confirmation (fire-and-forget, `:452-460`), staff notification (best-effort).

### Approval workflow — states

`requested → executed` (via Approve) or `requested → rejected` (via Reject). There is **no third
"approved-but-not-yet-executed" state** — approval performs the cash/gateway/credit action and
flips status to `executed` in the same call, matching the class doc-comment: *"ApproveAsync
executes the refund immediately ... in the same transaction as the approval"*
(`RefundService.cs:16-21`).

### Inventory restoration on refund

**Yes, but only on a full sale refund**, not partial: `RestoreRetailStockAsync`
(`RefundService.cs:483-551`) only runs when `sale.Status == "refunded"` (full), explicitly **not**
for `"partially_refunded"` (comment at `:368-370`: *"INVS-7: restore retail stock only on full sale
refund ... Partial amount refunds leave stock unchanged until line-level refunds exist"*). It walks
each `retail` `SaleLine`, finds the original `StockMovement`(s) posted at sale time
(`ReferenceType=SaleLine, Reason=Sale`), and posts an equal-and-opposite `StockLedgerService.PostAsync`
call with `Reason = StockMovementReasons.SaleRefund` for the same product/warehouse/batch/unit
cost (`:521-547`) — this correctly restores to the exact original batch, preserving COGS history.
Non-stocked products (`TrackStock == false`) are skipped, not treated as an error (`:508-515`). If
the *original* sale movement is missing entirely for a stocked product, the whole refund approval
fails (`OriginalSaleMovementMissing`, `:517-519`) — i.e. a data-integrity gap here blocks refund
approval outright rather than silently under-restoring stock.

### Failure-point summary (Flow 4)

- Gateway refund call is made *inside* a `Serializable` DB transaction — a slow/hanging external
  call to Paymob/Fawry holds DB locks for its duration.
- Partial refunds never restore stock — acceptable per the code's own documented design, but worth
  flagging for the audit since a reader might assume partial refund = partial stock restore.
- Self-approval is blocked for everyone except Owner — a normal Manager cannot rubber-stamp their
  own refund request; only another Owner/Manager, or the same Owner, can.

### Flow 4 internet/external-dependency verdict

**PARTIAL** — `cash` and `credit` method refunds are fully local (DB only). A `gateway` method
refund genuinely calls out to Paymob/Fawry over the internet as a **blocking step inside the
approval transaction**; if that call fails or times out (e.g., offline), the entire refund approval
fails and rolls back — gateway refunds cannot be completed offline by design (the service returns
`GATEWAY_REFUND_UNSUPPORTED` rather than deferring/queuing the call).

---

## Flow 5: Inventory (Purchase Order → Receipt → Sale → COGS → Refund restoration)

### Purchase Order lifecycle

`GMS.Application\Services\PurchaseOrderService.cs`, states: `Draft → Approved → (Partially)Received
→ Cancelled`. `CreateDraftAsync` (`:42-140`) validates supplier/warehouse/products (must
`TrackStock`, be active, respect `AllowFractionalQty`), persists `PurchaseOrder` + `PurchaseOrderLine`
rows, and fires a best-effort staff notification requiring `Permissions.InventoryPurchase` to act
on it. `ApproveAsync` (`:261-289`) only allows `Draft → Approved`. `CancelAsync` (`:291-314`)
blocks cancellation once any line has `QtyReceived > 0`.

### Goods receipt (`ReceiveAsync`, `PurchaseOrderService.cs:316-561`)

- Validates each receipt line: positive qty, non-negative unit cost, fractional-qty rules, and
  **requires a batch number if `Product.TrackBatch`** and **requires an expiry date if
  `Product.TrackExpiry`** (`:352-362`).
- Computes remaining-to-receive **before** any DB writes to reject over-receive early (`:364-368`),
  then re-validates it **atomically** via a guarded raw-SQL UPDATE with a `WHERE QtyOrdered -
  QtyReceived >= @qty` clause (`:478-489`, comment: "High Close H2 — atomic remaining check to
  block concurrent over-receive") — a genuine concurrency-safe design, not just an app-level check.
- Wrapped in an explicit transaction when the caller doesn't already own one
  (`_db.Database.CurrentTransaction == null` check, `:373-375`).
- For each line: creates/reuses a `ProductBatch` (by `BatchNumber`) when the product tracks
  batches/expiry, posts a `StockLedgerService.PostAsync` with `Reason =
  StockMovementReasons.PurchaseReceipt` (positive `QtyDelta`), then the atomic `QtyReceived`
  increment described above, and updates `Product.CostPrice = unitCost` (**last-received-cost
  overwrite** — the product's current `CostPrice` reflects only the most recent purchase, not a
  weighted average) (`:507-509`).
- Posts a `SupplierLedgerEntry` (`Reason = Purchase`) for the total received value (`:521-534`) —
  this is the closest thing to a "purchasing ledger" in the codebase, scoped per-supplier.
- Sets `PurchaseOrder.Status` to `Received` (all lines fully received) or `PartiallyReceived`
  (`:512-518`).

### COGS computation

**Computed at sale time from actual allocated batch cost, not from `Product.CostPrice`.**
`StockLedgerService.AllocateSaleAsync` (`:273-335`) allocates a sale's requested quantity across
FEFO-ordered sellable buckets (earliest expiry first; unexpired batches only —
`LoadSellableBucketsAsync`/`IsSellableBucket`, `:573-637`), computing each bucket's **weighted
average inbound unit cost** from that batch's/warehouse's own `StockMovement` history
(`inboundCosts` groupby, `:294-314`) — this is a weighted-average-cost model per batch, not FIFO
lot-tracking of individual cost layers, and not a straight "current `CostPrice`" lookup. Back in
`SaleService.CreateSaleAsync` (`:510-548`), each allocated slice's `Qty * UnitCost` is summed into
`SaleLine.CogsAmount`, explicitly documented as immutable historical fact:
*"Cost is assigned only from the immutable stock allocation below. Product.CostPrice is a current
catalog hint, not historical COGS"* (`SaleService.cs:432-434`). If any allocated slice lacks a unit
cost (e.g., a batch with no priced inbound movement), `CogsAmount`/`UnitCost` are left `null`
rather than guessed (`completeCost` flag, `StockLedgerService`-consumer logic in `SaleService.cs:538-549`).

### Stock reduction / restoration

Covered in Flows 2 and 4 above — `StockLedgerService.PostAsync` is the single writer of both
`StockMovement` (append-only ledger) and `StockBalance` (denormalized running total per
product/warehouse/batch), used identically for sale decrement, refund restoration, and purchase
receipt increment, all funneled through the same guarded, retry-on-concurrency-conflict code path
(`StockLedgerService.cs:80-204`).

### Flow 5 internet/external-dependency verdict

**NO** — purchase order approval, goods receipt, batch/expiry tracking, FEFO allocation, and COGS
computation are all local SQL Server operations with no external calls anywhere in
`PurchaseOrderService`/`StockLedgerService`.

---

## Flow 6: HR / Shifts — two genuinely distinct systems

**Confirmed explicitly by in-code documentation and by fully separate entities/tables/services —
these are NOT the same concept:**

| | HR employee attendance (payroll clock-in/out) | Cash-drawer shift (POS till) |
|---|---|---|
| Purpose | Track working hours for payroll | Reconcile cash collected during a POS session |
| Controller | `HrEmployeeAttendanceController.cs` (`api/hr/employee-attendance`) | `ShiftsController.cs` (not read in full; `IShiftService` traced) |
| Service | `EmployeeAttendanceService.cs` | `ShiftService.cs` |
| Entity | `EmployeeAttendance` (`TenantId, EmployeeId, AttendanceDate, CheckInAtUtc, CheckOutAtUtc, WorkedMinutes, LateMinutes, OvertimeMinutes, Status, Source`) | `Shift` (`TenantId, UserId, OpenedAt, OpeningFloat, ClosedAt, CountedCash, ExpectedCash, Variance, Status`) + `CashMovement` line items |
| Uniqueness | One row per `(TenantId, EmployeeId, AttendanceDate)` — DB unique index `IX_employee_attendances...` (`EmployeeAttendanceService.cs:171-175`) | One **open** shift per `(TenantId, UserId)` at a time (`ShiftService.cs:45-50`) |
| Explicit doc | *"Separate from member AttendanceController (gym check-in) — this tracks working hours, not gym visits"* (`HrEmployeeAttendanceController.cs:13-14`) | Class doc: *"Cash-drawer shift management: open/close with blind-count reconciliation, signed cash movement tracking, and variance approval"* (`ShiftService.cs:14-17`) |

Cross-link: `SaleService`/`MembershipService`/`RefundService`/`CashExpenseService` all call into
`ShiftService.GetCurrentOpenShiftIdAsync`/`RecordMovementAsync` to require an **open cash-drawer
shift** for any cash-affecting transaction and to keep the drawer's expected-cash total in sync —
this is a completely different "shift" from the HR one and the two never reference each other's
tables.

### HR attendance detail (`EmployeeAttendanceService.cs`)

- `CheckInAsync` (`:30-90`): one row per employee per Cairo day; computes lateness via
  `AttendanceCalculator.ComputeCheckIn` against the employee's scheduled shift template
  (`EmployeeScheduleAssignments` → `EmployeeShift.StartTime`/`GraceMinutes`), with the same
  DB-unique-index-as-backstop pattern as gym attendance (`:74-84`).
- `CheckOutAsync` (`:177-215`) computes `WorkedMinutes`/`OvertimeMinutes` via
  `AttendanceCalculator.ComputeCheckOut`.
- Self-service QR flow (`/me/qr-validate` preview, `/me/qr-check-in` confirm) reuses
  `IGymQrTokenService` (same physical-presence signal as member gym check-in) but writes to
  `EmployeeAttendance`, not `GymAttendance` (`:92-141`).
- `CorrectAsync` (`:241-280`) lets HR-permissioned staff manually adjust a recorded
  check-in/check-out with before/after audit logging.

### Cash-drawer shift detail (`ShiftService.cs`)

- `OpenAsync` (`:37-73`): one open shift per user; records `OpeningFloat`.
- `RecordMovementAsync` (`:109-166`): types `sale | refund | paid_in | paid_out | float_adjust`,
  auto-signs the amount by type (`NormalizeSignedAmount`, `:396-406`); `paid_out` above a
  tenant-configured threshold requires `Permissions.ShiftReconcileApprove` on the caller
  (`:131-143`) — same Owner/Manager-only permission gate as refund approval.
  `RecordMovementAsync`'s optional `callerPermissions` parameter is only actually populated when
  called from a context that resolves them; several call sites in `SaleService`/`MembershipService`
  pass no permissions set, effectively skipping the paid-out threshold check on those paths (not
  fully verified as a real gap — most of those call sites post `"sale"`/`"refund"` movements, not
  `"paid_out"`, so the omission may be immaterial in practice).
- `CloseAsync` (`:168-229`): blind-count reconciliation — `ExpectedCash = OpeningFloat + Σ
  movements`, `Variance = countedCash - expected`; status auto-`"approved"` if `|variance| <=
  tolerance` (tenant setting, default 20 EGP), else `"closed"` (awaiting explicit approval) and a
  `CashVariance` staff notification fires.
- `ApproveAsync` (`:231-269`) / `ForceCloseAsync` (`:271-313`) — manager-side variance
  sign-off / emergency close without a counted-cash figure (still `"closed"`, not auto-approved).

### Flow 6 internet/external-dependency verdict

**NO** for both sub-systems — all reads/writes are local SQL Server; the only shared external-ish
dependency is `IGymQrTokenService`, which (per Flow 1) is itself fully local/stateless.

---

## Flow 7: Expenses

### Recording

`GMS.Application\Services\CashExpenseService.cs`. `CreateAsync` (`:28-138`) validates a
structured category/type catalog (`CashExpenseCatalog.IsKnownCategory`/`IsKnownType` — a fixed
running-cost taxonomy, not free text), a payment method from a fixed set (`cash | card |
bank_transfer | wallet | other`), and — **if `paymentMethod == "cash"`** — **requires an open
cash-drawer `ShiftId`** (`:48-58`), mirroring the same open-shift invariant as Flow 2/4.
Idempotency-key replay is supported (`:60-68`). Inside an explicit transaction (relational only):
inserts the `CashExpense` row, and if it's a cash expense tied to a shift, posts a matching
`CashMovement` of type `"paid_out"` with a **negative** signed amount (`:94-116`) — so a posted
cash expense immediately affects that shift's expected-cash total, same mechanism as a cash refund.
`UpdateAsync` (`:161-271`) carefully re-derives the delta between old and new cash-drawer impact
when amount/status/payment-method/shift change, posting a compensating `CashMovement` adjustment
rather than mutating the original movement row (`:253-266`) — and blocks editing a posted cash
expense whose original shift has already closed (`:205-221`), which prevents silently changing a
reconciled shift's numbers after the fact.

### Dashboard / profit incorporation

`CashExpenses` are **not** read directly by `DashboardService` — they flow through
`ProfitabilityService.GetAsync` (`ProfitabilityService.cs:127` confirms `_db.CashExpenses` is
queried there) which aggregates them into `OperatingExpenses`; `DashboardService.BuildFinancialAsync`
(`DashboardService.cs:189-269`) then gates whether the caller even sees that figure behind
`Permissions.ReportsExpensesView` (`canViewExpenses` parameter, `:193, 218`) — `NetProfit`/
`ProfitMargin`/`OperatingExpenses` are all returned as `null` to a caller without that permission,
even though the underlying `ProfitabilityDto` computed it (`:236-240`). `DashboardOverviewDto`
also separately surfaces today's revenue via a same-day `_profitability.GetAsync(tenantId, today,
today)` call (`DashboardService.cs:96-108`), independent of the period-range financial block.

### Flow 7 internet/external-dependency verdict

**NO** — expense entry, shift-impact posting, and profitability aggregation are all local SQL
Server computation with no external calls.

---

## Cross-cutting observations relevant to an offline "Local Lifetime Edition"

1. **The only genuinely internet-dependent business logic found across all seven flows** is the
   `card_paymob`/`fawry`/(`vodafone`/`instapay` per naming, unconfirmed as implemented) payment
   gateway integration: (a) desk `CreateSaleAsync` refuses to record these methods synchronously at
   all (`SaleService.cs:91-96`), so completing such a sale depends on an external webhook
   (`PaymentsController.cs`) reaching the server; (b) `gateway`-method refund approval makes a
   live, transaction-blocking call to Paymob/Fawry (`RefundService.cs:286-304`).
2. Hangfire (background jobs, including nightly membership expiry and invoice
   creation/enqueueing referenced in Flow 2/3) uses **SQL Server storage**
   (`GMS.Infrastructure\InfrastructureServiceExtensions.cs:110-124`) — i.e. it is exactly as local
   as the primary database, not a hosted/cloud queue.
3. `IGymQrTokenService`, used by **both** member gym check-in and HR employee QR check-in, is fully
   stateless local HMAC — no dependency on any external clock/time-sync service beyond the host
   OS's own clock and the "Egypt Standard Time" zone data already present in the .NET/OS timezone
   database.
4. Fire-and-forget WhatsApp notifications (`IWhatsAppService`) appear after nearly every
   money-moving flow (sale, membership renewal, refund) but are explicitly never awaited/propagated
   as failures (`_ = _whatsAppService...` pattern) — confirmed not to gate any flow's success.
5. Two "duplicate protection" patterns recur identically across Flow 1 (gym attendance) and Flow 6
   (HR attendance): an application-level pre-check plus a DB unique-index backstop caught via a
   custom exception type — a consistent, deliberate concurrency-safety idiom in this codebase.

## Items marked UNKNOWN — REQUIRES VERIFICATION

- The actual controller/service that **initiates** a Paymob/Fawry checkout session (produces the
  `merchant_order_id`/`merchantRefNum` and redirects the customer) was not located within
  `GMS.Api\Controllers` or `GMS.Application\Services\SaleService.cs`/`PaymentService.cs` — only the
  webhook **receivers** (`PaymentsController.cs`) and refund-side gateway calls
  (`RefundService.cs`) were found. Searched: grep for `Paymob`/`Fawry` across `GMS.Api` and
  `GMS.Application\Services`.
- Whether `vodafone`/`instapay` (referenced as valid `PaymentTransaction.Method` values in
  `RefundService.cs:677, 695`) have any live gateway integration at all, or are recorded purely as
  labels via the webhook/manual path — no `IVodafoneService`/`IInstapayService` or equivalent was
  found; searched `GMS.Application\Interfaces` and `GMS.Application\Services` for those names.
- Whether the frontend refund-action modal's CSS state `.st.approved` maps to this backend's
  `"executed"` status or represents a UI-only intermediate state — not verifiable from backend code
  alone; flagged in Flow 4.
