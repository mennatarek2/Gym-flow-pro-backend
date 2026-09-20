-- D1 proposal — NOT an EF migration. Do not apply until explicitly approved.
-- Impact: allows persisting platform_sales on platform.platform_admin_users.Role.
-- JWT/policies already know platform_sales; only the SQL CHECK currently rejects the row.
-- Existing rows are support/ops/admin only, so this is additive.
-- Rollback: DROP the new constraint and recreate the previous three-value CHECK
-- (fails if any platform_sales row exists — delete or recast those users first).

ALTER TABLE [platform].[platform_admin_users]
    DROP CONSTRAINT [CK_platform_admin_users_role];

ALTER TABLE [platform].[platform_admin_users]
    ADD CONSTRAINT [CK_platform_admin_users_role]
    CHECK ([Role] IN ('platform_support','platform_ops','platform_admin','platform_sales'));
