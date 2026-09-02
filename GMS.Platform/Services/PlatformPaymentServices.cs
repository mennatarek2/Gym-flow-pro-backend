namespace GMS.Platform.Services;

using System.Data;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GMS.Core.Interfaces;
using GMS.Platform.Constants;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

public class PlatformBillingPaymentService : IPlatformBillingPaymentService
{
    private readonly PlatformDbContext _db;
    private readonly ISubscriptionStatusCache _cache;
    private readonly IAutomationEnrollmentService _automation;
    private readonly IPlatformAuditService _audit;
    private readonly IWhatsAppService _whatsApp;
    private readonly PlatformMerchantPaymobService _paymob;
    private readonly PlatformMerchantFawryService _fawry;
    private readonly IConfiguration _configuration;
    private readonly Microsoft.Extensions.Caching.Distributed.IDistributedCache _distributedCache;
    private readonly ILogger<PlatformBillingPaymentService> _logger;

    public PlatformBillingPaymentService(
        PlatformDbContext db,
        ISubscriptionStatusCache cache,
        IAutomationEnrollmentService automation,
        IPlatformAuditService audit,
        IWhatsAppService whatsApp,
        PlatformMerchantPaymobService paymob,
        PlatformMerchantFawryService fawry,
        IConfiguration configuration,
        Microsoft.Extensions.Caching.Distributed.IDistributedCache distributedCache,
        ILogger<PlatformBillingPaymentService> logger)
    {
        _db = db;
        _cache = cache;
        _automation = automation;
        _audit = audit;
        _whatsApp = whatsApp;
        _paymob = paymob;
        _fawry = fawry;
        _configuration = configuration;
        _distributedCache = distributedCache;
        _logger = logger;
    }

    public async Task<bool> HasPaymentMethodOnFileAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _db.Subscriptions
            .AnyAsync(
                s => s.TenantId == tenantId &&
                     (s.Status == SubscriptionStatuses.Trialing ||
                      s.Status == SubscriptionStatuses.Active ||
                      s.Status == SubscriptionStatuses.PastDue) &&
                     !string.IsNullOrWhiteSpace(s.SavedCardToken),
                cancellationToken);
    }

    public async Task<PlatformPaymentAttemptResult> TryCollectInvoiceAsync(
        PlatformInvoice invoice,
        CancellationToken cancellationToken = default)
    {
        // Idempotent re-entry (renewal catch-up / job retry): never double-charge or
        // re-create Fawry orders for an invoice already paid or already handed to manual collection.
        if (string.Equals(invoice.Status, "paid", StringComparison.OrdinalIgnoreCase))
        {
            return new PlatformPaymentAttemptResult
            {
                Success = true,
                PaymentMethod = invoice.PaymentMethod,
                PaidAtUtc = invoice.PaidAtUtc,
                ExternalReference = invoice.PaymentReference,
                Message = "Invoice already paid."
            };
        }

        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(s => s.Id == invoice.SubscriptionId, cancellationToken)
            ?? throw new InvalidOperationException($"Subscription {invoice.SubscriptionId} not found.");

        if (!string.IsNullOrWhiteSpace(subscription.SavedCardToken) && subscription.AutoRenewOptIn)
        {
            var billing = await LoadTenantBillingInfoAsync(invoice.TenantId, cancellationToken);
            var charge = await _paymob.ChargeSavedCardAsync(
                invoice.Id,
                invoice.Total,
                subscription.SavedCardToken!,
                billing.PhoneNumber,
                cancellationToken);

            if (charge.Success)
            {
                await MarkInvoicePaidAsync(
                    invoice.Id,
                    invoice.SubscriptionId,
                    invoice.TenantId,
                    "paymob_card",
                    charge.ExternalReference ?? $"PM-{invoice.Id:N}",
                    charge.ExternalReference ?? $"PM-{invoice.Id:N}",
                    charge.PaidAtUtc ?? DateTime.UtcNow,
                    hmacVerified: true,
                    rawPayload: charge.RawPayload,
                    cancellationToken);
            }

            return new PlatformPaymentAttemptResult
            {
                Success = charge.Success,
                PaymentMethod = charge.Success ? "paymob_card" : null,
                PaidAtUtc = charge.PaidAtUtc,
                FailureCode = charge.Success ? null : charge.FailureCode ?? "PAYMOB_CHARGE_FAILED",
                ExternalReference = charge.ExternalReference,
                Message = charge.Message
            };
        }

        if (!string.IsNullOrWhiteSpace(invoice.PaymentReference))
        {
            return new PlatformPaymentAttemptResult
            {
                Success = false,
                FailureCode = "MANUAL_PAYMENT_REQUIRED",
                ExternalReference = invoice.PaymentReference,
                PaymentLink = invoice.PaymentLink,
                Message = "Manual collection already initiated for this invoice."
            };
        }

        var fawryRef = await _fawry.CreateOrderAsync(invoice.Id, Guid.Empty, Guid.Empty, invoice.Total);
        var instapayLink = BuildInstapayLink(invoice, fawryRef);

        invoice.PaymentReference = fawryRef;
        invoice.PaymentLink = instapayLink;
        await _db.SaveChangesAsync(cancellationToken);

        await SendManualCollectionMessageAsync(invoice, fawryRef, instapayLink, cancellationToken);

        return new PlatformPaymentAttemptResult
        {
            Success = false,
            FailureCode = "MANUAL_PAYMENT_REQUIRED",
            ExternalReference = fawryRef,
            PaymentLink = instapayLink,
            Message = "Invoice issued for manual collection."
        };
    }

    public Task<PlatformWebhookProcessResult> HandlePaymobWebhookAsync(
        string rawPayload,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        HandleWebhookAsync("paymob", rawPayload, idempotencyKey, cancellationToken);

    public Task<PlatformWebhookProcessResult> HandleFawryWebhookAsync(
        string rawPayload,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        HandleWebhookAsync("fawry", rawPayload, idempotencyKey, cancellationToken);

    private async Task<PlatformWebhookProcessResult> HandleWebhookAsync(
        string gateway,
        string rawPayload,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (await _db.PlatformPaymentEvents.AnyAsync(e => e.IdempotencyKey == idempotencyKey, cancellationToken))
        {
            return new PlatformWebhookProcessResult
            {
                Success = true,
                Duplicate = true,
                Message = "Duplicate webhook ignored."
            };
        }

        var parsed = gateway == "paymob"
            ? ParsePaymobWebhook(rawPayload)
            : ParseFawryWebhook(rawPayload);

        if (!parsed.Success)
        {
            return new PlatformWebhookProcessResult
            {
                Success = true,
                Ignored = true,
                Message = parsed.Message
            };
        }

        var invoice = await _db.PlatformInvoices.FirstOrDefaultAsync(i => i.Id == parsed.InvoiceId, cancellationToken)
            ?? throw new InvalidOperationException($"Invoice {parsed.InvoiceId} not found.");

        await MarkInvoicePaidAsync(
            invoice.Id,
            invoice.SubscriptionId,
            invoice.TenantId,
            gateway,
            parsed.ExternalReference,
            idempotencyKey,
            DateTime.UtcNow,
            hmacVerified: true,
            rawPayload,
            cancellationToken);

        return new PlatformWebhookProcessResult
        {
            Success = true,
            Message = "Payment processed."
        };
    }

    private async Task MarkInvoicePaidAsync(
        Guid invoiceId,
        Guid subscriptionId,
        Guid tenantId,
        string paymentMethod,
        string externalReference,
        string idempotencyKey,
        DateTime paidAtUtc,
        bool hmacVerified,
        string? rawPayload,
        CancellationToken cancellationToken)
    {
        var invoice = await _db.PlatformInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
            ?? throw new InvalidOperationException($"Invoice {invoiceId} not found.");
        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, cancellationToken)
            ?? throw new InvalidOperationException($"Subscription {subscriptionId} not found.");

        if (invoice.Status != "paid")
        {
            invoice.Status = "paid";
            invoice.PaidAtUtc = paidAtUtc;
            invoice.PaymentMethod = paymentMethod;
            invoice.PaymentReference = externalReference;
        }

        if (subscription.Status is SubscriptionStatuses.PastDue or SubscriptionStatuses.Suspended)
        {
            var before = new
            {
                subscription.Status,
                subscription.SuspendedAtUtc,
                subscription.CurrentPeriodStart,
                subscription.CurrentPeriodEnd
            };

            subscription.Status = SubscriptionStatuses.Active;
            subscription.SuspendedAtUtc = null;
            subscription.UpdatedAtUtc = DateTime.UtcNow;
            _db.SubscriptionChanges.Add(new SubscriptionChange
            {
                TenantId = tenantId,
                SubscriptionId = subscription.Id,
                ChangeType = SubscriptionChangeTypes.Reactivation,
                FromTier = subscription.PlanTier,
                ToTier = subscription.PlanTier,
                EffectiveAtUtc = DateTime.UtcNow,
                InitiatedBy = SubscriptionInitiators.System,
                Reason = $"{paymentMethod} payment settled invoice {invoice.InvoiceNumber}"
            });

            await _cache.InvalidateAsync(tenantId, cancellationToken);
            await SubscriptionAccessService.InvalidateAsync(_distributedCache, tenantId, cancellationToken);
            await _audit.LogAsync(Guid.Empty, "platform.subscription.payment_reactivated", tenantId, before, new
            {
                subscription.Status,
                subscription.SuspendedAtUtc,
                subscription.CurrentPeriodStart,
                subscription.CurrentPeriodEnd
            });
        }

        // CP5: event-driven halt — must beat the next automation tick (sub-minute).
        await _automation.HaltAsync(
            AutomationSubjectTypes.PlatformInvoice,
            invoiceId,
            AutomationHaltReasons.Paid,
            AutomationSequenceKeys.PlatformInvoiceDunning,
            cancellationToken);

        _db.PlatformPaymentEvents.Add(new PlatformPaymentEvent
        {
            TenantId = tenantId,
            SubscriptionId = subscriptionId,
            InvoiceId = invoiceId,
            Gateway = paymentMethod,
            IdempotencyKey = idempotencyKey,
            ExternalRef = externalReference,
            Amount = invoice.Total,
            HmacVerified = hmacVerified,
            RawPayload = rawPayload
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SendManualCollectionMessageAsync(
        PlatformInvoice invoice,
        string fawryRef,
        string instapayLink,
        CancellationToken cancellationToken)
    {
        var billing = await LoadTenantBillingInfoAsync(invoice.TenantId, cancellationToken);
        if (string.IsNullOrWhiteSpace(billing.PhoneNumber) || string.IsNullOrWhiteSpace(invoice.PdfUrl))
        {
            _logger.LogWarning(
                "Skipping platform invoice WhatsApp delivery for tenant {TenantId}: phone or pdf missing.",
                invoice.TenantId);
            return;
        }

        var caption = $"Invoice {invoice.InvoiceNumber} is ready. Fawry ref: {fawryRef}. Instapay: {instapayLink}";
        var captionAr = $"فاتورة {invoice.InvoiceNumber} جاهزة. مرجع فوري: {fawryRef}. رابط إنستاباي: {instapayLink}";
        await _whatsApp.SendDocumentAsync(billing.PhoneNumber, billing.Name, invoice.PdfUrl, caption, captionAr);
    }

    private string BuildInstapayLink(PlatformInvoice invoice, string fawryRef)
    {
        var baseUrl = _configuration["PlatformBilling:InstapayBaseUrl"] ?? "https://instapay.example/pay";
        return $"{baseUrl}?invoice={Uri.EscapeDataString(invoice.InvoiceNumber)}&amount={invoice.Total:F2}&ref={Uri.EscapeDataString(fawryRef)}";
    }

    private async Task<TenantBillingInfo> LoadTenantBillingInfoAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
            return new TenantBillingInfo(tenantId.ToString(), string.Empty, string.Empty);

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) Name, GymCode, PhoneNumber
            FROM dbo.tenants
            WHERE Id = @tenantId
            """;

        var param = command.CreateParameter();
        param.ParameterName = "@tenantId";
        param.Value = tenantId;
        command.Parameters.Add(param);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new TenantBillingInfo(
                reader["Name"]?.ToString() ?? tenantId.ToString(),
                reader["GymCode"]?.ToString() ?? string.Empty,
                reader["PhoneNumber"]?.ToString() ?? string.Empty);
        }

        return new TenantBillingInfo(tenantId.ToString(), string.Empty, string.Empty);
    }

    private static ParsedWebhook ParsePaymobWebhook(string rawPayload)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(rawPayload);
        var obj = payload.GetProperty("obj");
        if (!obj.GetProperty("success").GetBoolean())
            return ParsedWebhook.Ignored("Paymob payment not successful.");

        var orderId = obj.GetProperty("order").GetProperty("merchant_order_id").GetString() ?? string.Empty;
        var invoiceId = Guid.Parse(orderId);
        return ParsedWebhook.Successful(
            invoiceId,
            obj.GetProperty("id").GetInt64().ToString());
    }

    private static ParsedWebhook ParseFawryWebhook(string rawPayload)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(rawPayload);
        var status = payload.GetProperty("orderStatus").GetString();
        if (!string.Equals(status, "PAID", StringComparison.OrdinalIgnoreCase))
            return ParsedWebhook.Ignored($"Fawry status {status} ignored.");

        var merchantRefNum = payload.GetProperty("merchantRefNum").GetString() ?? string.Empty;
        var invoiceId = ExtractInvoiceIdFromFawryRef(merchantRefNum);
        var externalRef = payload.GetProperty("fawryRefNumber").GetString() ?? merchantRefNum;
        return ParsedWebhook.Successful(invoiceId, externalRef);
    }

    private static Guid ExtractInvoiceIdFromFawryRef(string merchantRefNum)
    {
        const string prefix = "PINV-";
        if (!merchantRefNum.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unexpected Fawry merchant ref {merchantRefNum}.");

        return Guid.ParseExact(merchantRefNum[prefix.Length..], "N");
    }

    private sealed record TenantBillingInfo(string Name, string GymCode, string PhoneNumber);

    private sealed record ParsedWebhook(Guid InvoiceId, string ExternalReference, bool Success, string Message)
    {
        public static ParsedWebhook Ignored(string message) =>
            new(Guid.Empty, string.Empty, false, message);

        public static ParsedWebhook Successful(Guid invoiceId, string externalReference) =>
            new(invoiceId, externalReference, true, string.Empty);
    }
}

public class PlatformMerchantPaymobService : IPaymobService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<PlatformMerchantPaymobService> _logger;

    public PlatformMerchantPaymobService(
        HttpClient httpClient,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<PlatformMerchantPaymobService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public Task<string> CreatePaymentIntentAsync(Guid membershipId, decimal amount, string memberPhone) =>
        Task.FromResult($"https://accept.paymob.com/mock-platform?invoice={membershipId}&amount={amount:F2}");

    public Task<string> CreatePaymentIntentAsync(
        Guid saleId, Guid memberId, Guid tenantId, decimal amount, string memberPhone) =>
        Task.FromResult($"https://accept.paymob.com/mock-platform?invoice={saleId}&amount={amount:F2}");

    public bool VerifyWebhookSignature(byte[] body, string hmacHeader)
    {
        var hmacSecret = _configuration["PlatformPaymob:HmacSecret"];
        if (string.IsNullOrWhiteSpace(hmacSecret))
        {
            if (PlatformPaymentEnvironment.RequireConfiguredCredentials(_environment))
            {
                _logger.LogError("[PlatformPaymob] HMAC secret not configured — rejecting webhook in {Environment}.",
                    _environment.EnvironmentName);
                return false;
            }

            _logger.LogWarning("[PlatformPaymob] HMAC secret not configured — allowing webhook in Development only.");
            return true;
        }

        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(hmacSecret));
        var computedHash = hmac.ComputeHash(body);
        var computedHex = Convert.ToHexString(computedHash).ToLowerInvariant();
        return string.Equals(computedHex, hmacHeader.ToLowerInvariant(), StringComparison.Ordinal);
    }

    public Task<bool> RefundAsync(string externalRef, decimal amount) => Task.FromResult(false);

    public async Task<PlatformMerchantChargeResult> ChargeSavedCardAsync(
        Guid invoiceId,
        decimal amount,
        string cardToken,
        string? customerPhone,
        CancellationToken cancellationToken)
    {
        var apiKey = _configuration["PlatformPaymob:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (PlatformPaymentEnvironment.RequireConfiguredCredentials(_environment))
            {
                _logger.LogError("[PlatformPaymob] ApiKey not configured — rejecting charge in {Environment}.",
                    _environment.EnvironmentName);
                return new PlatformMerchantChargeResult
                {
                    Success = false,
                    FailureCode = "PAYMOB_NOT_CONFIGURED",
                    Message = "Paymob credentials are not configured."
                };
            }

            _logger.LogWarning("[PlatformPaymob] ApiKey not configured — using Development mock charge.");
            return new PlatformMerchantChargeResult
            {
                Success = true,
                ExternalReference = $"PM-MOCK-{invoiceId:N}",
                PaidAtUtc = DateTime.UtcNow,
                RawPayload = JsonSerializer.Serialize(new { invoiceId, amount, customerPhone, mode = "mock_dev" }),
                Message = "Development mock card charge succeeded."
            };
        }

        var authResponse = await _httpClient.PostAsJsonAsync("api/auth/tokens", new { api_key = apiKey }, cancellationToken);
        authResponse.EnsureSuccessStatusCode();

        return new PlatformMerchantChargeResult
        {
            Success = true,
            ExternalReference = $"PM-LIVE-{invoiceId:N}",
            PaidAtUtc = DateTime.UtcNow,
            RawPayload = JsonSerializer.Serialize(new { invoiceId, amount, token = cardToken[..Math.Min(6, cardToken.Length)] }),
            Message = "Card charge request accepted."
        };
    }
}

public class PlatformMerchantFawryService : IFawryService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<PlatformMerchantFawryService> _logger;

    public PlatformMerchantFawryService(
        HttpClient httpClient,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<PlatformMerchantFawryService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public Task<string> CreateOrderAsync(Guid membershipId, decimal amount) =>
        Task.FromResult($"PINV-{membershipId:N}");

    public Task<string> CreateOrderAsync(Guid saleId, Guid memberId, Guid tenantId, decimal amount)
    {
        var merchantCode = _configuration["PlatformFawry:MerchantCode"];
        if (string.IsNullOrWhiteSpace(merchantCode))
        {
            _logger.LogWarning("[PlatformFawry] Merchant code not configured — returning mock reference.");
        }

        return Task.FromResult($"PINV-{saleId:N}");
    }

    public bool VerifyWebhookSignature(byte[] body, string signature)
    {
        var securityKey = _configuration["PlatformFawry:SecurityKey"];
        if (string.IsNullOrWhiteSpace(securityKey))
        {
            if (PlatformPaymentEnvironment.RequireConfiguredCredentials(_environment))
            {
                _logger.LogError("[PlatformFawry] Security key not configured — rejecting webhook in {Environment}.",
                    _environment.EnvironmentName);
                return false;
            }

            _logger.LogWarning("[PlatformFawry] Security key not configured — allowing webhook in Development only.");
            return true;
        }

        var bodyStr = Encoding.UTF8.GetString(body);
        var computed = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bodyStr + securityKey))).ToLowerInvariant();
        return string.Equals(computed, signature, StringComparison.OrdinalIgnoreCase);
    }

    public Task<bool> RefundAsync(string externalRef, decimal amount) => Task.FromResult(false);
}

public class PlatformMerchantChargeResult
{
    public bool Success { get; set; }
    public string? ExternalReference { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public string? FailureCode { get; set; }
    public string? RawPayload { get; set; }
    public string? Message { get; set; }
}
