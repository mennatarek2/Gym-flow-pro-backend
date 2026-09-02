# Financial remediation baseline

This document records the implementation baseline for the financial remediation
work. It is intentionally descriptive; it does not redefine historical rows or
silently convert payment events into revenue.

## Canonical sources

- `Sales` and `SaleLines`: commercial sale facts.
- `PaymentTransactions`: payment events and collection evidence.
- `Refunds`: sale/payment reversals; `credit` refunds create member credit.
- `CashExpenses`: posted operating-expense recognition ledger.
- `GoodsReceipts`, `ProductBatches`, and `StockMovements`: inventory and cost evidence.
- `SupplierLedgerEntries`: supplier/AP obligations and settlements.
- `PayrollPeriods` and `PayrollLines`: calculated payroll liability.

## Current implementation boundary

The first canonical reporting slice exposes collections, settled cash evidence,
revenue basis, refunds by method, sale-line COGS coverage, posted operating
expenses, approved payroll, supplier AP, member receivables, and net cash flow.
Profit is marked unavailable when a required cost source has incomplete coverage.

The running LocalDB was previously observed to contain profitability-related
columns/migrations and `payroll_payments` that are not present in the restored
source branch. Any migration must therefore be generated only after comparing
source model, migration history, and the target database.

## Non-negotiable separation

- Payment is not Revenue.
- Supplier purchases are inventory/AP, not operating expenses or COGS.
- Payroll calculation is not payroll disbursement.
- Cash flow is not profitability.
- A missing financial source is reported as unavailable, never as a trusted zero.
