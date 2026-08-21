namespace GMS.Application.DTOs.Trials;

public class TrialConfirmRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string Otp { get; set; } = string.Empty;
}
