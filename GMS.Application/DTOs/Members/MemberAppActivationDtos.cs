namespace GMS.Application.DTOs.Members;

public class MemberAppActivationCodeResponse
{
    public Guid MemberId { get; set; }
    public string ActivationCode { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public int ExpiresInMinutes { get; set; }
}

/// <summary>Safe status for Member Profile — never includes plaintext code.</summary>
public class MemberAppAccessStatusDto
{
    /// <summary>not_activated | pending_code | activated</summary>
    public string Status { get; set; } = "not_activated";
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? PendingCodeExpiresAtUtc { get; set; }
    public bool HasLinkedAppUser { get; set; }
}
