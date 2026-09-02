# Release QA pack — Final Role Assignment (QA-01 … QA-06)

Executable checklist for this cycle. Unit isolation tests already exist; this pack covers regression / E2E gates.

| ID | Title | Precondition | Pass criteria |
| -- | ---- | ------------ | ------------- |
| QA-01 | Member Orders isolation + IDOR + empty list | FL-02 shipped in Member App | Member A ≠ Member B; unauth 401; empty list ≠ all gym orders; detail IDOR → 404 |
| QA-02 | Staff inbox vs Member 360 | W-01 verified | `/member-orders` without `memberId` = all; Member 360 with `memberId` = one member only |
| QA-03 | Partial pay + due date + collect later | R-02 path live | Add Member underpay + due date succeeds; Collect Payment on 360 clears/reduces AmountDue |
| QA-04 | Classes read-only + seats | FL-01 shipped | `GET /member/classes` creates 0 bookings/payments; seats = capacity − booked/checked_in |
| QA-05 | OpEx / payroll / Net gate | Finance live | OpEx excludes payroll; payroll warning on partial Owner month; Net unavailable until COMPLETE periods |
| QA-06 | Cross-tenant smoke | Two tenants | Tenant A never sees B orders, expenses, or classes |

## Automation hooks (staff web)

```bash
node apps/web/src/app/(dashboard)/members/[id]/member-detail.selftest.js
node apps/web/src/app/(dashboard)/dashboard-home.selftest.js
```

Backend (from API solution):

```bash
dotnet test --filter "FullyQualifiedName~MemberOrderIsolation|FullyQualifiedName~MemberClass"
```

## Sign-off

| ID | Tester | Date | Result | Notes |
| -- | ------ | ---- | ------ | ----- |
| QA-01 | | | | |
| QA-02 | | | | |
| QA-03 | | | | |
| QA-04 | | | | |
| QA-05 | | | | |
| QA-06 | | | | |

## Live local evidence (2026-09-02)

Run: `node docs/qa/live-smoke.mjs` (API on `https://localhost:5001`, `owner@gymflow.test` / `GYM-TEST-01`).

Artifact: [LIVE_SMOKE_EVIDENCE.json](./LIVE_SMOKE_EVIDENCE.json)

| ID | Result |
| -- | ------ |
| QA-02 | PASS � inbox 11; filtered 10 + 1; no foreign rows |
| QA-03 | PASS � outstanding sales endpoint returned 3 AmountDue sales |
| QA-05 | PASS � financial-v1 OpEx 15000 vs payroll 12500 |
| QA-01 / QA-04 | Still need Member App E2E after FL-01/FL-02 |
