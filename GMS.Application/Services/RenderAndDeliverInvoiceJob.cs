namespace GMS.Application.Services;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Interfaces;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Real IInvoiceDeliveryJob implementation: renders the invoice PDF, uploads it, sends it to the
/// member via WhatsApp, and notifies the staff member who made the sale. Idempotent across
/// Hangfire retries — skips re-render/re-upload once Invoice.PdfUrl is already set, so a retry
/// only re-attempts whichever step actually failed.
/// </summary>
public class RenderAndDeliverInvoiceJob : IInvoiceDeliveryJob
{
    private readonly GymFlowProDbContext _dbContext;
    private readonly IInvoicePdfRenderer _pdfRenderer;
    private readonly IFileStorageService _fileStorageService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<RenderAndDeliverInvoiceJob> _logger;

    public RenderAndDeliverInvoiceJob(
        GymFlowProDbContext dbContext,
        IInvoicePdfRenderer pdfRenderer,
        IFileStorageService fileStorageService,
        IWhatsAppService whatsAppService,
        INotificationService notificationService,
        ILogger<RenderAndDeliverInvoiceJob> logger)
    {
        _dbContext = dbContext;
        _pdfRenderer = pdfRenderer;
        _fileStorageService = fileStorageService;
        _whatsAppService = whatsAppService;
        _notificationService = notificationService;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 2, 4, 8 })]
    public async Task Execute(Guid invoiceId)
    {
        try
        {
            // Hangfire job scope has no ambient ITenantContext — IgnoreQueryFilters throughout.
            var invoice = await _dbContext.Invoices.IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => i.Id == invoiceId);

            if (invoice == null)
            {
                _logger.LogWarning("RenderAndDeliverInvoiceJob: invoice {InvoiceId} not found", invoiceId);
                return;
            }

            var tenant = await _dbContext.Tenants.IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == invoice.TenantId);

            if (!string.IsNullOrWhiteSpace(invoice.PdfUrl))
            {
                _logger.LogInformation(
                    "RenderAndDeliverInvoiceJob: PDF already rendered for {InvoiceNumber} — resuming at delivery",
                    invoice.InvoiceNumber);
            }
            else
            {
                var model = InvoicePdfModelFactory.FromEntity(invoice, tenant);
                if (!string.IsNullOrWhiteSpace(tenant?.LogoUrl))
                {
                    var logoBytes = await _fileStorageService.TryReadAsync(tenant.LogoUrl);
                    InvoicePdfModelFactory.AttachLogo(model, logoBytes, tenant.LogoUrl);
                }
                var pdfBytes = _pdfRenderer.Render(model);

                await using var stream = new MemoryStream(pdfBytes);
                var folder = $"invoices/{invoice.TenantId}/{invoice.IssuedAt:yyyy}";
                var url = await _fileStorageService.UploadAsync(stream, $"{invoice.InvoiceNumber}.pdf", folder);

                invoice.PdfUrl = url;
                invoice.UpdatedAtUtc = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }

            if (!string.IsNullOrWhiteSpace(invoice.MemberPhoneSnapshot))
            {
                var caption = $"Your invoice {invoice.InvoiceNumber} is attached.";
                var captionAr = $"فاتورتك {invoice.InvoiceNumber} مرفقة.";
                await _whatsAppService.SendDocumentAsync(
                    invoice.MemberPhoneSnapshot, invoice.MemberNameSnapshot, invoice.PdfUrl!, caption, captionAr);
            }

            var soldByUserId = await _dbContext.Sales.IgnoreQueryFilters()
                .Where(s => s.Id == invoice.SaleId)
                .Select(s => (Guid?)s.SoldByUserId)
                .FirstOrDefaultAsync();

            if (soldByUserId.HasValue)
            {
                await _notificationService.CreateForStaffAsync(
                    invoice.TenantId, soldByUserId.Value,
                    $"Invoice {invoice.InvoiceNumber} delivered",
                    $"تم تسليم الفاتورة {invoice.InvoiceNumber}",
                    $"Invoice {invoice.InvoiceNumber} for {invoice.MemberNameSnapshot} was delivered.",
                    $"تم تسليم الفاتورة {invoice.InvoiceNumber} للعضو {invoice.MemberNameSnapshot}.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RenderAndDeliverInvoiceJob failed for invoice {InvoiceId}", invoiceId);
            throw; // let Hangfire's AutomaticRetry handle it
        }
    }
}
