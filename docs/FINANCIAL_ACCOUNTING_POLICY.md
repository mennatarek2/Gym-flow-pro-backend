# GymFlow Pro financial reporting policy

These definitions are the reporting contract used by `ProfitabilityService` and
the Owner Dashboard.

- Revenue is sale-created accrual-style revenue:
  `Sales.Total - executed refunds - posted cancellation adjustments`.
- Deferred revenue is not implemented. Membership revenue is not deferred or
  prorated by service period.
- Collections are successful `PaymentTransaction` events. Collections are not
  settled cash.
- Settled cash requires trusted settlement evidence. Payment success alone does
  not prove settlement; historical `unknown` settlement remains unavailable.
- Payroll expense is not inferred when an approved or closed payroll period
  does not exist. `NO_PAYROLL_PERIOD` makes payroll-dependent net profit
  unavailable.
- Supplier ledger reductions are not cash-flow evidence unless the supplier
  payment is linked to trusted `CashMovement` evidence.
- Cash flow is available only when the required settled inflow and cash-outflow
  evidence is complete.

Owner Dashboard financial states are explicit:

`TRUSTWORTHY`, `CONDITIONALLY_TRUSTWORTHY`, `UNAVAILABLE`, and
`REQUIRES_RECONCILIATION`.

Legacy Reports and Z-Report endpoints remain available for operational or
historical report consumers. They are not financial sources for the Owner
Dashboard.
