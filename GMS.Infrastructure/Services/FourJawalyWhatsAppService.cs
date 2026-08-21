namespace GMS.Infrastructure.Services;

using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// WhatsApp messaging via 4jawaly.com API (Egyptian Meta BSP).
/// All templates are pre-approved Arabic messages.
/// 
/// Uses IHttpClientFactory typed client with Polly retry policy.
/// API key stored in user-secrets (dev) or Key Vault (prod).
/// </summary>
public class FourJawalyWhatsAppService : IWhatsAppService
{
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<FourJawalyWhatsAppService> _logger;

    public FourJawalyWhatsAppService(
        HttpClient httpClient,
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<FourJawalyWhatsAppService> logger)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    // === Overloads that resolve phone from memberId (used by Hangfire jobs) ===

    public async Task SendExpiryReminderAsync(Guid memberId, int daysLeft)
    {
        var (phone, name) = await ResolveMemberAsync(memberId);
        if (phone == null) return;
        await SendExpiryReminderAsync(phone, name!, daysLeft);
    }

    public async Task SendBirthdayGreetingAsync(Guid memberId, string discountCode)
    {
        var (phone, name) = await ResolveMemberAsync(memberId);
        if (phone == null) return;
        await SendBirthdayGreetingAsync(phone, name!, discountCode);
    }

    public async Task SendClassReminderAsync(Guid memberId, string className, DateTime classTime)
    {
        var (phone, _) = await ResolveMemberAsync(memberId);
        if (phone == null) return;
        await SendClassReminderAsync(phone, className, classTime);
    }

    // === Direct phone methods (used by controllers/services) ===

    public async Task SendExpiryReminderAsync(string phone, string memberName, int daysLeft)
    {
        var messageAr = daysLeft switch
        {
            0 => $"⚠️ أهلاً {memberName}، عضويتك تنتهي اليوم! جدد الآن لتستمر بالتمرين.",
            1 => $"⏰ أهلاً {memberName}، عضويتك تنتهي غداً! جدد الآن.",
            3 => $"📅 أهلاً {memberName}، عضويتك تنتهي خلال 3 أيام. لا تنسَ التجديد.",
            7 => $"📋 أهلاً {memberName}، عضويتك تنتهي خلال أسبوع. جدد مبكراً!",
            _ => $"أهلاً {memberName}، عضويتك تنتهي خلال {daysLeft} يوم."
        };

        await SendMessageAsync(phone, messageAr);
    }

    public async Task SendBirthdayGreetingAsync(string phone, string memberName, string discountCode)
    {
        var message = $"🎂 كل سنة وانت طيب يا {memberName}! كود خصم خاص بيك: {discountCode}";
        await SendMessageAsync(phone, message);
    }

    public async Task SendClassReminderAsync(string phone, string className, DateTime startTime)
    {
        var time = startTime.ToString("hh:mm tt");
        var message = $"🏋️ تذكير: حصة {className} بعد ساعتين الساعة {time}. نستناك!";
        await SendMessageAsync(phone, message);
    }

    public async Task SendGuestInvitationAsync(string phoneNumber, string guestName, string gymName, DateOnly visitDate)
    {
        var date = visitDate.ToString("dd/MM/yyyy");
        var message = $"أهلاً {guestName}! أنت مدعو لزيارة {gymName} يوم {date}. نتشرف بزيارتك! 💪";
        await SendMessageAsync(phoneNumber, message);
    }

    public async Task SendRenewalConfirmationAsync(string phone, string memberName, DateTime newExpiry)
    {
        var expiry = newExpiry.ToString("dd/MM/yyyy");
        var message = $"✅ تم تجديد عضويتك بنجاح يا {memberName}! صالحة حتى {expiry}. تمرين موفق! 💪";
        await SendMessageAsync(phone, message);
    }

    // Unlike the Send* methods above (best-effort, swallow-and-log), SendDocumentAsync and
    // SendTemplateAsync propagate failures — RenderAndDeliverInvoiceJob relies on an exception
    // here to trigger Hangfire's [AutomaticRetry].

    public async Task SendDocumentAsync(string phone, string memberName, string documentUrl, string caption, string captionAr)
    {
        var apiKey = _config["FourJawaly:ApiKey"];
        var senderName = _config["FourJawaly:SenderName"] ?? "GymFlowPro";

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("[4jawaly] API key not configured — document NOT sent to {Phone}: {DocumentUrl}", phone, documentUrl);
            return;
        }

        var payload = new
        {
            to = phone,
            documentUrl,
            caption = $"{captionAr}\n{caption}",
            sender = senderName
        };

        var response = await _httpClient.PostAsJsonAsync("api/v1/messages/whatsapp/document", payload);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("[4jawaly] Failed to send document to {Phone}: {Status} — {Error}",
                phone, response.StatusCode, error);
            throw new InvalidOperationException($"4jawaly document send failed with status {response.StatusCode}: {error}");
        }

        _logger.LogInformation("[4jawaly] Document sent to {Phone}: {DocumentUrl}", phone, documentUrl);
    }

    public async Task SendTemplateAsync(string phone, string templateName, Dictionary<string, string> parameters)
    {
        var apiKey = _config["FourJawaly:ApiKey"];
        var senderName = _config["FourJawaly:SenderName"] ?? "GymFlowPro";

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("[4jawaly] API key not configured — template NOT sent to {Phone}: {Template}", phone, templateName);
            return;
        }

        var payload = new
        {
            to = phone,
            template = templateName,
            parameters,
            sender = senderName
        };

        var response = await _httpClient.PostAsJsonAsync("api/v1/messages/whatsapp/template", payload);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("[4jawaly] Failed to send template to {Phone}: {Status} — {Error}",
                phone, response.StatusCode, error);
            throw new InvalidOperationException($"4jawaly template send failed with status {response.StatusCode}: {error}");
        }

        _logger.LogInformation("[4jawaly] Template '{Template}' sent to {Phone}", templateName, phone);
    }

    // === Private Helpers ===

    private async Task SendMessageAsync(string phone, string message)
    {
        var apiKey = _config["FourJawaly:ApiKey"];
        var senderName = _config["FourJawaly:SenderName"] ?? "GymFlowPro";

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("[4jawaly] API key not configured — message NOT sent to {Phone}: {Message}", phone, message);
            return;
        }

        try
        {
            var payload = new
            {
                to = phone,
                body = message,
                sender = senderName
            };

            var response = await _httpClient.PostAsJsonAsync("api/v1/messages/whatsapp", payload);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[4jawaly] WhatsApp sent to {Phone}", phone);
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("[4jawaly] Failed to send to {Phone}: {Status} — {Error}",
                    phone, response.StatusCode, error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[4jawaly] Exception sending WhatsApp to {Phone}", phone);
        }
    }

    private async Task<(string? phone, string? name)> ResolveMemberAsync(Guid memberId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymFlowProDbContext>();

        var member = await db.GymMembers
            .IgnoreQueryFilters()
            .Where(m => m.Id == memberId && !m.IsDeleted)
            .Select(m => new { m.PhoneNumber, m.FullName })
            .FirstOrDefaultAsync();

        if (member == null)
        {
            _logger.LogWarning("[4jawaly] Member {MemberId} not found — skipping", memberId);
            return (null, null);
        }

        return (member.PhoneNumber, member.FullName);
    }
}
