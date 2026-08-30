namespace GMS.Platform.DTOs;

// --- List / detail expansions (CP6) ---

public class PlatformTenantListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GymCode { get; set; } = string.Empty;
    public string? PlanTier { get; set; }
    public string? Status { get; set; }
    public string? BillingCycle { get; set; }
    public DateOnly? CurrentPeriodStart { get; set; }
    public DateOnly? CurrentPeriodEnd { get; set; }
    /// <summary>Trial end timestamp from the live subscription row (trialing only).</summary>
    public DateTime? TrialEndsAtUtc { get; set; }
    public decimal? PriceEgp { get; set; }
    /// <summary>CP7 seam — null until health scores are populated.</summary>
    public string? RiskBand { get; set; }
    public int? HealthScore { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    /// <summary>
    /// P2.1 — the tenant's active Owner-role account (dbo.AspNetUsers joined to the "Owner" role,
    /// same relationship PlatformImpersonationService already uses to pick who to impersonate).
    /// Null when the tenant has no active Owner account.
    /// </summary>
    public string? OwnerName { get; set; }
    public string? OwnerEmail { get; set; }
    /// <summary>
    /// P2.1 — the tenant's active_members usage counter for the current Cairo billing period (the
    /// same monthly rollup already shown on the tenant's own Usage tab) — not a live COUNT(*) against
    /// tenant member tables. Null until the rollup job has produced a row for this tenant/period.
    /// </summary>
    public int? MemberCount { get; set; }
    /// <summary>Null means unlimited on the tenant's current tier.</summary>
    public int? MemberCap { get; set; }
}

public class PlatformTenantDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string GymCode { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public SubscriptionStatusDto? Subscription { get; set; }
    public List<SubscriptionChangeDto> SubscriptionChanges { get; set; } = new();
    public List<PlatformInvoiceDto> Invoices { get; set; } = new();
    public List<UsageCounterDto> UsageCounters { get; set; } = new();
    public TenantHealthScoreDto? Health { get; set; }
    public List<FeatureOverrideDto> FeatureOverrides { get; set; } = new();
    public List<PriceOverrideDto> PriceOverrides { get; set; } = new();
    public List<PlatformAuditLogDto> RecentAudit { get; set; } = new();
    public DateTime? LastLoginAtUtc { get; set; }
    /// <summary>Staff (non-Member) login accounts for this tenant — who a platform admin would see
    /// when checking who has access, independent of the active_members usage counter.</summary>
    public List<PlatformTenantUserDto> Users { get; set; } = new();
}

public class PlatformTenantUserDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Role { get; set; }
    public bool IsActive { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public class UsageCounterDto
{
    public string Period { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public int Count { get; set; }
    public int? Cap { get; set; }
    public decimal? OverageBilledEgp { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class TenantHealthScoreDto
{
    public string RiskBand { get; set; } = string.Empty;
    public int Score { get; set; }
    /// <summary>Detailed contributing_factors JSON (rules_v1).</summary>
    public string? ContributingFactorsJson { get; set; }
    /// <summary>Alias for older Stage-1 clients — same as ContributingFactorsJson.</summary>
    public string? BreakdownJson => ContributingFactorsJson;
    public DateTime ComputedAtUtc { get; set; }
    /// <summary>Alias for older Stage-1 clients.</summary>
    public DateTime UpdatedAtUtc => ComputedAtUtc;
    public Guid? AssignedPlatformUserId { get; set; }
    public decimal? Confidence { get; set; }
    public string? Summary { get; set; }
}

public class FeatureOverrideDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string FeatureKey { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid GrantedByPlatformUserId { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class PriceOverrideDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string DiscountType { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid GrantedByPlatformUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool IsActive => ExpiresAtUtc > DateTime.UtcNow;
}

public class PlatformAuditLogDto
{
    public Guid Id { get; set; }
    public Guid ActorPlatformUserId { get; set; }
    /// <summary>Display name of the platform admin (joined at read time).</summary>
    public string? ActorName { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    /// <summary>Populated only by the global audit feed (GET /platform-api/audit) — null on the
    /// per-tenant embedded RecentAudit, where the caller is already on that tenant's page.</summary>
    public string? TenantName { get; set; }
    public string? GymCode { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class PlatformPagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}

public class SubscriptionChangeDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SubscriptionId { get; set; }
    public string ChangeType { get; set; } = string.Empty;
    public string? FromTier { get; set; }
    public string? ToTier { get; set; }
    public DateTime EffectiveAtUtc { get; set; }
    public decimal? ProratedAmountEgp { get; set; }
    public string InitiatedBy { get; set; } = string.Empty;
    public Guid? PlatformAdminUserId { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

// --- Action requests ---

public class CreateCouponRequest
{
    public string DiscountType { get; set; } = "percent"; // percent | fixed
    public decimal Value { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class ExtendTrialRequest
{
    public int Days { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Ops repair: start a Growth (or chosen) trial for a tenant with no live subscription.</summary>
public class StartTrialRequest
{
    /// <summary>Optional; defaults to growth.</summary>
    public string? Tier { get; set; }

    /// <summary>Optional trial length in days (1–90). Omitted → global default (14).</summary>
    public int? TrialDays { get; set; }
}

public class ForceSuspendRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class ForceReactivateRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class UpsertFeatureOverrideRequest
{
    public string FeatureKey { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime? ExpiresAtUtc { get; set; }
}

public class ImpersonationResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public Guid TenantId { get; set; }
    public string GymCode { get; set; } = string.Empty;
    public Guid ImpersonatedUserId { get; set; }
    public string ImpersonatedEmail { get; set; } = string.Empty;
    /// <summary>Always false — impersonation tokens cannot be refreshed.</summary>
    public bool RefreshAllowed => false;
}

public class ImpersonateRequest
{
    /// <summary>Mandatory audit reason — why support is entering this tenant.</summary>
    public string Reason { get; set; } = string.Empty;
}

public class PlatformActionResult
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    public static PlatformActionResult Ok() => new() { Success = true };

    public static PlatformActionResult Fail(string code, string message) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorMessage = message
    };
}
