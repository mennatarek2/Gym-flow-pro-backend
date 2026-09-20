-- D3 proposal — NOT an EF migration. Do not apply until duplicates are cleaned and this SQL is approved.
-- Goal: optional 0..1 Customer.TenantId (nullable unique). Local-only customers keep TenantId NULL.
-- Current schema: IX_customers_TenantId is a non-unique index; duplicates are possible.
-- Cleanup BEFORE this index (inspect first):
--   1. List duplicate TenantId groups (query in the Phase 2.1 report).
--   2. Keep one customer per TenantId (prefer the row Ops confirms, else newest UpdatedAtUtc).
--   3. SET TenantId = NULL on the extras (licenses/tickets stay on those customer rows).
--   4. Confirm the duplicate query returns zero rows.
-- Rollback: DROP INDEX [UX_customers_TenantId] ON [platform].[customers];
--           CREATE INDEX [IX_customers_TenantId] ON [platform].[customers] ([TenantId]);

CREATE UNIQUE INDEX [UX_customers_TenantId]
    ON [platform].[customers] ([TenantId])
    WHERE [TenantId] IS NOT NULL;
