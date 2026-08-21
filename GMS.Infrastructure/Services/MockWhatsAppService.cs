namespace GMS.Infrastructure.Services;

using Microsoft.Extensions.Logging;
using GMS.Core.Interfaces;

/// <summary>
/// Mock WhatsApp service for development.
/// Logs messages instead of sending via 4jawaly.com.
/// </summary>
public class MockWhatsAppService : IWhatsAppService
{
    private readonly ILogger<MockWhatsAppService> _logger;

    public MockWhatsAppService(ILogger<MockWhatsAppService> logger)
    {
        _logger = logger;
    }

    // === Overloads with memberId (resolve phone from DB) ===

    public Task SendExpiryReminderAsync(Guid memberId, int daysLeft)
    {
        _logger.LogInformation("[MOCK WA] Expiry → Member {MemberId} ({Days} days left)", memberId, daysLeft);
        return Task.CompletedTask;
    }

    public Task SendBirthdayGreetingAsync(Guid memberId, string discountCode)
    {
        _logger.LogInformation("[MOCK WA] Birthday → Member {MemberId}, Code: {Code}", memberId, discountCode);
        return Task.CompletedTask;
    }

    public Task SendClassReminderAsync(Guid memberId, string className, DateTime classTime)
    {
        _logger.LogInformation("[MOCK WA] Class → Member {MemberId}: {Class} at {Time}", memberId, className, classTime);
        return Task.CompletedTask;
    }

    // === Overloads with phone string (used by PaymentService etc.) ===

    public Task SendExpiryReminderAsync(string phone, string memberName, int daysLeft)
    {
        _logger.LogInformation("[MOCK WA] Expiry → {Phone} ({Name}): {Days} days left", phone, memberName, daysLeft);
        return Task.CompletedTask;
    }

    public Task SendBirthdayGreetingAsync(string phone, string memberName, string discountCode)
    {
        _logger.LogInformation("[MOCK WA] Birthday → {Phone} ({Name}): Code {Code}", phone, memberName, discountCode);
        return Task.CompletedTask;
    }

    public Task SendClassReminderAsync(string phone, string className, DateTime startTime)
    {
        _logger.LogInformation("[MOCK WA] Class → {Phone}: {Class} at {Time}", phone, className, startTime);
        return Task.CompletedTask;
    }

    public Task SendGuestInvitationAsync(string phoneNumber, string guestName, string gymName, DateOnly visitDate)
    {
        _logger.LogInformation("[MOCK WA] Invitation → {Phone}: {Guest} invited to {Gym} on {Date}",
            phoneNumber, guestName, gymName, visitDate);
        return Task.CompletedTask;
    }

    public Task SendRenewalConfirmationAsync(string phone, string memberName, DateTime newExpiry)
    {
        _logger.LogInformation("[MOCK WA] Renewal → {Phone} ({Name}): expires {Expiry:dd/MM/yyyy}",
            phone, memberName, newExpiry);
        return Task.CompletedTask;
    }

    public Task SendDocumentAsync(string phone, string memberName, string documentUrl, string caption, string captionAr)
    {
        _logger.LogInformation("[MOCK WA] Document → {Phone} ({Name}): {Url} — {Caption}",
            phone, memberName, documentUrl, caption);
        return Task.CompletedTask;
    }

    public Task SendTemplateAsync(string phone, string templateName, Dictionary<string, string> parameters)
    {
        _logger.LogInformation("[MOCK WA] Template → {Phone}: {Template} ({Params})",
            phone, templateName, string.Join(", ", parameters.Select(p => $"{p.Key}={p.Value}")));
        return Task.CompletedTask;
    }
}
