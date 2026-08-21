namespace GMS.Application.DTOs.Auth;

/// <summary>Member App Stage 0 claim — Gym Code + staff-issued one-time activation code.</summary>
public class MemberActivateRequest
{
    public string GymCode { get; set; } = string.Empty;
    public string ActivationCode { get; set; } = string.Empty;
}
