namespace GMS.Api.Platform.Controllers;

using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Platform.Interfaces;
using GMS.Platform.Services;

[ApiController]
[Route("platform-api/webhooks")]
public class PlatformPaymentWebhooksController : ControllerBase
{
    private readonly PlatformMerchantPaymobService _paymob;
    private readonly PlatformMerchantFawryService _fawry;
    private readonly IPlatformBillingPaymentService _payments;
    private readonly ILogger<PlatformPaymentWebhooksController> _logger;

    public PlatformPaymentWebhooksController(
        PlatformMerchantPaymobService paymob,
        PlatformMerchantFawryService fawry,
        IPlatformBillingPaymentService payments,
        ILogger<PlatformPaymentWebhooksController> logger)
    {
        _paymob = paymob;
        _fawry = fawry;
        _payments = payments;
        _logger = logger;
    }

    [HttpPost("paymob")]
    [AllowAnonymous]
    public async Task<IActionResult> Paymob(CancellationToken cancellationToken)
    {
        Request.EnableBuffering();
        var bodyBytes = await ReadBodyBytesAsync();
        var rawPayload = Encoding.UTF8.GetString(bodyBytes);

        var hmacHeader = Request.Headers["X-Hmac"].FirstOrDefault() ?? string.Empty;
        if (!_paymob.VerifyWebhookSignature(bodyBytes, hmacHeader))
            return Unauthorized(new { error = "Invalid HMAC signature" });

        try
        {
            var result = await _payments.HandlePaymobWebhookAsync(rawPayload, $"paymob:{hmacHeader}", cancellationToken);
            return Ok(new { status = result.Duplicate ? "duplicate" : result.Ignored ? "ignored" : "processed", result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Platform Paymob webhook processing failed.");
            return Ok(new { status = "error" });
        }
    }

    [HttpPost("fawry")]
    [AllowAnonymous]
    public async Task<IActionResult> Fawry(CancellationToken cancellationToken)
    {
        Request.EnableBuffering();
        var bodyBytes = await ReadBodyBytesAsync();
        var rawPayload = Encoding.UTF8.GetString(bodyBytes);

        var signature = Request.Headers["X-Fawry-Signature"].FirstOrDefault() ?? string.Empty;
        if (!_fawry.VerifyWebhookSignature(bodyBytes, signature))
            return Unauthorized(new { error = "Invalid signature" });

        try
        {
            var result = await _payments.HandleFawryWebhookAsync(rawPayload, $"fawry:{signature}", cancellationToken);
            return Ok(new { status = result.Duplicate ? "duplicate" : result.Ignored ? "ignored" : "processed", result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Platform Fawry webhook processing failed.");
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
