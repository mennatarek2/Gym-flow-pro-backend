namespace GMS.Core.Constants;

/// <summary>
/// Documents the known keys inside <see cref="Entities.Tenant.Settings"/> (a raw JSON blob —
/// this is not a strongly-typed schema, just the agreed key names/types/defaults so callers stay
/// consistent). A key missing from the JSON should be treated as its documented default.
/// </summary>
public static class TenantSettingsKeys
{
    /// <summary>bool, default false</summary>
    public const string RequirePaperWaiver = "require_paper_waiver";

    /// <summary>bool, default false</summary>
    public const string VatEnabled = "vat_enabled";

    /// <summary>decimal, default 0.14</summary>
    public const string VatRate = "vat_rate";

    /// <summary>decimal, default 20</summary>
    public const string VarianceToleranceEgp = "variance_tolerance_egp";

    /// <summary>bool, default true</summary>
    public const string AllowDowngrades = "allow_downgrades";

    /// <summary>bool, default true</summary>
    public const string AllowMidcycleChanges = "allow_midcycle_changes";

    /// <summary>decimal, default 0</summary>
    public const string TransferFeeEgp = "transfer_fee_egp";

    /// <summary>bool, default false</summary>
    public const string InvoicePerPayment = "invoice_per_payment";

    /// <summary>string, default null</summary>
    public const string TaxRegistrationNumber = "tax_registration_number";

    /// <summary>string, default null</summary>
    public const string InvoiceFooterText = "invoice_footer_text";

    /// <summary>string, default null</summary>
    public const string InvoiceFooterTextAr = "invoice_footer_text_ar";

    /// <summary>decimal, default null = unlimited (no manager approval ever required for paid_out)</summary>
    public const string PaidOutApprovalThresholdEgp = "paid_out_approval_threshold_egp";

    /// <summary>
    /// decimal, default 0 — minimum paid sale amount (EGP) required before a pending referral
    /// invitation is marked converted. Trials/day_pass never convert regardless of this value.
    /// </summary>
    public const string ReferralMinSaleAmountEgp = "referral_min_sale_amount_egp";

    /// <summary>int, default 14 — fraud hold days before pending_hold rewards are granted.</summary>
    public const string ReferralHoldDays = "referral_hold_days";

    /// <summary>
    /// int, default 10 — max rewarded referrals (pending_hold+granted) per referrer per Cairo month.
    /// </summary>
    public const string ReferralMonthlyRewardedCap = "referral_monthly_rewarded_cap";

    /// <summary>
    /// decimal, default 1500 — plan price threshold; at/above → default free_days, below → credit
    /// when plan.ReferralRewardType is unset.
    /// </summary>
    public const string ReferralRewardPriceThresholdEgp = "referral_reward_price_threshold_egp";

    /// <summary>decimal, default 50 — default credit EGP when using threshold default.</summary>
    public const string ReferralDefaultCreditEgp = "referral_default_credit_egp";

    /// <summary>int, default 7 — default free days when using threshold default.</summary>
    public const string ReferralDefaultFreeDays = "referral_default_free_days";

    /// <summary>
    /// decimal, default 1.5 — multiplies reward value when the converting plan PlanType is
    /// <c>family</c> (INV-5). Values ≤1 leave the standard reward unchanged.
    /// </summary>
    public const string ReferralFamilyRewardMultiplier = "referral_family_reward_multiplier";

    /// <summary>
    /// string[] JSON, default ["Owner","Manager"] — AppUser.Role values notified by
    /// the daily inventory low-stock / expiry Hangfire job (INVS-10).
    /// </summary>
    public const string InventoryLowStockNotifyRoles = "inventory_low_stock_notify_roles";

    /// <summary>
    /// int[] JSON, default [90,30,7] — expiry alert windows in days (INVS-10).
    /// </summary>
    public const string InventoryExpiryWindowsDays = "inventory_expiry_windows_days";

    /// <summary>string, default null — optional short display name (Gym Identity Phase A).</summary>
    public const string ShortName = "short_name";

    /// <summary>string, default null — optional website URL.</summary>
    public const string Website = "website";

    /// <summary>string hex #RRGGBB, default #7ACC00 — desk/UI primary brand color.</summary>
    public const string BrandPrimaryColor = "brand_primary_color";

    /// <summary>string hex #RRGGBB, default #148F8F.</summary>
    public const string BrandSecondaryColor = "brand_secondary_color";

    /// <summary>string hex #RRGGBB, default #A0E040.</summary>
    public const string BrandAccentColor = "brand_accent_color";

    /// <summary>string hex #RRGGBB, default same as primary — access-card mark / accent.</summary>
    public const string CardPrimaryColor = "card_primary_color";

    /// <summary>bool, default true — show gym logo on access card when LogoUrl is set.</summary>
    public const string CardShowGymLogo = "card_show_gym_logo";

    /// <summary>
    /// object JSON, default unset — gym-wide dashboard Quick Actions.
    /// Shape: { "keys": ["new_member", "checkin", ...] }. Missing/null → FE/API default four keys.
    /// Persisted empty array is intentional (owner cleared all shortcuts).
    /// </summary>
    public const string QuickActions = "quick_actions";

    /// <summary>
    /// int, default unset — maximum members allowed inside this gym at once.
    /// Missing/null → capacity not configured (occupancy UI must not invent a number).
    /// </summary>
    public const string GymMaxCapacity = "gym_max_capacity";

    /// <summary>
    /// object JSON, default unset — per-gym overlay of Identity role → permission keys.
    /// Shape: { "Manager": ["members.view", ...], "Receptionist": [...], "Trainer": [...] }.
    /// Missing role key → DefaultPermissionProvider. Owner and Member keys are ignored.
    /// Unknown permission strings are dropped. Does not change OwnerOnly / AnyStaff role policies.
    /// </summary>
    public const string RolePermissions = "role_permissions";

    /// <summary>int, default 2. Hours before session start after which a member cancellation is "late" (no quota refund).</summary>
    public const string LateCancellationHours = "late_cancellation_hours";

    /// <summary>int, default 30. Rolling window (days ahead) for generating class sessions from recurring schedules.</summary>
    public const string SessionGenerationDays = "session_generation_days";

    /// <summary>int, default 30 — no-check-in window used by dashboard inactive-member counts.</summary>
    public const string DashboardInactivityDays = "dashboard_inactivity_days";
}
