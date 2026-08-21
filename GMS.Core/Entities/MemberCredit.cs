namespace GMS.Core.Entities;

/// <summary>
/// An append-only ledger entry for a member's account credit balance. Never soft-deleted — the
/// balance is always SUM(Amount) over every row for the member, so a correction is made by adding a
/// new 'adjustment' entry, not by editing or removing a prior one.
/// </summary>
public class MemberCredit : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid MemberId { get; set; }

    /// <summary>Signed: positive = credit added (refund), negative = credit spent (payment_use).</summary>
    public decimal Amount { get; set; }

    /// <summary>'refund' | 'payment_use' | 'adjustment' | 'referral_reward'</summary>
    public string EntryType { get; set; } = string.Empty;

    /// <summary>Polymorphic pointer (e.g. Refund.Id, Sale.Id) — no FK constraint.</summary>
    public Guid? ReferenceId { get; set; }

    public string? Reason { get; set; }

    public Guid CreatedByUserId { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public GymMember? Member { get; set; }
    public AppUser? CreatedByUser { get; set; }
}
