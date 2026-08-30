namespace GMS.Application.DTOs.Auth;

/// <summary>Employee App Phase 1 — Gym Code + HR-issued one-time activation code.</summary>
public class EmployeeActivateRequest
{
    public string GymCode { get; set; } = string.Empty;
    public string ActivationCode { get; set; } = string.Empty;
}
