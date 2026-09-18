namespace GMS.Platform.Entities;

/// <summary>
/// Operator-assisted Local Owner password recovery. Platform never writes HyMotionLocal SQL and
/// never stores a password. Approval is a short-lived signed blob bound to gym + installation +
/// request nonce — a recovery request is not itself proof of ownership.
/// </summary>
public class LocalOwnerRecoveryRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LicenseId { get; set; }
    public Guid? CustomerId { get; set; }
    public string InstallationId { get; set; } = string.Empty;
    public string GymCode { get; set; } = string.Empty;
    public string? GymName { get; set; }
    public string Nonce { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string Method { get; set; } = "online";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? DecisionByPlatformUserId { get; set; }
    public string? DecisionReason { get; set; }
    public Guid? ApprovalJti { get; set; }
    /// <summary>Signed HYMA1 blob. Returned only to the matching installation on poll and to
    /// Ops+ after approve. Never written to audit logs.</summary>
    public string? ApprovalBlob { get; set; }
    public string HistoryJson { get; set; } = "[]";

    public LocalLicense? License { get; set; }
}
