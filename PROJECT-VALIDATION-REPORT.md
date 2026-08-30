# PROJECT VALIDATION REPORT — GymFlowPro / GMS
Phase 2: End-to-End Business Flow Validation

Baseline: Project Understanding Report + PROJECT-UNDERSTANDING.html (Phase 1 discovery).
Method: static code tracing only. No code, schema, or configuration modified.

Status vocabulary: CONFIRMED · PARTIALLY CONFIRMED · UNKNOWN · CONTRADICTS PHASE 1 · POTENTIAL RISK · CONFIRMED BUG

---

# Flow 1 — Member Creation

**A. Business purpose.** Register a new gym member in a tenant with unique phone, encrypted national ID, auto member number, referral code; optionally attach a pending referral attribution.

**B. Entry point.**
- UI: `Frontend/apps/web/src/app/(dashboard)/members/member-modals.js` (member create modal) and POS inline new-member (`pos/pos-app.js` → `SaleService.CreateSaleAsync` `request.NewMember` path).
- API: `POST /api/members` — MembersController.cs:84-85, `[HasPermission(Permissions.MembersCreate)]`.

**C. Frontend → API.**
- Request DTO: `CreateMemberRequest` (FullName, FullNameAr, Phone, Email?, DateOfBirth, NationalId?, Notes?, ReferralCode?/ReferringMemberId?).
- Frontend calls via shared `api-client.js` fetch wrapper (bearer token, silent refresh).

**D. API → Service.**
- MembersController → `MemberService.CreateMemberAsync` (MemberService.cs:105).
- Tenant resolution: TenantMiddleware sets ITenantContext from JWT `gym_code`; controller passes tenantId explicitly.
- Tier enforcement: `_tierEnforcement.CheckCapAsync(tenantId,"active_members")` — soft cap only (PLAN_SOFT_CAP message), never blocks.

**E. Business logic (actual).**
1. Egyptian mobile normalization (`PhoneNormalizer.Normalize`); null → failure.
2. Duplicate phone within tenant rejected via `_memberRepo.GetByPhoneAsync`.
3. Referral pre-check via `ReferralAttributionService.ResolveReferrerAsync` if code/referrer supplied.
4. MemberNumber auto-generated `$"GYM-{sequence:D3}"` from `GetNextMemberSequenceAsync(tenantId)` — note: format differs from the "ST-0001" staff numbering; per-member, tenant-scoped.
5. NationalId AES-encrypted into `NationalIdEncrypted` (or empty string if not provided — never stored plaintext).
6. ReferralCode allocated with 12-attempt collision loop + GUID suffix fallback.
7. `InvitationQuotaRemaining = 0` at creation (quota is derived from covering plan's `ReferralInviteQuota`, computed live in `ComputeInvitationQuotaRemainingAsync`).

**F. Database.** GymFlowProDbContext; creates one `GymMember` row (tenant-scoped query filter active). No ApplicationUser/AppUser rows are created here — **staff-created members have no identity until first app login** (identity is lazily provisioned by AuthService). This refines Phase 1 wording.

**G. Transaction / concurrency / idempotency.**
- No explicit DB transaction. Referral attach failure performs manual compensation: member flagged `IsDeleted=true; IsActive=false` then updated — a compensating soft-delete, not a rollback (PARTIALLY CONFIRMED; works but leaves a tombstone row and is not atomic under concurrent reads).
- No idempotency key on member creation (duplicate submissions blocked only by duplicate-phone check).

**H. Side effects.** None beyond the member row (no payment/shift/invoice/SignalR/job). Audit log for creation was NOT observed in this method (UNKNOWN whether logged elsewhere).

**I. Failure paths.** Invalid phone / duplicate / bad referral → `Result.Failure` before any write. Referral attach failure after write → compensating soft-delete (above). DB failure → exception propagates to controller error handling; no partial state beyond possibly the compensated member.

**J. Final result.** `Result<MemberDetailDto>` incl. computed invitation quota; controller may add PLAN_SOFT_CAP header/message. UI refreshes member list/detail.

**K. Evidence.** GMS.Application/Services/MemberService.cs (105–180, 373–420); GMS.Api/Controllers/MembersController.cs:84; PhoneNormalizer (GMS.Core/Utilities).

**Validation status: CONFIRMED** (with the no-identity-at-creation refinement noted above).

---

# Flow 2 — Membership Purchase (desk assign)

Two real purchase paths exist: desk **assign** (`MembershipsController`) and **POS sale containing a membership line** (Flow 7). Both validated.

### Path A: POST /api/memberships/{memberId}/assign

**B–D. Entry/controller/service.** MembershipsController.cs:77 → `MembershipService.AssignMembershipAsync` (MembershipService.cs:132). Auth `[Authorize]` + permission attributes; tenantId from middleware context.

**E. Business logic (verified order).**
1. Mark expired memberships (`TryMarkExpired`), save if changed.
2. Block assign when operational status ∈ {active, scheduled, frozen} — prevents multiple active memberships.
3. Validate plan active & in-tenant.
4. EndDate = today (Cairo) + DurationDays; day_pass ends same day.
5. Cash ⇒ requires open shift (`ResolveCashStaffAndShiftAsync`: AppUser by `UserId == sub string`, shift via `ShiftService.GetCurrentOpenShiftIdAsync`); non-cash ⇒ membership Status="pending".
6. Inactive member reactivated ("gym ops never leave a paid member inactive").
7. Invitation quota synced for cash; pending referral attached; cash path persists full financial bundle (below).

**F/G/H. Database & transaction.** Cash path: `PersistPaidMembershipAsync` adds Sale + SaleLine(LineType="membership", ReferenceId=membership.Id) + PaymentTransaction(ExternalRef=$"MEMBERSHIP:{membership.Id}") + Membership in ONE `SaveChangesAsync` — single implicit transaction, no explicit scope. Post-save side effects are best-effort try/catch: invoice enqueue (`InvoiceService.EnqueueForSale`), shift cash movement (`ShiftService.RecordMovementAsync(shiftId,"sale",amountPaid,...)` only when method is cash AND amount>0 AND shift exists). **A failed cash movement is logged, not rolled back — drawer can drift from PaymentTransactions** (POTENTIAL RISK, see Cross-flow section).

**Idempotency:** none on this endpoint (idempotency keys exist only in POS SaleService). Duplicate submission of an assign would fail only via the active-membership block AFTER the first succeeds — but two truly concurrent assigns could both pass the check (POTENTIAL RISK: no unique constraint observed preventing two active membership rows).

**Status: CONFIRMED** mechanics; **POTENTIAL RISK** on concurrency + drawer drift.

---

# Flow 3 — Membership Renewal

**Entry.** POST `/api/memberships/{memberId}/renew` (MembershipsController.cs:117) → `MembershipService.RenewMembershipAsync` (259).

**Verified behavior.**
- Requires valid `transitionMode` ∈ {cancel_and_switch, queue_next, manual_rollover} (`PlanTransitionModes.TryNormalize`) — renewal is NOT simply "another membership".
- Plan defaults to prior plan unless overridden; planSource = covering-today ?? operational ?? latest-by-EndDate.
- Dating via `MembershipRenewalDating.Calculate`:
  - Not covering today → start today.
  - Covering + `queue_next` → start = prior.EndDate+1 (queued future membership).
  - Covering + `manual_rollover` → start today, end = prior.EndDate + duration (overlapping extension).
  - Covering + `cancel_and_switch` (default) → start today, end today+duration.
- `ApplyPriorOpenHandling` (cash only): priors marked "expired"; cancel_and_switch additionally clips prior EndDate to today; queue_next untouched.
- Gateway/pending renewals bake dates but do NOT clip/expire the covering membership until payment clears (explicit branch).
- Same financial bundle as assign (Sale/SaleLine/PaymentTransaction/cash movement/invoice enqueue) + audit event `membership.renew` with before/after.
- Renewal detection downstream: Z-report treats membership as renewal iff `LastRenewalDate != null || PlanTransitionMode != ""` (ZReportService.IsRenewal).

**Concurrency/idempotency:** none specific to renew (same risks as Flow 2). **Status: CONFIRMED.**

---

# Flow 4 — Check-in

Owner: `CheckinService` (QR=member self, barcode/manual=staff). Endpoints: AttendanceController.cs (qr-checkin :33, manual-checkin :57, barcode-checkin :80).

**Validated gauntlet (ValidateMembershipGauntletAsync):**
1. Tenant/gym-code validity (QR path checks QR belongs to caller's tenant).
2. Member resolution: QR via `FindMemberByUserIdAsync` (GymMembers where AppUser.UserId == sub-string); manual validates staff AppUser first; barcode exact-match MemberNumber only ("MAC-P0/C10 — never Contains").
3. Active membership exists (status active/frozen, date window covers Cairo today) — cached 5-min IMemoryCache key `membership:{tenant}:{member}`.
4. Stored status must be "active" + date-range check (expired/trial-expired messages).
5. Time restriction window (TimeRestrictionStart/End) — note: compares `DateTime.UtcNow` time-of-day against plan window (see Risks).
6. Session-pack: SessionsRemaining > 0.
7. Trial visit-limit count check.
8. **Duplicate prevention: any attendance already recorded since `DateTime.UtcNow.Date` (UTC midnight) blocks re-entry** — CONFIRMED mechanism; note it uses UTC date while all other rules use Cairo days (POTENTIAL RISK near midnight boundary).
9. Write GymAttendance (EntryMethod qr/barcode/manual, StaffUserId = AppUser.Id domain PK).
10. `DecrementSessionsIfNeededAsync` (Flow 5) + cache invalidation.
11. Fire-and-forget SignalR push `_ = _notifier.NotifyCheckinAsync(...)`.

**Transactional?** NO explicit transaction around attendance-write + session-decrement. See Flow 5 for the consequence analysis.
**SignalR failure:** fire-and-forget task; dashboard misses a live update but check-in succeeds — CONFIRMED safe-degrade.
**Audit:** manual/barcode log `checkin.manual`/`checkin.barcode`.

**Status: CONFIRMED** — matches Phase 1 documented order exactly.

---

# Flow 5 — Session Decrement

- Storage: `Membership.SessionsRemaining` (nullable int) — owned by the Membership row.
- Trigger: only inside check-in flows, AFTER attendance insert succeeds.
- Implementation: raw SQL `UPDATE memberships SET SessionsRemaining = SessionsRemaining - 1 WHERE Id = {id} AND SessionsRemaining > 0` via `ExecuteSqlInterpolatedAsync` (CheckinService.cs:507) then `ReloadAsync`.
- Atomicity: the UPDATE itself is atomic and guarded (`> 0`), so it cannot go negative even under concurrency.
- **Can attendance be created while session consumption fails?** If the SQL affects 0 rows (e.g., concurrent zeroing between validation and decrement), attendance is STILL kept — the method does not check the affected-row count; it just reloads and returns the current value. **Answer: YES — attendance can persist without session consumption. POTENTIAL RISK (race-window), not provably a bug without runtime reproduction.** No transaction wraps both writes; a crash between them yields attendance-without-decrement too.

**Status: PARTIALLY CONFIRMED** (mechanism confirmed; cross-write atomicity absent).

---

# Flow 6 — POS Retail Sale

Path: POS page `pos/pos-app.js` → `POST /api/sales` `[HasPermission(SalesSell)]` (SalesController.cs:61) → `SaleService.CreateSaleAsync`.

**Verified chain.**
1. Idempotency pre-check read-only on `SaleIdempotencyKeys` → replay response if key seen.
2. Staff AppUser resolution (sub-string → AppUser).
3. Cart normalization; retail lines require Inventory feature flag ON (`FeatureAccessService`).
4. Member optional for retail-only (walk-in); inline new-member creation delegates to Flow 1.
5. Product resolution: must be IsActive && !IsArchived && IsSellable; unit price default product.SellPrice.
6. Warehouse: explicit request.WarehouseId, else default-active, else any-active; none → fail.
7. Availability pre-check via `_stockLedger.GetAvailableAsync` (expiry-aware); distinguishes STOCK_UNSELLABLE_EXPIRED vs INSUFFICIENT_STOCK.
8. Promo: membership-plan-scoped only (retail-only carts cannot use promo codes — verified guard `primaryPlan == null → PROMO_INVALID`). Manual discount requires `sales.discount.override` permission, audited.
9. VAT from Tenant.Settings (VatEnabled/VatRate default 0.14); totals half-up rounded.
10. Payments: sum == total → completed; less + partialPayment → partially_paid (+AmountDue/DueDate); overpay rejected; underpay without partialPayment rejected.
11. Cash requires open shift (`GetCurrentOpenShiftIdAsync`).
12. **Explicit transaction**: `BeginTransactionAsync(IsolationLevel.ReadCommitted)` when relational. Inside: account-credit balance locked-read + MemberCredit debit row; Sale + Membership(if membership line, always Status="active") + SaleLines + PaymentTransactions(ExternalRef=$"POS:{saleId}:{i}"); promo consume via atomic conditional UPDATE (PROMO_RACE_LOST on loss); SaveChanges; then per-line FIFO allocation slices posted as negative StockMovements (Reason=sale, ReferenceType=SaleLine, ReferenceId=line.Id) — allocation/post failure rolls back everything; trial conversion fields set when non-trial plan sold to trial member; idempotency key row inserted; commit.
13. Post-commit best-effort: referral convert, inline invoice create + Hangfire redelivery, WhatsApp confirmation, shift cash movement (cash payments only, try/catch logged).

**StockMovement = source of truth? CONFIRMED** — deduction happens through ledger posts; StockBalance.QtyOnHand updated in the same ledger transaction as a cache with RowVersion. Balance cannot be written outside IStockLedgerService (grep confirms writers only there).

**Refund implication:** fully-refunded sales restore stock via original movements (Flow 8).

**Status: CONFIRMED** (strongest flow in the codebase: transaction + idempotency + concurrency handled).

---

# Flow 7 — Membership Sale (POS)

Same `CreateSaleAsync`; differences from retail verified:
- Requires member (walk-in forbidden for membership lines).
- Creates `Membership` Status="active" immediately regardless of payment method mix; mixed payments recorded as PaymentMethod="mixed".
- Promo discount allocated preferentially against membership subtotal; SaleLine.LineTotal stores net-of-discount share.
- Trial conversion (IsTrial→converted + ConvertingSaleId) inside transaction.
- Financial records identical shape to Flow 2 Path A but created inside the POS transaction (better atomicity than desk assign).

Failure matrix:
- Payment succeeds (records committed) → completed sale + active membership + invoice queued.
- Partial → partially_paid + AmountDue; settlement via `RecordPaymentAsync` (SaleService.cs:691): blocks non-partially_paid sales, over-collection rejected, cash requires open shift, new PaymentTransaction points at original invoice receipt URL; optional invoice-per-payment tenant setting.
- Sale created but membership creation fails → impossible within POS (same transaction/SaveChanges batch). Possible in desk-assign path only via exception between Add calls — still single SaveChanges, so effectively atomic.
- Duplicate submission → idempotency replay (BuildReplayResponseAsync) including DbUpdateException race fallback that re-reads winner key. CONFIRMED robust.

**Status: CONFIRMED.**

---

# Flow 8 — Refund

Split model verified: `POST /api/refunds` `[PaymentsRefundRequest]` creates request; `{id}/approve` `[PaymentsRefundApprove]` executes (`RefundService.ApproveAsync`). Self-approval forbidden unless Owner role.

Methods (verified branches):
- **cash** — requires approver's open shift; `RecordMovementAsync(shiftId,"refund",-amount)`; movement failure aborts approval (pre-commit, so consistent).
- **gateway** — Paymob/Fawry external refund call using original PaymentTransaction ExternalRef; unsupported gateway → suggests credit method. External call happens BEFORE local commit (if gateway succeeds but local commit fails, gateway refund exists without local record — POTENTIAL RISK, classic dual-write).
- **credit** — MemberCredit positive entry; deliberately NO credit note (documented reasoning: revenue not reversed).

On approval (inside ReadCommitted transaction):
- Full refund (executedTotal ≥ Total) → Sale.Status="refunded"; linked membership (via SaleLine LineType=membership ReferenceId) → **cancelled**; retail stock restored ONLY on full refund ("INVS-7: partial amount refunds leave stock unchanged until line-level refunds exist") — explicitly documented limitation, not a bug claim.
- `RestoreRetailStockAsync`: restores full retail qty using original sale warehouse/batch from each movement; idempotent via ledger reference (RefundSaleLine + SaleLine.Id).
- Post-commit: credit note (non-credit methods), referral reward forfeit handling, WhatsApp notification — all best-effort logged.

**Membership refund ≠ complete reversal:** cancelling the membership does NOT restore consumed sessions or delete attendances — CONFIRMED design.

**Status: CONFIRMED**, with dual-write gateway risk noted.

---

# Flow 9 — Stock Receiving

Path: `POST /api/inventory/purchase-orders/{id}/receipts` `[InventoryPurchase]` → `PurchaseOrderService.ReceiveAsync` (PurchaseOrderService.cs:292).

Verified:
- PO must be in Receivable status set.
- Per-line validation: positive qty, fractional allowed only if product allows, batch number required if TrackBatch, expiry required if TrackExpiry.
- Over-receive pre-check vs QtyOrdered−QtyReceived (including quantities already staged in the same request).
- Explicit DB transaction (only if none ambient).
- Batch get-or-create (ProductBatch, updates expiry if differs); GRN + lines persisted incrementally (multiple SaveChanges inside tx — safe due to outer transaction).
- Ledger post per line: Reason=purchase_receipt, ReferenceType=GoodsReceiptLine (reference-idempotent).
- **Concurrent over-receive protection ("High Close H2"): atomic conditional UPDATE on purchase_order_lines claiming qty; claimed != 1 → rollback + "Over-receive race".** Non-relational fallback reloads and re-checks. CONFIRMED strong concurrency design.
- Product.CostPrice updated to last received cost; SupplierLedgerEntry (Reason=purchase, Amount=Σ unitCost×qty, ReferenceType=GoodsReceipt) appended → accounts payable.
- PO status transitions draft/approved → partially_received/received.

Partial receiving: supported. Idempotency: via ledger reference uniqueness (a retried identical GRN line would dedupe at PostAsync, though the GRN row itself could duplicate if retried after commit — edge case UNKNOWN/unlikely).

**Status: CONFIRMED.**

---

# Flow 10 — Stock Deduction

Paths verified:
1. **POS sale** (Flow 6): availability pre-check → AllocateSaleAsync (FIFO-ish batch slices) → negative ledger posts.
2. **Manual adjustment**: StockAdjustmentService posts via ledger (reasons validated against StockMovementReasons.All; zero-delta rejected).
3. **Transfers**: out-post at source warehouse + in-post at destination (StockTransferService, includes atomic stock claim SQL at line 388).

Core engine `StockLedgerService.PostAsync` (lines 25–215):
- Validates reason ∈ whitelist, product active/track-stock/fractional rules, warehouse active.
- Reference-idempotent: existing movement with same (tenant, refType, refId, reason, batch) returns it.
- Negative-stock PREVENTION: newQty < 0 → failure (no negative balances possible through the service).
- RowVersion optimistic concurrency with **3-attempt retry** on DbUpdateConcurrencyException when owning the transaction ("Critical Close C1"), detaching pending writes between attempts; unique-violation race handler returns raced movement.

**Source of truth determination: CONFIRMED — StockMovement ledger; StockBalance is a derived cache maintained transactionally by the same service.** Matches Phase 1 exactly.

**Status: CONFIRMED.**

---

# Flow 11 — Shift

- Open (`OpenAsync`): one open shift per staff user enforced (per-user, not per-desk — two staff can hold open shifts simultaneously; CONFIRMED from `s.UserId == staffUser.Id` predicates everywhere).
- Movements (`RecordMovementAsync` ShiftService.cs:102): types {sale, refund, paid_in, paid_out, float_adjust}; signed amounts normalized; shift must belong to the requesting staff user AND be open; `paid_out` above tenant threshold requires `shift.reconcile.approve` permission.
- Close (`CloseAsync`): blind count preserved until close (ExpectedCash null in DTO until closed, line 407 comment); ExpectedCash = OpeningFloat + Σ(Movements.Amount); Variance = counted − expected; tolerance from Tenant.Settings (default 20 EGP); |variance| ≤ tolerance → status "approved" else "closed"; ForceClose available for managers (clears counted/variance).

Operations affecting the drawer (all verified): cash POS sale payments, cash membership assign/renew payments, RecordPayment cash settlements, refund-cash approvals, manual paid_in/paid_out/float_adjust. **Non-cash payments NEVER touch movements** (they appear only in Z-report method totals via PaymentTransactions). CONFIRMED separation.

**Status: CONFIRMED.**

---

# Flow 12 — Z-Report

Data sources verified (ZReportService.ComputeAggregationAsync + GetShiftClosingAsync):
- Daily report aggregates from Sales/PaymentTransactions/CashMovements over the Cairo business-day range (`CairoInclusiveRangeUtc`) — no separate summary table other than persisted ZReport payload rows built by `ZReportGenerationJob` (Hangfire, Cairo tz).
- Per-shift closing (traced in detail): payments filtered by ShiftId & success & amount>0; origin sales by ShiftId (fallback: sale ids referenced by those payments); revenue split per SaleLine type — retail→Products, membership→Renewals iff `IsRenewal` (LastRenewalDate or PlanTransitionMode present) else Memberships, else Other; discounts = DiscountAmount + ManualDiscountAmount; refunds reconciled from executed Refunds referenced by shift "refund" movements (falls back to |movement amount|); cash math purely from movements (CashSales/CashRefunds/CashExpenses/PaidIn/FloatAdjust); expected/counted/difference surfaced only when shift closed (blind-count respected via RevealCash=false while open); payment-method totals from PaymentTransactions.Method.

Assessment: calculations trace correctly to their sources; cash figures derive from movements (drawer truth), revenue from sale lines, method splits from transactions. **No financial-correctness defect demonstrated.** One nuance: a lost cash-movement (Flow 2 risk) would understate CashSales while PaymentTransactions still show the method total → internal inconsistency would become visible. Consistent with POTENTIAL RISK raised earlier, nothing worse.

**Status: CONFIRMED** (calculation tracing), correctness contingent on Flow 2/6 movement reliability.

---

# Flow 13 — Tenant Isolation (security validation)

Answers to the mandated questions:

1. **How is TenantId established?** JWT `gym_code`/`tenant_id` claims; header `X-Gym-Code` fallback. TenantMiddleware (TenantMiddleware.cs) resolves+caches (10-min MemoryCache keyed by gymCode), rejects unknown/inactive gyms, sets ITenantContext.
2. **How does it reach EF Core?** GymFlowProDbContext takes optional ITenantContext; global filters embed `_tenantContext.TenantId`. Services additionally pass explicit tenantId parameters.
3. **Tenant-scoped entities:** 55 types in `TenantScopedEntityTypes` (DbContext lines ~37–80) — verified count.
4. **Intentionally unscoped:** ApplicationUser (AspNetUsers), Tenant, SaleIdempotencyKey, InvoiceSequence (documented NOTE comments in DbContext).
5. **Filter bypasses:** `IgnoreQueryFilters()` found **85×** across Application/Api/Infrastructure — concentrated in AuthService (12), ImportService (11), ReferralRewardService (10), InvoiceService (10), RenderAndDeliverInvoiceJob (4), MemberAppActivationService (3), plus others. Spot-checked instances pair IgnoreQueryFilters with explicit TenantId predicates (AuthService OTP lookups, analytics MERGE SQL). A systematic audit of all 85 was NOT performed — PARTIALLY CONFIRMED safety.
6. **Raw SQL:** 6 sites found (session decrement, invoice sequence UPDLOCK, promo consume, PO claim, transfer claim, analytics MERGE) — ALL interpolate TenantId parameters. No FromSqlRaw without tenant predicate found. CONFIRMED safe as inspected.
7. **Background jobs preserve tenant context?** NO ambient context — jobs iterate tenants and filter explicitly. Verified pattern: AnalyticsAggregationJob lists tenants via IgnoreQueryFilters then runs per-tenant MERGE SQL with literal TenantId. JobScheduler registers ~12 recurring jobs (Cairo tz). Pattern appears deliberate and correct as sampled; every job was not individually audited — PARTIALLY CONFIRMED.
8. **Cached data cross tenants?** Cache keys carry tenantId (`membership:{tenantId}:{memberId}`, `tenant:{gymCode}`, permission cache keyed (tenantId,userId)) — no cross-tenant key collision found. CONFIRMED.
9. **SignalR groups:** AttendanceHub adds connection to group `tenant-{tenant_id claim}`; broadcast targets that group. Group membership derives from authenticated claim — CONFIRMED isolated.
10. **Platform operations outside tenant isolation:** Yes, intentionally — `/platform-api/*` skipped by TenantMiddleware, separate PlatformDbContext/auth scheme/policies. CONFIRMED by design.

Additional: suspension gate (CP5) verified again at middleware level. Unauthenticated requests without gym_code pass through (design, unchanged finding).

**Overall: CONFIRMED with two PARTIALLY CONFIRMED caveats (85× IgnoreQueryFilters audit; per-job tenant-context audit).**

---

# Flow 14 — Member App Authentication

Paths: `POST /api/auth/member-otp` → `SendMemberOtpAsync`; `member-verify` → VerifyMemberOtpAsync; `member-activate` → ActivateMemberAppAsync (AuthService.cs:281/334/384).

Verified chain:
1. Gym identification: case-insensitive GymCode lookup with IgnoreQueryFilters + explicit !IsDeleted + IsActive checks.
2. Phone lookup: GymMembers.IgnoreQueryFilters + explicit TenantId + !IsDeleted (+IsActive for send). Anti-enumeration: verify path returns identical generic message for unknown phone.
3. **OTP requires member Email on file** (delivery is email-based despite phone lookup — OtpDeliveryStrategy sends to masked email). Confirmed surprising-but-real requirement.
4. OTP generate/consume via OtpCacheService (GenerateAndStore reuses valid cached OTP; ValidateAndConsume consumes once; TTL/length from config 5 min/6 digits).
5. Activation-code path: peppered hash consume inside a relational transaction wrapping identity provisioning + token issue (rollback on failure).
6. Identity provisioning: `FindOrCreateMemberIdentityUserAsync` lazily creates ApplicationUser (Identity), `EnsureMemberRoleAsync` adds Member role, `FindOrCreateMemberAppUserAsync` creates AppUser with UserId=identity-id-string, `LinkGymMemberToAppUserAsync` sets GymMember.AppUserId. Chain CONFIRMED end-to-end.
7. JWT: issued via TokenService.GenerateAccessTokenAsync with sub=user.Id (**JWT sub = ApplicationUser.Id — CONFIRMED, Phase 1 claim holds**), tenant_id, gym_code, role(s), perm claims.
8. Refresh tokens: hashed, stored with UserId+TenantId+IP+expiry (30d default); revocation path uses IgnoreQueryFilters by hash (documented) with tenant-filtered ambient revocation.
9. Invalid/expired access token → standard JwtBearer 401 challenge; frontend silent-refresh on `Token-Expired` (api-client.js:170).

**Status: CONFIRMED.**

---

# Cross-Flow Analysis

- Membership purchase (both paths) DOES create expected financial records (Sale+SaleLine+PaymentTransaction[+shift movement if cash]) — but desk-assign records them outside an explicit transaction scope while POS uses one. Renewal follows the SAME rules (shared PersistPaidMembershipAsync). CONFIRMED consistency.
- Check-in consumes entitlement correctly per gauntlet; the sole gap is the attendance/decrement non-atomicity (Flow 5).
- Refund reverses money/stock/membership coherently with documented partial-refund stock limitation; does NOT resurrect sessions/attendance (by design).
- Retail sale affects inventory + shift consistently within one transaction; cash-movement recording is post-commit best-effort in BOTH sale paths → theoretical drawer drift. POTENTIAL RISK.
- Every traced tenant operation carries explicit TenantId or operates under active filters; Member App auth resolves to the correct in-tenant member.
- All traced cash movements bind to the actor's own open shift (ownership enforced in RecordMovementAsync).
- All inventory changes traced go through the stock ledger (no rogue StockBalance writers found).

# Transaction & Data Consistency Audit

| Operation | Explicit Tx | Concurrency guard | Idempotency | Partial-failure exposure |
|---|---|---|---|---|
| POS CreateSaleAsync | Yes (ReadCommitted) | Promo atomic UPDATE; credit lock-read; ledger RowVersion+retry | IdempotencyKey + race fallback | Post-commit side effects best-effort (logged) |
| Desk assign/renew | No (single SaveChanges) | None on active-membership check | None | Cash movement/invoice failures swallowed → drawer/report drift |
| Check-in + decrement | No | Decrement guarded SQL (>0) | Duplicate same-day UTC check | Attendance can exist w/o decrement (race) |
| Goods receipt | Yes | Atomic PO-line claim (H2) | Ledger reference | Safe (tx rollback) |
| Ledger PostAsync | Own tx when top-level | RowVersion ×3 retry + unique-violation race recovery | Reference tuple | Safe |
| Refund approve | Yes | Executed-total recompute in tx | Stock restore via ledger ref | Gateway dual-write risk (external before commit) |
| Invoice numbering | n/a (UPDLOCK SQL) | Atomic OUTPUT + INSERT-race retry ×10 | Sequence itself | Safe |
| Member create | No | Phone-dup check (non-unique-index dependent?) | None | Compensating soft-delete on referral failure |

Unique index existence for member phone per tenant was not verified from migrations — UNKNOWN (dup check is query-based).

# Side-Effect Matrix

| Flow | Database | Payment | Inventory | Shift | Invoice | SignalR | Background Job | Notification |
|------|----------|---------|-----------|-------|---------|---------|----------------|--------------|
| Member Creation | YES | NO | NO | NO | NO | NO | NO | NO |
| Membership Purchase (desk) | YES | YES | NO | CONDITIONAL (cash) | YES (enqueue) | NO | YES (invoice job) | CONDITIONAL (WhatsApp via POS path) |
| Membership Purchase (POS) | YES | YES | CONDITIONAL (mixed cart) | CONDITIONAL (cash) | YES (inline+queue) | NO | YES | YES (WhatsApp) |
| Membership Renewal | YES | YES | NO | CONDITIONAL (cash) | YES | NO | YES (invoice job) | NO (observed) |
| Check-in | YES | NO | NO | NO | NO | YES | NO | NO |
| Session Decrement | YES | NO | NO | NO | NO | NO | NO | NO |
| POS Retail Sale | YES | YES | YES | CONDITIONAL (cash) | YES | NO | YES | NO |
| Membership Sale (POS) | YES | YES | CONDITIONAL | CONDITIONAL (cash) | YES | NO | YES | YES |
| Refund | YES | YES (reversal/credit) | CONDITIONAL (full retail refund) | CONDITIONAL (cash) | YES (credit note) | NO | NO | YES (WhatsApp) |
| Stock Receiving | YES | NO | YES | NO | NO | NO | NO | NO |
| Stock Deduction | YES | NO | YES | NO | NO | NO | NO | NO |
| Shift | YES | NO | NO | YES | NO | NO | NO | NO |
| Z-Report | YES (persisted payload) | NO (reads) | NO | YES (reads) | NO | NO | YES (generation job) | NO |
| Tenant Isolation | n/a | n/a | n/a | n/a | n/a | YES (group isolation) | CONDITIONAL (jobs self-scope) | n/a |
| Member App Auth | YES | NO | NO | NO | NO | NO | NO | CONDITIONAL (OTP email) |

# Phase 1 Validation

| Previous Finding | Result | Evidence | Explanation |
|------------------|--------|----------|-------------|
| 3-layer tenancy (middleware/query filters/explicit) | CONFIRMED | TenantMiddleware.cs; GymFlowProDbContext; service predicates | As described |
| 55 tenant-scoped entity types | CONFIRMED | DbContext TenantScopedEntityTypes | Count matches |
| Check-in gauntlet order (8 steps) | CONFIRMED | CheckinService.ValidateMembershipGauntletAsync | Order identical incl. comments |
| Effective status computed (Cairo) | CONFIRMED | MembershipOperational.GetEffectiveStatus | Used by services/UI paths traced |
| StockMovement = truth, StockBalance = cache | CONFIRMED | StockLedgerService.PostAsync; sole-writer pattern | Verified |
| Cash drawer invariant | PARTIALLY CONFIRMED | MembershipService.PersistPaidMembershipAsync; SaleService post-commit | Movement recording exists but is best-effort post-commit — invariant intended, durability weaker than implied |
| JWT sub = ApplicationUser.Id | CONFIRMED | TokenService.cs:41; CheckinService/AppUser.UserId joins | Exact match |
| Idempotency keys on sales | CONFIRMED | SaleService a-step + catch-fallback | Includes race recovery |
| Referral rewards via hold job | PARTIALLY CONFIRMED | TryConvertOnPaidActivateAsync + ProcessReferralRewardHoldsJob | Conversion hook confirmed; hold-job internals not fully traced |
| Suspension 72h buffer | CONFIRMED | TenantMiddleware CP5 gate | Matches |
| PermissionsOverride unused | PARTIALLY CONFIRMED | DefaultPermissionProvider remarks | Comment says ignored; exhaustive runtime search not done |
| MemberNumber "GYM-nnn" vs ST-style staff numbers | CLARIFIED | MemberService.cs:139; AppUser.StaffNumber doc | Two distinct sequences; Phase 1 didn't conflate but detail now precise |
| Flutter app spec-only | CONFIRMED | repo scan | Unchanged |
| Filters inactive when ITenantContext null | CONFIRMED | DbContext constructor + ApplyGlobalQueryFilters early-return | Risk stands |

Contradictions with Phase 1: **NONE** — no finding reversed. Two refinements: (1) cash-drawer invariant durability weaker than Phase 1 tone suggested; (2) member creation provisions no identity (Phase 1 stated lazy provisioning generally; now pinned to creation-vs-login boundary).

# Critical Rules — Validation Status

| Rule | Status | Evidence | Notes |
|------|--------|----------|-------|
| Attendance validation order | CONFIRMED | CheckinService header + implementation | Order intact |
| Effective membership status | CONFIRMED | MembershipOperational | Used consistently in traced flows |
| Session consumption | PARTIALLY CONFIRMED | DecrementSessionsIfNeededAsync | Atomic decrement, but not transactional with attendance insert; 0-row case unchecked |
| StockMovement inventory truth | CONFIRMED | StockLedgerService | Sole writer verified |
| Cash drawer invariant | PARTIALLY CONFIRMED | PersistPaidMembershipAsync / CreateSaleAsync post-commit blocks | Recorded, but swallow-on-failure |
| Combined tenant+soft-delete filter | CONFIRMED | DbContext ApplyGlobalQueryFilters + skip-set | Split-bug guard intact |
| JWT subject identity | CONFIRMED | TokenService; FindMemberByUserIdAsync | Holds |
| Sale idempotency | CONFIRMED | SaleService | Read-precheck + unique-race recovery |
| Referral reward hold | PARTIALLY CONFIRMED | hooks verified, hold job internals untraced | |
| Suspension grace buffer | CONFIRMED | TenantMiddleware | 72h default from config |

# Validation Scorecard

| Flow | UI | API | Service | Database | Side Effects | Confidence |
|------|----|----|---------|----------|--------------|------------|
| 1 Member Creation | PARTIAL (modal located) | OK | OK | OK | OK | HIGH |
| 2 Membership Purchase | PARTIAL | OK | OK | OK | OK | HIGH |
| 3 Membership Renewal | NOT TRACED IN UI | OK | OK | OK | OK | HIGH |
| 4 Check-in | PARTIAL | OK | OK | OK | OK | HIGH |
| 5 Session Decrement | n/a | n/a | OK | OK | OK | MEDIUM (race unproven) |
| 6 POS Retail Sale | PARTIAL (pos-app.js) | OK | OK | OK | OK | HIGH |
| 7 Membership Sale | PARTIAL | OK | OK | OK | OK | HIGH |
| 8 Refund | NOT TRACED IN UI | OK | OK | OK | OK | HIGH |
| 9 Stock Receiving | NOT TRACED IN UI | OK | OK | OK | OK | HIGH |
| 10 Stock Deduction | PARTIAL | OK | OK | OK | OK | HIGH |
| 11 Shift | NOT TRACED IN UI | OK | OK | OK | OK | HIGH |
| 12 Z-Report | NOT TRACED IN UI | OK | OK | OK | OK | HIGH |
| 13 Tenant Isolation | n/a | OK | OK | OK | OK | MEDIUM-HIGH (85× IgnoreQueryFilters not exhaustively audited) |
| 14 Member App Auth | n/a (spec docs) | OK | OK | OK | OK | HIGH |

Fully validated flows: 11 · Partially validated flows: 3 · Unknown flows: 0 · Potential risks: 7 (below) · Confirmed bugs: 0 · Phase 1 contradictions: 0

# Final Findings

## Confirmed Architectural Facts
1. Three-plane architecture with separate PlatformDbContext/auth scheme; platform-api bypasses tenant middleware.
2. POS sales run inside explicit ReadCommitted transactions with idempotency-key replay incl. commit-race recovery.
3. Stock ledger is the single write path for inventory state; RowVersion ×3 retry + reference idempotency + negative-stock rejection.
4. Goods receipts enforce concurrent over-receive protection via atomic conditional claim SQL.
5. Invoice numbering is UPDLOCK-atomic with insert-race retry.
6. Member-app identity chain (ApplicationUser→AppUser→GymMember) lazily provisioned inside activation transaction; JWT sub = ApplicationUser.Id.
7. SignalR attendance events are tenant-group isolated by JWT claim.
8. Z-report figures decompose exactly into traced sources (movements for cash, sale lines for revenue, transactions for methods).
9. Renewal implements three genuine transition semantics (cancel_and_switch clip, queue_next future dating, manual_rollover overlap-extension).

## Potential Risks
1. Attendance write and session decrement are not wrapped in one transaction; 0-row decrement is silently tolerated → attendance-without-consumption possible under race.
2. Cash-movement recording (assign/renew/sale/record-payment) is post-commit best-effort → drawer/Z-report can drift from PaymentTransactions on failure.
3. Desk assign/renew lack idempotency keys and rely on a non-concurrency-safe active-membership check (two racing assigns plausible; no unique constraint verified).
4. Gateway refunds execute externally BEFORE local commit → orphaned external refund if commit fails (dual-write).
5. Duplicate check-in guard uses UTC date while business day rules elsewhere use Cairo calendar → midnight-boundary inconsistency.
6. Time-restriction check compares UtcNow clock-time to plan window (potential mismatch for a Cairo-timezone product rule).
7. 85× IgnoreQueryFilters sites and per-Hangfire-job tenant scoping not exhaustively audited.
8. Member-phone uniqueness relies on application-level check; backing unique index unverified.

## Confirmed Bugs
None demonstrable from static inspection. (All anomalies above require runtime conditions to manifest.)

## Unknown / Requires Runtime Verification
- Whether a DB unique index backs member phone per tenant.
- Live Paymob/Fawry webhook ↔ refund reconciliation behavior.
- Concurrency behavior of concurrent desk assigns under load.
- Actual Hangfire job coverage under missing-tenant-context scenarios at scale.
- apps/web wwwroot build drift vs source.

## Contradictions With Phase 1
None. Two refinements recorded (cash-invariant durability; identity provisioning timing) — documented above without rewriting the baseline.

---
*Discovery/validation only. Nothing implemented, fixed, or refactored.*
