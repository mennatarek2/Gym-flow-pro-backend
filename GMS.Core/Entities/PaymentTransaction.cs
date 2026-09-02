namespace GMS.Core.Entities;

/// <summary>
/// Represents a completed payment transaction.
/// Used for idempotency checks and audit trail.
/// </summary>
public class PaymentTransaction : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid? MemberId { get; set; }
    /// <summary>Null for retail-only (no membership) sales (INVS-6).</summary>
    public Guid? MembershipId { get; set; }

    /// <summary>Payment gateway name: "paymob", "fawry", "cash".</summary>
    public string Gateway { get; set; } = string.Empty;

    /// <summary>External reference from the payment gateway (idempotency key).</summary>
    public string ExternalRef { get; set; } = string.Empty;

    /// <summary>Amount paid in EGP.</summary>
    public decimal Amount { get; set; }

    /// <summary>Currency code.</summary>
    public string Currency { get; set; } = "EGP";

    /// <summary>Payment lifecycle: pending, success, failed, reversed, refunded.</summary>
    public string Status { get; set; } = "success";

    /// <summary>
    /// Settlement lifecycle: unknown, pending, settled, failed, reversed.
    /// A successful payment is not necessarily settled cash.
    /// </summary>
    public string SettlementStatus { get; set; } = "unknown";
    public DateTime? SettledAtUtc { get; set; }

    /// <summary>Raw webhook payload (JSON) for audit.</summary>
    public string? RawPayload { get; set; }

    /// <summary>HMAC verification passed.</summary>
    public bool HmacVerified { get; set; }

    public DateTime PaidAtUtc { get; set; }

    /// <summary>FK to sales.Id — constraint deferred to the P5 migration.</summary>
    public Guid? SaleId { get; set; }

    /// <summary>Staff member (app_users.Id) who received/recorded this payment.</summary>
    public Guid? ReceivedByUserId { get; set; }

    public Guid? ShiftId { get; set; }

    /// <summary>Specific payment method: cash|card_paymob|fawry|vodafone|instapay|account_credit. Distinct from <see cref="Gateway"/>.</summary>
    public string? Method { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public GymMember? Member { get; set; }
    public Membership? Membership { get; set; }
    public AppUser? ReceivedByUser { get; set; }
    public Shift? Shift { get; set; }
}
