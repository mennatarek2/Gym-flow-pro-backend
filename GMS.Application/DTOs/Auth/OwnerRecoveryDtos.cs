namespace GMS.Application.DTOs.Auth;

public class OwnerRecoveryContextResponse
{
    public bool Available { get; set; }
    public string? GymCode { get; set; }
    public string? GymName { get; set; }
    public bool HasOwner { get; set; }
    public bool HasLicense { get; set; }
}

public class OwnerRecoveryStatusResponse
{
    public Guid? RequestId { get; set; }
    public string Status { get; set; } = "none";
    public string? Method { get; set; }
    public string? Reference { get; set; }
    public string? GymCode { get; set; }
    public string? GymName { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public bool OnlineSubmitted { get; set; }
    public bool ReadyForPassword { get; set; }
    public string? ChallengeText { get; set; }
    public string? Message { get; set; }
}

public class CompleteOwnerRecoveryRequest
{
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class SubmitOwnerRecoveryCodeRequest
{
    public string RecoveryCode { get; set; } = string.Empty;
}
