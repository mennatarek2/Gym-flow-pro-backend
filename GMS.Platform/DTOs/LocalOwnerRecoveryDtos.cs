namespace GMS.Platform.DTOs;

public class LocalOwnerRecoveryListItemDto
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public Guid? CustomerId { get; set; }
    public string InstallationId { get; set; } = string.Empty;
    public string GymCode { get; set; } = string.Empty;
    public string? GymName { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string Reference { get; set; } = string.Empty;
}

public class LocalOwnerRecoveryDetailDto : LocalOwnerRecoveryListItemDto
{
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? DecisionByPlatformUserId { get; set; }
    public string? DecisionReason { get; set; }
    public IReadOnlyList<LocalOwnerRecoveryHistoryItemDto> History { get; set; } = [];
    /// <summary>Present only for Ops+ while status is approved. Never logged.</summary>
    public string? RecoveryCode { get; set; }
}

public class LocalOwnerRecoveryHistoryItemDto
{
    public DateTime AtUtc { get; set; }
    public string Event { get; set; } = string.Empty;
    public string? Actor { get; set; }
    public string? Outcome { get; set; }
    public string? Reason { get; set; }
}

public class LocalOwnerRecoveryAuthRequest
{
    public string LicenseKey { get; set; } = string.Empty;
    public string InstallationId { get; set; } = string.Empty;
    public Guid? RequestId { get; set; }
    public string? GymCode { get; set; }
    public string? GymName { get; set; }
    public string? Nonce { get; set; }
    public Guid? Jti { get; set; }
}

public class LocalOwnerRecoveryClientDto
{
    public Guid RequestId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string? RecoveryCode { get; set; }
    public string? Message { get; set; }
}

public class ImportOwnerRecoveryChallengeRequest
{
    public string Challenge { get; set; } = string.Empty;
    public Guid? CustomerId { get; set; }
}

public class OwnerRecoveryDecisionRequest
{
    public string Reason { get; set; } = string.Empty;
    public string? Method { get; set; }
}
