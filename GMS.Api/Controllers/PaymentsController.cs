namespace GMS.Api.Controllers;

using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Application.Interfaces;
using GMS.Core.Interfaces;

/// <summary>
/// Payment webhook endpoints.
/// [AllowAnonymous] — payment gateways don't send JWT tokens.
/// Security is enforced via HMAC signature verification instead.
/// </summary>
[Route("api/payments")]
public class PaymentsController : BaseApiController
{
    private readonly IPaymobService _paymobService;
    private readonly IFawryService _fawryService;
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        IPaymobService paymobService,
        IFawryService fawryService,
        IPaymentService paymentService,
        ILogger<PaymentsController> logger)
    {
        _paymobService = paymobService;
        _fawryService = fawryService;
        _paymentService = paymentService;
        _logger = logger;
    }

    /// <summary>
    /// Paymob webhook — called by Paymob after payment completion.
    /// 
    /// Security flow:
    ///   1. Read raw body bytes (EnableBuffering)
    ///   2. Compute HMAC-SHA512 over the body using our secret
    ///   3. Compare with X-Hmac header — reject if mismatch
    ///   4. Check idempotency (ExternalRef already processed?)
    ///   5. Process payment → create membership
    /// </summary>
    [HttpPost("paymob-webhook")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> PaymobWebhook()
    {
        // Step 1: Read raw body for HMAC verification
        Request.EnableBuffering();
        var bodyBytes = await ReadBodyBytesAsync();
        var bodyStr = Encoding.UTF8.GetString(bodyBytes);

        // Step 2-3: Verify HMAC signature
        var hmacHeader = Request.Headers["X-Hmac"].FirstOrDefault() ?? string.Empty;
        var hmacValid = _paymobService.VerifyWebhookSignature(bodyBytes, hmacHeader);

        if (!hmacValid)
        {
            _logger.LogWarning("[Paymob Webhook] HMAC FAILED — IP: {IP}, Body length: {Len}",
                HttpContext.Connection.RemoteIpAddress, bodyBytes.Length);
            return Unauthorized(new { error = "Invalid HMAC signature" });
        }

        // Step 4-5: Parse payload and process
        try
        {
            var payload = JsonSerializer.Deserialize<JsonElement>(bodyStr);
            var obj = payload.GetProperty("obj");

            var success = obj.GetProperty("success").GetBoolean();
            if (!success)
            {
                _logger.LogInformation("[Paymob Webhook] Payment not successful — ignoring");
                return Ok(new { status = "ignored", reason = "payment_not_successful" });
            }

            var externalRef = obj.GetProperty("id").GetInt64().ToString();
            var amountCents = obj.GetProperty("amount_cents").GetInt64();
            var amount = amountCents / 100m;

            // Extract member/tenant from merchant_order_id or order metadata
            var merchantOrderId = obj.GetProperty("order").GetProperty("merchant_order_id").GetString() ?? "";

            // Attempt to parse memberId and tenantId from metadata
            // Format expected: "memberId|tenantId" or just membershipId
            Guid memberId = Guid.Empty, tenantId = Guid.Empty;
            if (merchantOrderId.Contains('|'))
            {
                var parts = merchantOrderId.Split('|');
                Guid.TryParse(parts[0], out memberId);
                Guid.TryParse(parts[1], out tenantId);
            }

            var result = await _paymentService.HandleSuccessfulPaymentAsync(
                "paymob", externalRef, amount, memberId, tenantId, bodyStr, hmacValid);

            return Ok(new { status = result.IsSuccess ? "processed" : "failed", message = result.IsSuccess ? result.Data : result.Error });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Paymob Webhook] Error processing payload");
            return Ok(new { status = "error" }); // Return 200 to prevent Paymob retries
        }
    }

    /// <summary>
    /// Fawry webhook — called by Fawry after payment completion.
    /// Same security pattern as Paymob but with SHA-256 signature.
    /// </summary>
    [HttpPost("fawry-webhook")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> FawryWebhook()
    {
        Request.EnableBuffering();
        var bodyBytes = await ReadBodyBytesAsync();
        var bodyStr = Encoding.UTF8.GetString(bodyBytes);

        var signature = Request.Headers["X-Fawry-Signature"].FirstOrDefault() ?? string.Empty;
        var sigValid = _fawryService.VerifyWebhookSignature(bodyBytes, signature);

        if (!sigValid)
        {
            _logger.LogWarning("[Fawry Webhook] Signature FAILED — IP: {IP}",
                HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { error = "Invalid signature" });
        }

        try
        {
            var payload = JsonSerializer.Deserialize<JsonElement>(bodyStr);

            var statusCode = payload.GetProperty("orderStatus").GetString();
            if (statusCode != "PAID")
            {
                _logger.LogInformation("[Fawry Webhook] Status {Status} — ignoring", statusCode);
                return Ok(new { status = "ignored" });
            }

            var externalRef = payload.GetProperty("fawryRefNumber").GetString() ?? "";
            var amount = payload.GetProperty("paymentAmount").GetDecimal();
            var merchantRefNum = payload.GetProperty("merchantRefNum").GetString() ?? "";

            Guid memberId = Guid.Empty, tenantId = Guid.Empty;
            if (merchantRefNum.Contains('|'))
            {
                var parts = merchantRefNum.Split('|');
                Guid.TryParse(parts[0], out memberId);
                Guid.TryParse(parts[1], out tenantId);
            }

            var result = await _paymentService.HandleSuccessfulPaymentAsync(
                "fawry", externalRef, amount, memberId, tenantId, bodyStr, sigValid);

            return Ok(new { status = result.IsSuccess ? "processed" : "failed", message = result.IsSuccess ? result.Data : result.Error });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Fawry Webhook] Error processing payload");
            return Ok(new { status = "error" });
        }
    }

    private async Task<byte[]> ReadBodyBytesAsync()
    {
        Request.Body.Position = 0;
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms);
        Request.Body.Position = 0;
        return ms.ToArray();
    }
}
