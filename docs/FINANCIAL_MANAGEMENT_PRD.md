# GymFlowPro — Financial Management & Profitability System PRD

**Product:** GymFlowPro (multi-tenant SaaS for gyms & fitness centers)  
**Document type:** Product Requirements Document (PRD)  
**Version:** 1.0  
**Status:** Implementation-ready (aligned to `financial-v1` canonical backend)  
**Calculation version header:** `X-Financial-Calculation-Version: financial-v1`  
**Primary timezone:** Africa/Cairo (`MembershipOperational.CairoInclusiveRangeUtc`)  
**Last updated:** 2026-09-01  

**Canonical backend services:** `ProfitabilityService`, `DashboardService`, `CashExpenseService`  
**Canonical APIs:** `/api/reports/profitability`, `/api/reports/cash-flow`, `/api/dashboard/overview`, `/api/expenses`  

---

## 1. Executive Summary

GymFlowPro needs one **auditable, tenant-isolated financial model** that lets gym owners answer: *How much did we earn? How much did we spend? What did it cost to run? What profit did we make? Where did cash go?* — without mixing cash drawer activity with accounting profit.

This PRD defines the **final product behavior** for Financial Management & Profitability on top of existing modules (Sales/POS, Payments, Refunds, Memberships, Inventory, Suppliers, Payroll, Shifts, Expenses, Owner Dashboard, Reports).

**Core decision:** Profitability and Cash Flow are **separate lenses**. Revenue is **accrual** (sale recognition). Collections and settled cash are **cash-event** metrics. Operating costs live in the existing **`CashExpense`** ledger only — no second expense ledger. Payroll and supplier inventory/AP are **not** folded into Operating Expenses.

**Canonical calculation engine:** `ProfitabilityService.GetAsync(tenantId, from, to)` — all Owner KPIs and financial reports must consume this (or `DashboardService` mapping of the same DTO), not legacy report formulas.

---

## 2. Problem Statement

### 2.1 Owner confusion (observed)

- **Operating expenses EGP 0** while **Net profit negative** because payroll is a separate accrual and running-cost ledger was empty — not a math bug.
- **Cash collected** shown alongside **revenue** without explaining accrual vs cash.
- **Supplier AP** (unpaid stock) mistaken for monthly operating expense.
- **Partial-month filters** charge a **full overlapping payroll month** (`IsMonthInRange`, not prorated) — day-1 September can show full September salaries against one day of revenue.
- Legacy **Sales Report** (payment-date collections) diverged from **Profitability** (sale-date revenue), producing conflicting “revenue” numbers.
- **COGS** unavailable when retail cost snapshots missing — UI must not invent COGS.
- **Cash flow** unavailable when settlement evidence incomplete — UI must not show misleading net cash flow.

### 2.2 Engineering risk

- Multiple calculation paths (Dashboard analytics, Z-Report, Sales tab, Profitability) created **competing sources of truth**.
- Cash movements, supplier payments, and expenses were at risk of **double-counting** if folded into one ledger.

### 2.3 Product goal

One canonical financial story for Owners; operational reports remain for desk workflows but **do not** drive Owner KPIs.

---

## 3. Goals

| ID | Goal |
|----|------|
| G1 | Single canonical **Revenue**, **COGS**, **Gross Profit**, **Net Profit** per tenant/period |
| G2 | Separate, explicit **Cash Flow** model (inflows/outflows/net) with availability gates |
| G3 | Structured **running/operating costs** via `CashExpense` + shared catalog (no duplicate ledger) |
| G4 | **Payroll expense** visible separately; contributes to Net Profit per accrual policy |
| G5 | **Supplier purchases** increase AP/inventory; **never** auto-post as OpEx |
| G6 | Owner Dashboard: gym-owner language (**Cost to run**, **Unpaid supplier stock**, profitability bridge) |
| G7 | Every displayed financial number traceable to API field + entity source |
| G8 | Tenant isolation, permission gating, audit on financial mutations |
| G9 | Preserve existing data; evolve schema with drift-safe migrations |
| G10 | Legacy endpoints remain for operations but are **non-canonical** for Owner KPIs |

---

## 4. Non-Goals

| ID | Non-Goal |
|----|----------|
| NG1 | Full double-entry accounting / GL module |
| NG2 | Second expense ledger (e.g. parallel `OperatingExpense` table) |
| NG3 | Payroll proration in this release (**OPEN DECISION** for future) |
| NG4 | Putting payroll or supplier AP into `CashExpense` OpEx totals |
| NG5 | Treating Z-Report or Sales Report collections as canonical Revenue |
| NG6 | Automatic COGS from supplier purchases at GRN time (COGS at retail sale) |
| NG7 | Member-facing profit/cash dashboards |
| NG8 | Multi-currency (tenant operates in gym currency, EGP in current deployments) |
| NG9 | Tax / VAT reporting as P&L lines (VAT on invoices is separate product scope) |

---

## 5. Personas & Roles

| Persona | Needs | Financial access |
|---------|-------|------------------|
| **Gym Owner** | Full P&L, cash, AR/AP, trends, expense entry, drill-down | All financial + expense manage |
| **Manager** | Month performance, running costs, collections, limited HR payroll | Financial view + expense manage (default grants) |
| **Receptionist** | POS, shifts, collect payment — not P&L | No financial permissions (default) |
| **Trainer** | Classes, attendance | No financial permissions (default) |
| **Member (app)** | Own payments, membership | No staff financial APIs |

**Role source:** JWT `perm` claims from `RolePermissionResolver` at login; Owner receives `Permissions.All`. Tenant overlay `Tenant.Settings.role_permissions` can customize Manager/Receptionist/Trainer (Owner locked).

---

## 6. Accounting Principles

1. **Single source of truth per metric** — see Section 14.
2. **Accrual vs cash separation** — Revenue ≠ Collections; Expense recognition ≠ Cash outflow (when payment method non-cash or settlement pending).
3. **No silent inference** — If COGS, Net Profit, Settled Cash, or Cash Flow cannot be computed reliably, return **unavailable** + `dataIssues` codes; UI shows explicit unavailable state.
4. **No duplicate ledgers** — Running costs = posted `CashExpense` (excluding payroll payment rows).
5. **Inventory/AP vs OpEx** — Supplier `purchase` ledger entries are AP; retail COGS hits P&L on sale via `SaleLine.CogsAmount`.
6. **Executed-only refunds** — Only `Refund.Status == executed` affects revenue and cash refunds.
7. **Tenant isolation** — All queries filtered by `TenantId`; global query filters on domain entities.
8. **Auditability** — Expense post/void, sale adjustments, refunds, payroll disbursements logged via `AuditService` where implemented.
9. **Cairo calendar** — All period filters use Africa/Cairo inclusive date ranges converted to UTC half-open intervals.
10. **Calculation version** — Responses include `calculationVersion: financial-v1` for forward compatibility.

---

## 7. Revenue Policy

### 7.1 Canonical definition

**Revenue** = recognized sale value in period, net of executed refunds and posted cancellation adjustments.

```
GrossRevenue     = Σ Sale.Total
                   WHERE Sale.CreatedAtUtc ∈ [Cairo(from), Cairo(to))

RefundsTotal     = Σ Refund.Amount
                   WHERE Status = executed
                   AND ExecutedAt ∈ range

RevenueAdjustments = Σ SaleAdjustment.Amount
                   WHERE Type = cancellation, Status = posted
                   AND CreatedAtUtc ∈ range

Revenue          = GrossRevenue − RefundsTotal − RevenueAdjustments
```

### 7.2 Recognition date

| Event | Date field | Policy |
|-------|------------|--------|
| Sale recognition | `Sale.CreatedAtUtc` | Accrual at sale creation (POS, membership assign/renew, drop-in, etc.) |
| Refund reduction | `Refund.ExecutedAt` | Only executed refunds |
| Cancellation adjustment | `SaleAdjustment.CreatedAtUtc` | Only `type=cancellation`, `status=posted` |

### 7.3 What is NOT canonical revenue

| Source | Why excluded |
|--------|----------------|
| `PaymentTransaction.PaidAtUtc` sums | Cash timing, not accrual |
| `CashMovement` on shift | Drawer physics, not revenue |
| Z-Report sales totals | Shift operational snapshot |
| `GET /api/reports/sales` Net cash-in | **Legacy operational** — payment-date collections |
| `Membership.Plan.Price` | List price, not recognized revenue |
| `Membership.AmountPaid` | First-payment snapshot; not updated on Collect Payment |

### 7.4 Partial payments & AR

- `Sale.Total` is gross recognized amount for the sale.
- `Sale.AmountDue` is outstanding balance (AR component).
- Collecting payment later does **not** increase Revenue again — it reduces AR and increases Collections.

### 7.5 Revenue breakdown (canonical)

From retail/membership sale lines in period (`SaleLine` on sales in range):

| Bucket key | Line types (current) |
|------------|----------------------|
| `memberships` | membership plan lines |
| `products` | retail product lines |
| `classes` | drop-in / class lines |

**OPEN DECISION:** `renewals` bucket exists in DTO shape but classification may not populate it separately — renewals currently roll into memberships unless explicitly implemented.

### 7.4 UI labels

- Dashboard **Revenue** = accrual (with trust state).
- **Collected** / **Collections** = operational cash-in (`PaymentTransaction`), separate card.

---

## 8. COGS Policy

### 8.1 Definition

**COGS** = cost of retail goods sold in period, from line-level snapshots.

```
COGS = Σ SaleLine.CogsAmount
       FOR retail lines on sales WHERE CreatedAtUtc ∈ range
       ADJUST for fully-refunded retail sales in range (negate their COGS)
       NULL if any retail line missing CogsAmount
       OR any partially-refunded retail sale in range
```

### 8.2 COGS source per sale line

- `SaleLine.CogsAmount` populated at sale from stock cost evidence (`UnitCost` / batch / movement-backed snapshot).
- **Backfill:** `POST /api/reports/profitability/backfill-cogs` (perm `inventory.manage`) — traceable stock evidence only.

### 8.3 What is NOT COGS

| Event | Treatment |
|-------|-------------|
| Supplier purchase (GRN) | AP ledger `+amount`; inventory asset; **not** P&L COGS |
| Supplier payment | AP reduction; cash outflow; **not** COGS |
| Membership sales | No COGS (service) |
| Payroll | Payroll expense line, not COGS |
| Running costs | Operating expenses |

### 8.4 Unavailable behavior

When `CogsAvailable = false`:

- `Cogs`, `GrossProfit`, `NetProfit` may be null.
- UI: **"COGS unavailable"** — do not estimate from supplier purchases or product cost fields.
- `dataIssues` may include: `cogs_unavailable`, `retail_refund_cogs_unavailable`.

### 8.5 Gross profit

```
GrossProfit = Revenue − COGS   (null if COGS null)
```

---

## 9. Payroll Policy

### 9.1 Components (distinct)

| Concept | Source | Affects |
|---------|--------|---------|
| **Payroll calculation** | `PayrollPeriodService` lines | `PayrollLine.NetSalary` |
| **Payroll expense (accrual)** | Approved/Closed periods overlapping range | Net Profit |
| **Payroll payment (cash)** | `PayrollPayment` by `PaidDate` | Cash outflow; linked `CashExpense` |
| **Payroll cash drawer** | — | **OPEN:** no automatic `CashMovement` today |

### 9.2 Net salary formula (period lines)

```
hourlyRate = BasicSalary / 240
overtimeAmount = auto overtime from minutes + manual adjustments
NetSalary = BasicSalary + overtimeAmount + bonus + allowance − deduction
```

### 9.3 Accrual inclusion rules

- Period `Status` ∈ {`Approved`, `Closed`}.
- Period month overlaps query `[from, to]` via `IsMonthInRange`:
  ```
  periodStart = DateOnly(Year, Month, 1)
  periodEnd   = periodStart.AddMonths(1).AddDays(-1)
  overlap     = periodStart <= to AND periodEnd >= from
  ```
- **Full month `NetSalary` summed** for overlapping periods — **not prorated** to partial date ranges.

### 9.4 Exclusion from Operating Expenses

`CashExpense` rows with `Category = payroll` OR `SourceType = payroll_payment` are **excluded** from `OperatingExpenses` to prevent double-count with `PayrollExpense`.

### 9.5 Availability

| `PayrollCoverageStatus` | Meaning |
|-------------------------|---------|
| `NO_PAYROLL_PERIOD` | No approved/closed period overlaps range |
| `PAYROLL_DATA_INCOMPLETE` | Overlap exists but lines invalid/incomplete |
| `COMPLETE` | Payroll expense computable |

When not `COMPLETE`: `PayrollExpense = null`, `NetProfitAvailable = false`.

### 9.6 Owner UX requirement

On partial calendar month filters, show warning: *Salaries reflect the full payroll period overlapping this range — not prorated.*

**OPEN DECISION (future):** Payroll proration by days in range vs keep full-month accrual.

---

## 10. Operating Expense Policy

### 10.1 Canonical ledger

**Operating Expenses (Running costs)** = posted rows in **`cash_expenses`** excluding payroll.

```
OperatingExpenses = Σ CashExpense.Amount
  WHERE Status = posted
  AND Category != payroll
  AND SourceType != payroll_payment
  AND ExpenseDate ∈ [from, to]  (DateOnly inclusive)
```

### 10.2 No second ledger

Desk **Running costs** UI posts to `CashExpense` via `POST /api/expenses`. Do not create parallel `OperatingExpense` entities.

### 10.3 Structured catalog

**Backend:** `CashExpenseCatalog`  
**Frontend:** `GfpCashExpenseCatalog` (mirror)

| Category | Example types |
|----------|----------------|
| Utilities | Electricity, Water, Gas, Internet, Telephone |
| Rent & Property | Rent, Property services, Maintenance |
| Software & Technology | Gym management software, POS subscriptions, Other SaaS |
| Operations | Cleaning, Security, Repairs, Supplies, Equipment maintenance |
| Marketing | Advertising, Social media, Printing, Promotions |
| Banking & Payment | Bank fees, Payment gateway fees |
| Other | Miscellaneous |

- `Category` = catalog category (required).
- `Description` = expense type from catalog (required) — **not** free-text category.
- `Note` = optional free-text notes (legacy compatible).
- `SourceType` for manual desk posts = `running_cost` (default if omitted).

### 10.4 Payment methods

`cash | card | bank_transfer | wallet | other`

- **Cash:** requires open `ShiftId`; creates `CashMovement` type `paid_out` (negative amount).
- **Non-cash:** no shift required; no drawer movement.

### 10.5 Void — not delete

- Status transitions: `posted` → `void`.
- Voided rows excluded from `OperatingExpenses`.
- Row retained for audit (no hard delete in normal workflow).

### 10.6 What is NOT operating expense

| Item | Correct treatment |
|------|-------------------|
| Payroll accrual | `PayrollExpense` |
| Payroll disbursement `CashExpense` | Excluded from OpEx query |
| Supplier purchase (GRN) | AP + inventory |
| Supplier payment | AP reduction + cash outflow |
| COGS | Gross profit deduction |
| Membership refunds | Revenue reduction |

---

## 11. Cash Flow Policy

### 11.1 Separation from profitability

Cash flow answers: *What cash moved?* Profitability answers: *What did we earn after costs?*

### 11.2 Canonical formulas

```
SettledCashInflow = Σ PaymentTransaction.Amount
  WHERE Status = success, Amount > 0, PaidAtUtc ∈ range
  AND IsSettledCash(payment)

IsSettledCash = Method != account_credit
  AND SettlementStatus == settled

CashRefunds = Σ Refund.Amount (executed, in range, Method != credit)

OperatingExpenseCashOutflows = OperatingExpenses
  (NOTE: current impl sums all posted OpEx by ExpenseDate regardless of payment method — see OPEN DECISION)

PayrollCashDisbursements = Σ PayrollPayment.Amount by PaidDate in range

SupplierCashPayments = Σ |SupplierLedgerEntry.Amount|
  for payment entries in range (negative amounts), only if evidence complete

CashOutflows = CashRefunds + OperatingExpenses + PayrollCashDisbursements + SupplierCashPayments

NetCashFlow = SettledCashInflow − CashOutflows
```

### 11.3 Collections vs settled cash

| Metric | Definition |
|--------|------------|
| **Collections** | All successful payments in range (includes pending settlement) |
| **Settled cash inflow** | Only payments passing `IsSettledCash` |

### 11.4 Cash inflow ≠ revenue

Example: Membership sold in August (Revenue August), paid in September (Collection September).

### 11.5 Cash outflow ≠ expense

Example: Electricity paid by bank transfer in September — OpEx by `ExpenseDate`; cash outflow only if modeled as cash movement (current OpEx cash-outflow line uses expense accrual date, not payment settlement).

### 11.6 Availability

`CashFlowAvailable = true` only when:

- No `settlement_data_incomplete` in `dataIssues`
- `SupplierCashPaymentsAvailable = true` (all supplier payments in range have `ReferenceType = CashMovement`)

Otherwise: hide or label **Net cash flow unavailable** on Owner dashboard; show **Collected** where appropriate.

**OPEN DECISION:** Fix supplier payment posting to set `ReferenceType`/`ReferenceId` so supplier cash evidence is available.

---

## 12. Profitability Model

### 12.1 Canonical formula

```
Revenue          (accrual, Section 7)
− COGS           (nullable)
= Gross Profit   (nullable)

Gross Profit
− OperatingExpenses   (CashExpense running costs)
− PayrollExpense      (nullable accrual)
= Net Profit          (nullable)

ProfitMargin = NetProfit / Revenue × 100  (only when both available, Revenue > 0)
```

### 12.2 Alternative paths forbidden

Owner Dashboard, Reports Profitability tab, and `/financial-reconciliation` **must** use `ProfitabilityService` only.

### 12.3 Net profit availability

`NetProfitAvailable = NetProfit.HasValue`

Requires: `GrossProfit` not null AND payroll coverage `COMPLETE`.

---

## 13. Financial Data Model

### 13.1 Core entities (existing — evolve, do not duplicate)

| Entity | Role in financial system |
|--------|--------------------------|
| `Sale` | Revenue recognition (`Total`, `CreatedAtUtc`, `AmountDue`, `Status`) |
| `SaleLine` | Revenue breakdown, COGS (`CogsAmount`, `LineType`) |
| `PaymentTransaction` | Collections, settled cash, allocation to sales |
| `Refund` | Executed refunds (revenue + cash/credit split) |
| `SaleAdjustment` | Cancellations (revenue) vs write-offs (AR only) |
| `CashExpense` | Running costs + payroll payment mirror rows |
| `PayrollPeriod` / `PayrollLine` | Accrual expense |
| `PayrollPayment` | Cash disbursement |
| `SupplierLedgerEntry` | AP purchases, payments, credits |
| `CashMovement` | Shift drawer movements |
| `Shift` | Open/close, expected/counted cash |
| `Invoice` | Document layer on sales (not revenue source) |

### 13.2 CashExpense fields (required behavior)

| Field | Type | Rules |
|-------|------|-------|
| `TenantId` | Guid | Required, isolated |
| `ExpenseDate` | DateOnly | Period filter field |
| `Category` | string(80) | Catalog category |
| `Description` | string(500) | Expense type (catalog) |
| `Amount` | decimal(12,2) | > 0, rounded 2dp |
| `Status` | posted/void | Default posted on create |
| `PaymentMethod` | varchar(20) | Enum set |
| `Payee` | string | Optional vendor |
| `Note` | string | Optional notes |
| `SourceType` | varchar(40) | `running_cost` manual; `payroll_payment` for payroll |
| `SourceReference` | string | INV/ref |
| `IdempotencyKey` | string | Optional dedup |
| `ShiftId` | Guid? | Required path for cash |
| `RecordedByUserId` | Guid | FK `app_users.Id` |

### 13.3 Relationships (financial)

- `CashExpense.RecordedByUserId` → `AppUser.Id` (not Identity user id directly).
- `CashExpense.ShiftId` → `Shift.Id` (nullable).
- `CashMovement.ReferenceId` may reference `CashExpense.Id` for `paid_out`.
- `PayrollPayment` links `CashExpenseId`, `PayrollLineId`.

### 13.4 DTOs

- `ProfitabilityDto` — full canonical report
- `CashFlowDto` — cash subset + availability
- `DashboardFinancialDto` — mapped from `ProfitabilityDto` + today slice
- `CashExpenseDto` — expense CRUD/list

---

## 14. Source of Truth Matrix

| Metric | Canonical source | Calculation / query | Date field | Non-canonical / legacy (do not use for Owner KPIs) |
|--------|------------------|---------------------|------------|---------------------------------------------------|
| **Revenue** | `ProfitabilityService` | `Sale.Total` − executed refunds − cancellation adjustments | `Sale.CreatedAtUtc`, `Refund.ExecutedAt`, `SaleAdjustment.CreatedAtUtc` | `/api/reports/sales`, analytics snapshots, `Plan.Price` |
| **COGS** | `ProfitabilityService` | Sum `SaleLine.CogsAmount` retail | `Sale.CreatedAtUtc` | Supplier purchases, GRN totals |
| **Gross Profit** | `ProfitabilityService` | Revenue − COGS | Derived | — |
| **Operating Expenses** | `ProfitabilityService` | Posted `CashExpense` excl. payroll | `ExpenseDate` | — |
| **Payroll (accrual)** | `ProfitabilityService` | Sum `PayrollLine.NetSalary` overlapping periods | Period Y/M overlap | CashExpense payroll rows |
| **Net Profit** | `ProfitabilityService` | Gross − OpEx − Payroll | Derived | Dashboard analytics KPIs |
| **Collections** | `ProfitabilityService` | Success payments | `PaidAtUtc` | — |
| **Settled cash inflow** | `ProfitabilityService` | Settled success payments | `PaidAtUtc` | Raw drawer totals |
| **Cash refunds** | `ProfitabilityService` | Executed non-credit refunds | `ExecutedAt` | — |
| **Payroll cash out** | `ProfitabilityService` | `PayrollPayment` | `PaidDate` | — |
| **Supplier cash out** | `ProfitabilityService` | Supplier ledger payments w/ evidence | `EffectiveAtUtc` | — |
| **Cash outflows** | `ProfitabilityService` | Sum components | Mixed | — |
| **Net cash flow** | `ProfitabilityService` | Settled in − outflows | Mixed | Z-Report net |
| **AR** | `ProfitabilityService` | Sum `Sale.AmountDue` snapshot | As-of range end | Member.DebtAmount (does not exist) |
| **AP (unpaid supplier stock)** | `ProfitabilityService` | Sum supplier ledger snapshot | As-of range end | — |
| **Revenue trend** | `ProfitabilityService` | Daily revenue series | Cairo days | Analytics chart job |

**API aliases (canonical):**

- `GET /api/reports/profitability?from=&to=`
- `GET /api/reports/financial-reconciliation?from=&to=` (same payload)
- `GET /api/reports/cash-flow?from=&to=`
- `GET /api/dashboard/overview?period=&from=&to=` (financial section)

---

## 15. Owner Dashboard Requirements

### 15.1 Audience

**Owner role** + `reports.financial.view` → executive layout (`renderFinanceExecutive`).  
**Manager** + financial view → detailed layout (`renderFinanceDetailed`).

### 15.2 Period filters

| Preset | Cairo range |
|--------|-------------|
| Today | Current Cairo day |
| Week | Current week |
| Month | Calendar month to today |
| Last month | Previous calendar month |
| Year | YTD |
| Last year | Previous calendar year (Manager) |
| Custom | `from`/`to` query params |

Period chip for partial month: **"1 Sep so far"** style (`ownerPeriodDisplayLabel`).

### 15.3 Primary hero KPIs (Owner — max 4)

| # | Card | Meaning | Formula / source | Unavailable behavior |
|---|------|---------|------------------|----------------------|
| 1 | **Revenue** | Accrual sales net refunds/cancellations | `ProfitabilityDto.Revenue` | Trust state from `TrustStates` |
| 2 | **Gross profit** | After COGS | `GrossProfit`; margin if COGS available | Hide margin if COGS unavailable |
| 3 | **Cost to run** | Running costs + salaries (UI aggregation) | `OperatingExpenses` + `PayrollExpense` lines | Show "None posted" for empty running costs + link to Reports |
| 4 | **Unpaid supplier stock** | AP snapshot | `AccountsPayable` | Not OpEx — inventory/AP label |

**Removed from Owner hero (by design):** standalone Operating expenses EGP 0, Net profit, Net cash flow (when settlement unavailable).

### 15.4 Secondary sections

1. **Profitability bridge** — Revenue → −COGS → Gross profit → −Running costs → −Payroll → Net profit  
2. **Payroll period warning** — when partial month + payroll COMPLETE  
3. **Cash** — **Collected** (`Collections`); settlement warning if `settledCashAvailable=false`  
4. **Money owed** — AR (`AccountsReceivable`) + unpaid supplier stock  
5. **Revenue trend** — `RevenueTrend` chart  
6. **Financial attention** — `dataIssues` + trust flags  

### 15.5 Cost to run card (presentation rule)

```
Running costs posted = OperatingExpenses
Salaries             = PayrollExpense (if payroll COMPLETE)
Total cost to run    = OperatingExpenses + PayrollExpense (UI sum only — does not change Net Profit formula)
```

Empty running costs: **"None posted"** + CTA to Reports → Running costs.

### 15.6 Drill-down

| KPI | Drill-down target |
|-----|-------------------|
| Revenue | Reports → Profitability / Sales operational |
| Running costs | Reports → Running costs tab |
| Unpaid supplier stock | Suppliers / Invoices Buy |
| Collected | Reports → Cash flow |
| AR | Members with outstanding / debtors API |

### 15.7 Permission gating

- No financial section without `reports.financial.view`.
- `OperatingExpenses`, `NetProfit`, `ProfitMargin` nulled without `reports.expenses.view`.

---

## 16. Expense Management Requirements

### 16.1 Workflows

| Action | API | Permission |
|--------|-----|------------|
| List | `GET /api/expenses?from=&to=` | `reports.expenses.view` |
| Create | `POST /api/expenses` | `reports.expenses.manage` |
| Void | `PATCH /api/expenses/{id}` `{ status: void }` | `reports.expenses.manage` |

Controller role gate: `Owner, Manager`.

### 16.2 Create expense — fields

| Field | Required | Validation |
|-------|----------|------------|
| `expenseDate` | Yes | DateOnly |
| `category` | Yes | `CashExpenseCatalog.IsKnownCategory` |
| `description` (type) | Yes | `IsKnownType(category, description)` |
| `amount` | Yes | > 0 |
| `paymentMethod` | Yes | Valid enum |
| `payee` | No | Max 200 |
| `note` | No | Max 500 |
| `sourceReference` | No | Max 200 |
| `shiftId` | Conditional | Required effective path for cash |
| `idempotencyKey` | No | Unique per tenant when set |

### 16.3 Edit expense

**Current:** PATCH supports limited updates + void.  
**Policy:** Posted cash expenses on closed shifts cannot be moved (service enforces).

**OPEN DECISION:** Full edit of amount/date/category for non-cash posted rows vs void-and-repost only.

### 16.4 Reports UI — Running costs tab

- KPIs: posted total, entry count, voided count  
- Ledger table: date, category, type/description, method, amount, status, void action  
- Form: catalog-driven category/type, payment method, shift auto-resolve for cash  
- Copy: payroll and supplier purchases stay separate  

### 16.5 Audit

- `cash_expense.posted`, `cash_expense.updated` via `AuditService` when configured.
- `RecordedByUserId` + `CreatedAtUtc` on row.

---

## 17. Supplier/Purchasing Financial Treatment

### 17.1 Event chain

```
Purchase Order → Goods Receipt (GRN)
  → SupplierLedgerEntry (+amount, reason=purchase, ref=GoodsReceipt)
  → Increases AP / inventory value
  → Does NOT hit OpEx or COGS at purchase time

Retail Sale
  → SaleLine.CogsAmount at sale time
  → COGS in P&L when sale in range

Supplier Payment
  → SupplierLedgerEntry (−amount, reason=payment)
  → Reduces AP
  → Cash outflow component when evidence linked
  → Does NOT hit OpEx
```

### 17.2 Owner labeling

Dashboard: **Unpaid supplier stock** = `AccountsPayable` snapshot — not "You owe suppliers" mixed into monthly expenses.

### 17.3 Double-count prevention

- GRN never creates `CashExpense`.
- Supplier payment never creates `CashExpense` running cost.

---

## 18. Refund/Discount/Write-off Treatment

### 18.1 Refunds (executed only)

| Impact | Full refund | Partial refund |
|--------|-------------|----------------|
| Revenue | −Refund.Amount | −Refund.Amount |
| Cash flow | −Amount if method cash/gateway | Same |
| COGS | Negate retail COGS if full retail refund | **COGS unavailable** for period |
| Sale status | `refunded` | `partially_refunded` |
| Membership | Cancel covering | No auto-cancel |
| Inventory | Restore stock (retail) | No restore |

Credit refunds: reduce revenue but not `CashRefunds`.

### 18.2 Discounts

- Embedded in `Sale.Total` at sale time (not second revenue subtraction).
- Sales Report shows discount columns informational only.

### 18.3 Sale adjustments

| Type | AR / Sale balance | Revenue |
|------|-------------------|---------|
| `write_off` | Reduces `AmountDue`; may set `written_off` | **No** revenue change |
| `cancellation` | Reduces balance; may set `cancelled` | **Reduces revenue** when posted in range |

### 18.4 Desk cancel membership

- Stops membership; does not refund cash automatically.
- Not the same as financial refund.

### 18.5 Voided sales

- Not a standard operation; cancelled/refunded statuses used instead.

---

## 19. Reporting Requirements

### 19.1 Canonical reports (Owner / Finance)

| Report | Route | Purpose |
|--------|-------|---------|
| Profitability | `/api/reports/profitability` | Full P&L + reconciliation |
| Cash flow | `/api/reports/cash-flow` | Cash in/out |
| Financial reconciliation | `/api/reports/financial-reconciliation` | Alias |
| Running costs | `/api/expenses` | OpEx ledger |
| Dashboard overview | `/api/dashboard/overview` | Owner KPIs + trend |

### 19.2 Operational reports (non-canonical for Revenue/Net Profit)

| Report | Route | Basis | Use |
|--------|-------|-------|-----|
| Sales | `/api/reports/sales` | `PaidAtUtc` collections | Desk reconciliation |
| Refunds | `/api/reports/refunds` | Executed refunds | Operations |
| Memberships | `/api/reports/memberships` | Payment-based activity | Operations |
| Products | `/api/reports/products` | Sale lines | Retail ops |
| Staff-Shifts | `/api/reports/staff-shifts` | Payments by staff/shift | Ops |
| Z-Report | `/api/reports/z/shifts` | Shift close | Cash drawer |

**Rule:** UI must label operational reports as cash/activity-based where they differ from profitability.

### 19.3 Exports

- CSV includes active filters + on-screen KPI totals (existing Reports UX).
- Export respects permission gates.

### 19.4 Expense breakdown report

**Required aggregation (by period):**

- Payroll (from profitability, not CashExpense)
- Utilities, Rent, Software, Operations, Marketing, Banking, Other — from `CashExpense.Category` sums
- Voided excluded

**OPEN DECISION:** Dedicated API `GET /api/reports/expense-breakdown` vs client aggregation from expenses list.

---

## 20. Role & Permission Requirements

| Permission | String | Owner | Manager (default) | Receptionist | Trainer |
|------------|--------|-------|-------------------|--------------|---------|
| Financial view | `reports.financial.view` | ✓ | ✓ | ✗ | ✗ |
| Expenses view | `reports.expenses.view` | ✓ | ✓ | ✗ | ✗ |
| Expenses manage | `reports.expenses.manage` | ✓ | ✓ | ✗ | ✗ |
| HR payroll | `hr.payroll.*` | ✓ | ✓ | ✗ | ✗ |
| Shift open/close | `shift.open`, `shift.close` | ✓ | ✓ | ✓ | ✗ |
| Inventory COGS backfill | `inventory.manage` | ✓ | ✓ | ✗ | ✗ |

**Member:** no staff financial endpoints.

**Sensitive fields:** Net profit, operating expenses, expense ledger hidden without expense view permission even if financial view granted.

---

## 21. UI/UX Requirements

### 21.1 Owner questions (must answer in <30s)

1. How much did we make? → Revenue (accrual)  
2. How much did we spend to run? → Cost to run  
3. What profit after costs? → Profitability bridge / Net profit (when available)  
4. How much cash came in? → Collected  
5. Where did money go? → Running costs ledger + breakdown  
6. What do we still owe / is owed? → AR + Unpaid supplier stock  

### 21.2 Clarity rules

- Never show **Operating expenses EGP 0** as hero when payroll exists — use **Cost to run**.
- Never show **Net cash flow** when `cashFlowAvailable=false`.
- Use **Unavailable** not $0 for missing COGS/Net Profit.
- Trust states: `TRUSTWORTHY`, `CONDITIONALLY_TRUSTWORTHY`, `UNAVAILABLE`.
- No hardcoded KPI values in production bundles.
- Error toasts: user message only (no stack traces).

### 21.3 Components

- KPI cards, profitability bridge, trend chart, breakdown tables, date presets, Cairo labels.
- Reports hub tabs: Profitability, Cash flow, Running costs (+ operational tabs).

### 21.4 Navigation

- Running costs: **Reports → Running costs** (not a separate ledger module).
- Z-Report: **Shifts → Z-Reports** (operational, not P&L).

---

## 22. Backend Requirements

### 22.1 Services (existing — extend only)

| Service | Responsibility |
|---------|----------------|
| `ProfitabilityService` | Canonical P&L, cash flow, AR/AP snapshots |
| `DashboardService` | Period resolution, maps profitability to overview |
| `CashExpenseService` | Running cost CRUD, shift cash linkage |
| `PayrollPeriodService` | Period lifecycle, line calculation |
| `PayrollPaymentService` | Disbursement + linked CashExpense |
| `SaleAdjustmentService` | Write-off / cancellation |
| `RefundService` | Executed refunds |
| `SupplierService` | AP payments |
| `ShiftService` | Drawer movements |
| `SaleService` / `PaymentService` | Sale + payment settlement semantics |
| `AuditService` | Financial audit events |
| `StaffAppUserProvisioner` | Ensure `AppUser` for desk actors |

### 22.2 Domain rules (must enforce)

- Tenant scoping on all reads/writes.
- Catalog validation on expense create.
- Cash expense requires open shift.
- Payroll CashExpense excluded from OpEx aggregation.
- No supplier payment → CashExpense running cost.
- Profitability date validation: `from <= to`.
- Idempotency on expense create when key provided.

### 22.3 Performance

- Cairo range queries indexed on date fields (`ExpenseDate`, `PaidAtUtc`, `CreatedAtUtc`).
- Reports cap operational lists (90 days / 500 rows — existing pattern).

---

## 23. API Requirements

### 23.1 Financial summary

**GET** `/api/dashboard/overview`  
**Auth:** Bearer + tenant (`gym_code` claim / `X-Gym-Code`)  
**Query:** `period=today|week|month|last_month|year|last_year|custom`, `from`, `to`  
**Permission:** Financial section requires `reports.financial.view`  
**Response:** `DashboardOverviewDto` with `financial` block mapped from `ProfitabilityDto`  
**Errors:** 401 unauthorized, 400 invalid period  

### 23.2 Profitability

**GET** `/api/reports/profitability?from=YYYY-MM-DD&to=YYYY-MM-DD`  
**Permission:** `reports.financial.view`  
**Response:** `ProfitabilityDto`  
**Headers:** `X-Financial-Calculation-Version: financial-v1`  
**Errors:** 400 invalid range  

### 23.3 Cash flow

**GET** `/api/reports/cash-flow?from=&to=`  
**Permission:** `reports.financial.view`  
**Response:** `CashFlowDto`  

### 23.4 Financial reconciliation

**GET** `/api/reports/financial-reconciliation?from=&to=`  
**Response:** Same as profitability (alias)  

### 23.5 COGS backfill

**POST** `/api/reports/profitability/backfill-cogs`  
**Permission:** `inventory.manage`  
**Response:** `CogsBackfillDto`  

### 23.6 Expenses

| Method | Route | Permission | Body / response |
|--------|-------|------------|-----------------|
| GET | `/api/expenses?from=&to=` | `reports.expenses.view` | `CashExpenseDto[]` |
| POST | `/api/expenses` | `reports.expenses.manage` | `CreateCashExpenseRequest` → 201 `CashExpenseDto` |
| PATCH | `/api/expenses/{id}` | `reports.expenses.manage` | `UpdateCashExpenseRequest` |

**POST validation failures (400):** invalid category/type, amount ≤ 0, cash without shift, staff profile not found.

### 23.7 Error shape

- `{ error: "message" }` for business failures  
- ProblemDetails for unhandled (production should not leak to UI toast)  

---

## 24. Validation & Business Rules

| Rule ID | Rule |
|---------|------|
| BR-01 | Revenue uses sale created date only |
| BR-02 | Only executed refunds count |
| BR-03 | Cancellation adjustments reduce revenue; write-offs do not |
| BR-04 | COGS null → Gross Profit null → Net Profit null |
| BR-05 | Payroll periods overlap uses full month salary |
| BR-06 | OpEx excludes payroll category/source |
| BR-07 | Cash running cost requires open shift |
| BR-08 | Manual expense SourceType defaults `running_cost` |
| BR-09 | Void excludes from OpEx totals |
| BR-10 | Supplier purchases never OpEx |
| BR-11 | AR snapshot excludes refunded/cancelled sales with due |
| BR-12 | TenantId on all mutations |
| BR-13 | Expense amount rounded half-up 2dp |
| BR-14 | Idempotency key replay returns same expense |
| BR-15 | Net profit hidden without payroll COMPLETE |

---

## 25. Audit & Security Requirements

- JWT auth on all financial endpoints; `TenantMiddleware` resolves gym.
- Permission policies on each action (`HasPermission`).
- `RecordedByUserId` resolved via `AppUser` (provision on login/create if missing).
- Audit events for expense post/update, adjustments, refunds (existing patterns).
- No cross-tenant reads (EF query filters + explicit tenant checks).
- Subscription suspension gate (CP5) blocks staff APIs except check-in allowlist.
- Financial exports require same permissions as view.

---

## 26. Migration & Compatibility

### 26.1 Schema evolution (done / required)

- `CashExpense` structured fields migration (`PaymentMethod`, `ShiftId`, `SourceType`, etc.)
- `SourceType` drift fix: backfill `running_cost`, nullable column (`20260901140704_CashExpenseSourceTypeDriftFix`)
- `SaleAdjustment` for cancellations
- Payroll payment disbursement tables aligned to live schema

### 26.2 Breaking changes

| Change | Impact |
|--------|--------|
| Owner dashboard KPI set | UI only — no API break |
| Expense catalog validation | Legacy free-text categories rejected on create |
| SourceType required at DB | Default `running_cost` in service |

### 26.3 Legacy endpoints

Remain operational:

- `/api/reports/sales`, `/refunds`, `/memberships`, `/products`, `/staff-shifts`
- Z-Report shift APIs
- Analytics overview (non-canonical)

**Documentation requirement:** `FRONTEND_API_CONTRACTS.ts` marks canonical vs operational.

### 26.4 Note field preservation

Existing `CashExpense.Note` retained; form `note` maps to `Note`, catalog type maps to `Description`.

---

## 27. Edge Cases

| Scenario | Expected behavior |
|----------|-------------------|
| No revenue in period | Revenue = 0; margin N/A |
| No expenses | OperatingExpenses = 0; Cost to run shows "None posted" |
| No COGS data | CogsAvailable=false; GP/NP unavailable |
| Negative net profit | Display negative; no clamping |
| Refund > period sales | Revenue can go negative in period |
| Expense after shift closed | Cash expense blocked or shift validation fails |
| Backdated expense | Allowed by `ExpenseDate`; appears in that period OpEx |
| Backdated sale | Revenue in sale created period |
| Cancelled sale | Cancellation adjustment reduces revenue if posted |
| Voided expense | Excluded from totals; row remains |
| Supplier payment w/o GRN | Payment reduces AP; no COGS |
| Payroll calculated not paid | Accrual in PayrollExpense; cash outflow 0 |
| Payroll paid other period | Cash outflow by PaidDate; accrual by period overlap |
| Multiple payment methods | Each payment row separate |
| Partial payment | Revenue unchanged; AR reduced on collect |
| Partial refund | Revenue − refund; COGS unavailable |
| Settlement pending | SettledCashAvailable=false |
| Owner without AppUser | Provision on login/post |
| Duplicate idempotency key | Return existing expense |

---

## 28. Acceptance Criteria

### 28.1 Revenue

```text
Given sales exist in Cairo September 2026
And payments occur in August 2026
When Owner selects period September 2026
Then Revenue reflects Sale.CreatedAtUtc in September only
And Collections may include August-dated payments for September sales
```

### 28.2 Net profit single source

```text
Given Owner dashboard and Reports profitability tab
When same from/to selected
Then NetProfit values match exactly (or both unavailable)
```

### 28.3 Supplier purchases

```text
Given a goods receipt posts supplier ledger purchase
When profitability computed for that period
Then OperatingExpenses do not include purchase amount
And AccountsPayable increases
```

### 28.4 Running cost create

```text
Given Manager with reports.expenses.manage
When POST /api/expenses with catalog category/type and bank_transfer
Then expense status=posted
And SourceType=running_cost
And OperatingExpenses includes amount in ExpenseDate period
```

### 28.5 Payroll separation

```text
Given approved September payroll period
When OpEx queried for September
Then payroll CashExpense rows are excluded
And PayrollExpense includes September NetSalary total
```

### 28.6 Cash running cost

```text
Given no open shift
When user submits cash running cost
Then API returns 400 requiring open shift
And UI shows open-shift message before POST
```

### 28.7 COGS unavailable

```text
Given retail sale lines without CogsAmount
When profitability loaded
Then CogsAvailable=false
And UI shows COGS unavailable not EGP 0
```

### 28.8 Cash flow gating

```text
Given settlement_data_incomplete in dataIssues
When Owner dashboard loads
Then Net cash flow card hidden
And Collected still visible
```

### 28.9 Void expense

```text
Given posted running cost
When PATCH status=void
Then subsequent OpEx totals exclude amount
And audit row preserved
```

### 28.10 Sales report non-canonical

```text
Given Sales report cash-in differs from Profitability revenue
When documented
Then Owner KPIs use Profitability only
```

---

## 29. Testing Strategy

### 29.1 Backend (xUnit)

| Suite | Coverage |
|-------|----------|
| `CashExpenseServiceTests` | Catalog, void, OpEx integration, cash shift rule, SourceType default |
| `ProfitabilityServiceTests` / financial integration | Revenue, refunds, COGS null, payroll overlap |
| `SaleAdjustmentServiceTests` | Cancellation vs write-off |
| `FinancialReportingApiIntegrationTests` | HTTP profitability + dashboard parity |
| Tenant isolation regression | Cross-tenant leakage |

### 29.2 Frontend (node selftests)

- `dashboard-home.selftest.js` — Owner Cost to run, bridge, labels  
- `reports.financial.selftest.js` — profitability/cash-flow availability, expense form  

### 29.3 QA manual scenarios

1. Owner month view day 1 — payroll warning visible  
2. Post electricity bank transfer — appears in OpEx, not cash drawer  
3. Post cash expense with open shift — drawer paid_out movement  
4. GRN then pay supplier — AP down, not OpEx up  
5. Retail sale with COGS snapshot — GP available  
6. Partial retail refund — COGS unavailable  
7. Manager without expense view — no net profit on dashboard  

### 29.4 Regression guards

- Assert `calculationVersion === financial-v1` on API responses  
- Assert Sales tab ≠ Profitability revenue in test tenant with timing mismatch  

---

## 30. Implementation Plan

### Phase 0 — Complete (current codebase)

- [x] `ProfitabilityService` financial-v1  
- [x] Dashboard maps profitability (not analytics)  
- [x] CashExpense catalog + Running costs UI  
- [x] Owner executive dashboard (Cost to run, bridge, AP label)  
- [x] SourceType default + migration  
- [x] Staff AppUser provisioning  
- [x] Sale adjustments (cancellation)  

### Phase 1 — Hardening (recommended next)

1. Fix supplier payment `ReferenceType=CashMovement` for cash flow availability  
2. Align `OperatingExpenseCashOutflows` naming vs payment-method filter (**OPEN DECISION**)  
3. Expense breakdown API or documented client aggregation  
4. Renewals bucket in revenue breakdown  
5. Payroll cash → drawer movement (**OPEN DECISION**)  

### Phase 2 — Owner analytics depth

1. Expense breakdown chart on dashboard  
2. YoY / prior-period comparison chips  
3. Export profitability PDF  

### Phase 3 — OPEN decisions resolution

- Payroll proration policy  
- Full expense edit vs void-only  
- Multi-location allocation (out of scope until product defines branches)  

---

## 31. Open Decisions / Risks

| ID | Topic | Options | Recommendation | Risk if deferred |
|----|-------|---------|----------------|------------------|
| OD-01 | Payroll proration | (A) Full month overlap (current) (B) Prorate by days | Keep (A) until owner requests; UI warning mandatory | Owner confusion on partial months |
| OD-02 | OpEx in cash flow | (A) All OpEx by expense date (current) (B) Cash-method OpEx only | Document (A) honestly in UI labels; consider (B) later | Mislabeled "cash outflow" |
| OD-03 | Supplier payment evidence | Fix posting to set CashMovement ref | **Implement Phase 1** | Cash flow permanently unavailable |
| OD-04 | Payroll cash drawer | Link disbursement to CashMovement | Defer unless Z-Report must include payroll cash | Drawer reconciliation gap |
| OD-05 | Expense edit policy | Void+repost only vs full PATCH | Void+repost for v1 | User error correction friction |
| OD-06 | Renewals revenue bucket | Split in `ProfitabilityService` | Low priority | Reporting granularity |
| OD-07 | Write-off revenue | Keep AR-only (current) | Document for support | Owner expects revenue impact |
| OD-08 | Settlement retry inconsistency | Audit `PaymentService` pending vs settled | Engineering hygiene | Wrong cash availability |
| OD-09 | Analytics legacy endpoints | Deprecate in UI | Keep APIs, hide from Owner | Stray integrations |
| OD-10 | Multi-currency | Single currency per tenant | N/A for Egypt v1 | Expansion blocker |

---

## Appendix A — `dataIssues` codes

| Code | Meaning |
|------|---------|
| `settlement_data_incomplete` | Payments in range lack settled evidence |
| `payment_allocation_mismatch` | Sale balance ≠ payments + adjustments |
| `supplier_cash_evidence_unavailable` | Supplier payments missing CashMovement ref |
| `cogs_unavailable` | Missing line COGS snapshots |
| `retail_refund_cogs_unavailable` | Partial retail refund in range |
| `no_payroll_period` | No overlapping approved/closed period |
| `payroll_data_incomplete` | Payroll lines incomplete |
| `no_retail_lines` | Informational |

---

## Appendix B — File reference (implementation)

| Area | Path |
|------|------|
| Profitability | `GMS.Application/Services/ProfitabilityService.cs` |
| Dashboard | `GMS.Application/Services/DashboardService.cs` |
| Expenses | `GMS.Application/Services/CashExpenseService.cs` |
| Catalog | `GMS.Core/Constants/CashExpenseCatalog.cs` |
| Reports API | `GMS.Api/Controllers/ReportsController.cs` |
| Expenses API | `GMS.Api/Controllers/CashExpensesController.cs` |
| Owner UI | `Frontend/apps/web/src/app/(dashboard)/dashboard-home.js` |
| Reports UI | `Frontend/apps/web/src/app/(dashboard)/reports/reports-app.js` |
| API contracts | `docs/api/FRONTEND_API_CONTRACTS.ts` |

---

**Document approval:** Product / Engineering sign-off required before Phase 1 scope changes to canonical formulas.
