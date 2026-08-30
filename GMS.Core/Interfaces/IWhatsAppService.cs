namespace GMS.Core.Interfaces;

/// <summary>
/// WhatsApp messaging service interface.
/// Sends pre-approved Arabic templates via 4jawaly.com (Egyptian Meta BSP).
/// </summary>
public interface IWhatsAppService
{
    /// <summary>Send membership expiry reminder.</summary>
    Task SendExpiryReminderAsync(Guid memberId, int daysLeft);

    /// <summary>Send membership expiry reminder with phone number directly.</summary>
    Task SendExpiryReminderAsync(string phone, string memberName, int daysLeft);

    /// <summary>Send birthday greeting with discount code.</summary>
    Task SendBirthdayGreetingAsync(Guid memberId, string discountCode);

    /// <summary>Send birthday greeting with phone number directly.</summary>
    Task SendBirthdayGreetingAsync(string phone, string memberName, string discountCode);

    /// <summary>Send class reminder.</summary>
    Task SendClassReminderAsync(Guid memberId, string className, DateTime classTime);

    /// <summary>Send class reminder with phone number directly.</summary>
    Task SendClassReminderAsync(string phone, string className, DateTime startTime);

    /// <summary>Send guest invitation WhatsApp message.</summary>
    Task SendGuestInvitationAsync(string phoneNumber, string guestName, string gymName, DateOnly visitDate);

    /// <summary>Send renewal/payment confirmation.</summary>
    Task SendRenewalConfirmationAsync(string phone, string memberName, DateTime newExpiry);

    /// <summary>Send a document (e.g. an invoice PDF) as a WhatsApp media message.</summary>
    Task SendDocumentAsync(string phone, string memberName, string documentUrl, string caption, string captionAr);

    /// <summary>Send a named pre-approved template with substitution parameters.</summary>
    Task SendTemplateAsync(string phone, string templateName, Dictionary<string, string> parameters);

    /// <summary>
    /// Generic staff-desk broadcast: exact title/body from Send Notification (not an expiry template).
    /// Prefer Arabic body when present; otherwise English. Title included when non-empty.
    /// Default no-op so test stubs compile; Mock + FourJawaly override.
    /// </summary>
    Task SendNotificationAsync(
        string phone,
        string title,
        string body,
        string? titleAr = null,
        string? bodyAr = null)
        => Task.CompletedTask;
}
