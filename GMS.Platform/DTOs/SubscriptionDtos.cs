namespace GMS.Platform.DTOs;

public class SubscriptionStatusDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string PlanTier { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string BillingCycle { get; set; } = string.Empty;
    public decimal PriceEgp { get; set; }
    public DateOnly CurrentPeriodStart { get; set; }
    public DateOnly CurrentPeriodEnd { get; set; }
    public DateTime? TrialEndsAtUtc { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public DateTime? SuspendedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Pending downgrade tier if scheduled for period end; null otherwise.</summary>
    public string? PendingDowngradeTier { get; set; }

    /// <summary>True when a saved card token exists on the subscription row (Paymob/Fawry on file).</summary>
    public bool HasPaymentMethodOnFile { get; set; }
}

public class ConvertTrialRequest
{
    /// <summary>Mandatory audit reason for sales-assisted conversion.</summary>
    public string Reason { get; set; } = string.Empty;
}

public class RestartPaidRequest
{
    /// <summary>Plan tier for the new active subscription (current list price is frozen into PriceEgp).</summary>
    public string Tier { get; set; } = string.Empty;

    /// <summary>Mandatory audit reason.</summary>
    public string Reason { get; set; } = string.Empty;
}

public class ChangeTierRequest
{
    public string NewTier { get; set; } = string.Empty;
    /// <summary>Upgrades ignore this (always immediate). Downgrades: true = apply now (ops override), false = period end.</summary>
    public bool EffectiveNow { get; set; }
    public string? Reason { get; set; }
}

public class CancelSubscriptionRequest
{
    /// <summary>false = cancel_at_period_end; true = immediate cancel (platform_admin + mandatory reason).</summary>
    public bool Immediate { get; set; }
    public string? Reason { get; set; }
}

public class UndoCancelSubscriptionRequest
{
    /// <summary>Mandatory audit reason (same pattern as immediate cancel / force ops).</summary>
    public string Reason { get; set; } = string.Empty;
}

public class SubscriptionMutationResult
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public SubscriptionStatusDto? Subscription { get; set; }

    public static SubscriptionMutationResult Ok(SubscriptionStatusDto dto) => new()
    {
        Success = true,
        Subscription = dto
    };

    public static SubscriptionMutationResult Fail(string code, string message) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorMessage = message
    };
}
