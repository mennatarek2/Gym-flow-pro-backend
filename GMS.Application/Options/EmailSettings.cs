namespace GMS.Application.Options;

/// <summary>
/// Configuration options for email delivery (SMTP or SendGrid).
/// Bound from appsettings.json: "EmailSettings" section.
/// </summary>
public class EmailSettings
{
    public const string SectionName = "EmailSettings";

    /// <summary>
    /// Email provider: "smtp" | "sendgrid" (default: "smtp").
    /// </summary>
    public string Provider { get; set; } = "smtp";

    /// <summary>
    /// SMTP host address (e.g., "smtp.gmail.com").
    /// </summary>
    public string SmtpHost { get; set; } = string.Empty;

    /// <summary>
    /// SMTP port (typically 587 for STARTTLS, 465 for SSL).
    /// </summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>
    /// SMTP username (typically an email address).
    /// </summary>
    public string SmtpUser { get; set; } = string.Empty;

    /// <summary>
    /// SMTP password (or app-specific password for Gmail).
    /// </summary>
    public string SmtpPassword { get; set; } = string.Empty;

    /// <summary>
    /// SendGrid API key (only required if Provider == "sendgrid").
    /// </summary>
    public string SendGridApiKey { get; set; } = string.Empty;

    /// <summary>
    /// "From" email address (e.g., "noreply@gymflowpro.com").
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>
    /// Display name for "From" field (e.g., "GymFlowPro").
    /// </summary>
    public string FromName { get; set; } = string.Empty;

    /// <summary>
    /// OTP time-to-live in minutes for email template display (default: 5).
    /// </summary>
    public int? OtpTtlMinutes { get; set; } = 5;
}
