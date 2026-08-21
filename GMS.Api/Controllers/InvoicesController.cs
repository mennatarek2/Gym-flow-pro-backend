namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Application.DTOs.Invoices;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>
/// Read-only invoice access, void, and resend-delivery endpoints.
/// </summary>
[Route("api/invoices")]
[Authorize]
public class InvoicesController : BaseApiController
{
    private readonly IInvoiceService _invoiceService;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantSettingsService _tenantSettings;
    private readonly IFileStorageService _files;

    public InvoicesController(
        IInvoiceService invoiceService,
        ITenantContext tenantContext,
        ITenantSettingsService tenantSettings,
        IFileStorageService files)
    {
        _invoiceService = invoiceService;
        _tenantContext = tenantContext;
        _tenantSettings = tenantSettings;
        _files = files;
    }

    /// <summary>GET /api/invoices?from=&amp;to=&amp;memberId=&amp;status=&amp;type=&amp;page=&amp;pageSize=</summary>
    [HttpGet]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetInvoices([FromQuery] InvoiceQueryRequest query)
    {
        var tenantId = _tenantContext.TenantId;
        var result = await _invoiceService.GetPagedAsync(tenantId, query);

        if (!result.IsSuccess)
            return Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);

        return Ok(result.Data);
    }

    /// <summary>GET /api/invoices/{id}</summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.ReportsFinancialView)]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInvoice(Guid id)
    {
        var result = await _invoiceService.GetByIdAsync(id);
        if (!result.IsSuccess)
            return NotFound(result.Error);

        return Ok(result.Data);
    }

    /// <summary>POST /api/invoices/{id}/void</summary>
    [HttpPost("{id:guid}/void")]
    [HasPermission(Permissions.PaymentsRefundApprove)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VoidInvoice(Guid id, [FromBody] VoidInvoiceRequest request)
    {
        var staffUserId = GetUserId();
        var result = await _invoiceService.VoidAsync(id, request.Reason, staffUserId);

        if (!result.IsSuccess)
            return Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);

        return Ok(new { message = result.Message ?? "Invoice voided / تم إلغاء الفاتورة" });
    }

    /// <summary>POST /api/invoices/{id}/resend — re-enqueues the invoice delivery job.</summary>
    [HttpPost("{id:guid}/resend")]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResendInvoice(Guid id)
    {
        var result = await _invoiceService.ResendAsync(id);

        if (!result.IsSuccess)
            return Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);

        return Ok(new { message = result.Message ?? "Invoice delivery re-enqueued / تم إعادة جدولة إرسال الفاتورة" });
    }

    /// <summary>
    /// GET /api/invoices/{id}/receipt-html?paymentId=&amp;format= — self-contained HTML.
    /// Default format is 80mm thermal (POS / reprint). format=a4 is the branded standard invoice.
    /// Same invoice data either way. Optional paymentId adds a payment-received section.
    /// </summary>
    [HttpGet("{id:guid}/receipt-html")]
    [HasPermission(Permissions.SalesSell)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReceiptHtml(
        Guid id,
        [FromQuery] Guid? paymentId,
        [FromQuery] string? format)
    {
        var result = await _invoiceService.GetByIdAsync(id);
        if (!result.IsSuccess)
            return NotFound(result.Error);

        PaymentReceiptInfoDto? payment = null;
        if (paymentId.HasValue)
        {
            var paymentResult = await _invoiceService.GetPaymentInfoAsync(paymentId.Value);
            if (paymentResult.IsSuccess)
                payment = paymentResult.Data;
        }

        var tenantId = _tenantContext.TenantId;
        var settings = await _tenantSettings.GetTenantSettingsAsync(tenantId);
        var tax = await _tenantSettings.GetTaxSettingsAsync(tenantId);
        var model = InvoicePdfModelFactory.FromDto(
            result.Data!,
            settings.IsSuccess ? settings.Data : null,
            tax.IsSuccess ? tax.Data : null,
            payment);

        if (!string.IsNullOrWhiteSpace(model.LogoUrl))
        {
            var logoBytes = await _files.TryReadAsync(model.LogoUrl);
            InvoicePdfModelFactory.AttachLogo(model, logoBytes, model.LogoUrl);
        }

        var layout = string.Equals(format, "a4", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(format, "standard", StringComparison.OrdinalIgnoreCase)
            ? InvoiceDocumentLayout.StandardA4
            : InvoiceDocumentLayout.Thermal80mm;

        return Content(InvoiceDocumentHtmlBuilder.Build(model, layout), "text/html");
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
