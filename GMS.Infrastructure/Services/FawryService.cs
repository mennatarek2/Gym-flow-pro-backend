namespace GMS.Infrastructure.Services;

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using GMS.Core.Interfaces;

/// <summary>
/// Fawry payment gateway integration.
/// Supports kiosk and mobile wallet payments popular in Egypt.
/// </summary>
public class FawryService : IFawryService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<FawryService> _logger;

    public FawryService(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<FawryService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<string> CreateOrderAsync(Guid membershipId, decimal amount)
    {
        var merchantCode = _config["Fawry:MerchantCode"];
        var securityKey = _config["Fawry:SecurityKey"];

        if (string.IsNullOrEmpty(merchantCode))
        {
            _logger.LogWarning("[Fawry] Merchant code not configured — returning mock reference");
            return $"FAWRY-MOCK-{membershipId.ToString()[..8].ToUpper()}";
        }

        try
        {
            var merchantRefNum = $"GFP-{membershipId.ToString()[..8].ToUpper()}-{DateTime.UtcNow:yyMMddHHmmss}";

            // Build signature: merchantCode + merchantRefNum + amount + securityKey
            var signatureInput = $"{merchantCode}{merchantRefNum}{amount:F2}{securityKey}";
            var signature = ComputeSha256(signatureInput);

            var payload = new
            {
                merchantCode,
                merchantRefNum,
                amount = amount,
                currencyCode = "EGP",
                description = $"GymFlowPro Membership - {membershipId}",
                paymentExpiry = DateTime.UtcNow.AddHours(48).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                signature
            };

            var response = await _httpClient.PostAsJsonAsync("ECommerceWeb/Fawry/payments/charge", payload);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[Fawry] Order created: {Ref} for membership {MembershipId}",
                    merchantRefNum, membershipId);
                return merchantRefNum;
            }
            else
            {
                _logger.LogWarning("[Fawry] Order creation failed: {Status} — {Body}",
                    response.StatusCode, responseBody);
                throw new Exception($"Fawry order creation failed: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Fawry] Exception creating order for {MembershipId}", membershipId);
            throw;
        }
    }

    public bool VerifyWebhookSignature(byte[] body, string signature)
    {
        var securityKey = _config["Fawry:SecurityKey"];
        if (string.IsNullOrEmpty(securityKey))
        {
            _logger.LogWarning("[Fawry] Security key not configured — SKIPPING verification (dev only!)");
            return true;
        }

        var bodyStr = Encoding.UTF8.GetString(body);
        var signatureInput = bodyStr + securityKey;
        var computed = ComputeSha256(signatureInput);

        var isValid = computed == signature;

        if (!isValid)
        {
            _logger.LogWarning("[Fawry] Signature verification FAILED — possible tampering");
        }

        return isValid;
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Requests a refund via Fawry's refund API. Mirrors CreateOrderAsync's dev-mode fallback:
    /// without a configured merchant code, this mock-succeeds rather than blocking local development.
    /// </summary>
    public async Task<bool> RefundAsync(string externalRef, decimal amount)
    {
        var merchantCode = _config["Fawry:MerchantCode"];
        var securityKey = _config["Fawry:SecurityKey"];

        if (string.IsNullOrEmpty(merchantCode))
        {
            _logger.LogWarning("[Fawry] Merchant code not configured — mock-succeeding refund for {ExternalRef}", externalRef);
            return true;
        }

        try
        {
            var signatureInput = $"{merchantCode}{externalRef}{amount:F2}{securityKey}";
            var signature = ComputeSha256(signatureInput);

            var payload = new
            {
                merchantCode,
                merchantRefNumber = externalRef,
                refundAmount = amount,
                reason = "Customer refund",
                signature
            };

            var response = await _httpClient.PostAsJsonAsync("ECommerceWeb/Fawry/payments/refund", payload);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("[Fawry] Refund failed for {ExternalRef}: {Status} — {Body}",
                    externalRef, response.StatusCode, body);
                return false;
            }

            _logger.LogInformation("[Fawry] Refund succeeded for {ExternalRef}, amount {Amount} EGP", externalRef, amount);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Fawry] Exception refunding {ExternalRef}", externalRef);
            return false;
        }
    }
}
