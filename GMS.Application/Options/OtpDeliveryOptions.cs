namespace GMS.Application.Options;

/// <summary>
/// Configuration options for OTP delivery mechanism.
/// Provides settings for OTP generation, TTL, and delivery method selection.
/// Bound from appsettings.json: "OtpDelivery" section.
/// </summary>
public class OtpDeliveryOptions
{
    public const string SectionName = "OtpDelivery";

    /// <summary>
    /// OTP delivery provider: "email" | "sms" (default: "email").
    /// </summary>
    public string Provider { get; set; } = "email";

    /// <summary>
    /// OTP time-to-live in minutes (default: 5).
    /// </summary>
    public int OtpTtlMinutes { get; set; } = 5;

    /// <summary>
    /// OTP code length in digits (default: 6).
    /// </summary>
    public int OtpLength { get; set; } = 6;
}
