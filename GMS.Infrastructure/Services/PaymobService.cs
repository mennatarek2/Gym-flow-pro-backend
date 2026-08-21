namespace GMS.Infrastructure.Services;

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using GMS.Core.Interfaces;

/// <summary>
/// Paymob payment gateway integration.
/// API docs: https://docs.paymob.com/
/// 
/// HMAC-SHA512 webhook verification prevents tampered/forged payment notifications.
/// Without this, an attacker could POST fake "payment successful" webhooks and
/// get free memberships.
/// </summary>
public class PaymobService : IPaymobService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<PaymobService> _logger;

    public PaymobService(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<PaymobService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<string> CreatePaymentIntentAsync(Guid membershipId, decimal amount, string memberPhone)
    {
        var apiKey = _config["Paymob:ApiKey"];
        var integrationId = _config["Paymob:IntegrationId"];
        var iframeId = _config["Paymob:IframeId"];

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("[Paymob] API key not configured — returning mock URL");
            return $"https://paymob.com/mock-payment?amount={amount}&ref={membershipId}";
        }

        try
        {
            // Step 1: Auth token
            var authResponse = await _httpClient.PostAsJsonAsync("api/auth/tokens", new { api_key = apiKey });
            var authData = await authResponse.Content.ReadFromJsonAsync<JsonElement>();
            var token = authData.GetProperty("token").GetString();

            // Step 2: Create order
            var orderPayload = new
            {
                auth_token = token,
                delivery_needed = false,
                amount_cents = (int)(amount * 100),
                currency = "EGP",
                merchant_order_id = membershipId.ToString(),
                items = Array.Empty<object>()
            };
            var orderResponse = await _httpClient.PostAsJsonAsync("api/ecommerce/orders", orderPayload);
            var orderData = await orderResponse.Content.ReadFromJsonAsync<JsonElement>();
            var orderId = orderData.GetProperty("id").GetInt64();

            // Step 3: Payment key
            var paymentKeyPayload = new
            {
                auth_token = token,
                amount_cents = (int)(amount * 100),
                expiration = 3600,
                order_id = orderId,
                billing_data = new
                {
                    first_name = "Member",
                    last_name = "GymFlowPro",
                    email = "member@gymflowpro.com",
                    phone_number = memberPhone,
                    apartment = "NA", building = "NA", floor = "NA",
                    street = "NA", city = "Cairo", country = "EG",
                    state = "Cairo", postal_code = "11511",
                    shipping_method = "NA"
                },
                currency = "EGP",
                integration_id = int.Parse(integrationId ?? "0")
            };
            var pkResponse = await _httpClient.PostAsJsonAsync("api/acceptance/payment_keys", paymentKeyPayload);
            var pkData = await pkResponse.Content.ReadFromJsonAsync<JsonElement>();
            var paymentKey = pkData.GetProperty("token").GetString();

            var redirectUrl = $"https://accept.paymob.com/api/acceptance/iframes/{iframeId}?payment_token={paymentKey}";

            _logger.LogInformation("[Paymob] Payment intent created for membership {MembershipId}, amount {Amount} EGP",
                membershipId, amount);

            return redirectUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Paymob] Failed to create payment intent for {MembershipId}", membershipId);
            throw;
        }
    }

    /// <summary>
    /// Verifies HMAC-SHA512 signature of Paymob webhook.
    /// 
    /// WHY THIS IS CRITICAL:
    /// Without HMAC verification, an attacker can:
    /// 1. Inspect your webhook URL from network traffic
    /// 2. POST a fake payload: { "success": true, "amount": 0 }
    /// 3. Get a free membership activated
    /// 
    /// HMAC ensures:
    /// - The payload came from Paymob (only they have the secret)
    /// - The payload wasn't modified in transit
    /// - The request can't be replayed (combined with idempotency check)
    /// </summary>
    public bool VerifyWebhookSignature(byte[] body, string hmacHeader)
    {
        var hmacSecret = _config["Paymob:HmacSecret"];
        if (string.IsNullOrEmpty(hmacSecret))
        {
            _logger.LogWarning("[Paymob] HMAC secret not configured — SKIPPING verification (dev only!)");
            return true; // Allow in dev when secret isn't set
        }

        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(hmacSecret));
        var computedHash = hmac.ComputeHash(body);
        var computedHex = Convert.ToHexString(computedHash).ToLowerInvariant();

        var isValid = computedHex == hmacHeader.ToLowerInvariant();

        if (!isValid)
        {
            _logger.LogWarning("[Paymob] HMAC verification FAILED — possible tampering attempt");
        }

        return isValid;
    }

    /// <summary>
    /// Requests a refund via Paymob's void_refund API. Mirrors CreatePaymentIntentAsync's
    /// dev-mode fallback: without a configured API key, this mock-succeeds rather than blocking
    /// local development.
    /// </summary>
    public async Task<bool> RefundAsync(string externalRef, decimal amount)
    {
        var apiKey = _config["Paymob:ApiKey"];

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("[Paymob] API key not configured — mock-succeeding refund for {ExternalRef}", externalRef);
            return true;
        }

        try
        {
            var authResponse = await _httpClient.PostAsJsonAsync("api/auth/tokens", new { api_key = apiKey });
            var authData = await authResponse.Content.ReadFromJsonAsync<JsonElement>();
            var token = authData.GetProperty("token").GetString();

            var refundPayload = new
            {
                auth_token = token,
                transaction_id = externalRef,
                amount_cents = (int)(amount * 100)
            };

            var refundResponse = await _httpClient.PostAsJsonAsync("api/acceptance/void_refund/refund", refundPayload);

            if (!refundResponse.IsSuccessStatusCode)
            {
                var body = await refundResponse.Content.ReadAsStringAsync();
                _logger.LogWarning("[Paymob] Refund failed for {ExternalRef}: {Status} — {Body}",
                    externalRef, refundResponse.StatusCode, body);
                return false;
            }

            _logger.LogInformation("[Paymob] Refund succeeded for {ExternalRef}, amount {Amount} EGP", externalRef, amount);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Paymob] Exception refunding {ExternalRef}", externalRef);
            return false;
        }
    }
}
