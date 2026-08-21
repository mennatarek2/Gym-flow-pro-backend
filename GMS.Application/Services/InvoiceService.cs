namespace GMS.Application.Services;

using System.Data;
using System.Text.Json;
using Hangfire;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Invoices;
using GMS.Application.Interfaces;
using GMS.Application.Jobs;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Legal invoice engine: gap-free sequential numbering (per tenant/year/type, via
/// <see cref="InvoiceSequence"/>), idempotent creation, and voiding.
/// </summary>
public class InvoiceService : IInvoiceService
{
    private static readonly TimeZoneInfo CairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    private readonly GymFlowProDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(GymFlowProDbContext dbContext, IAuditService auditService, ILogger<InvoiceService> logger)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task EnqueueForSale(Guid saleId)
    {
        // Called from SaleService's post-commit step (proper ambient tenant context), but this
        // check is self-contained so EnqueueForSale is correct regardless of caller.
        var total = await _dbContext.Sales
            .IgnoreQueryFilters()
            .Where(s => s.Id == saleId)
            .Select(s => (decimal?)s.Total)
            .FirstOrDefaultAsync();

        if (total is null or 0m)
        {
            _logger.LogInformation("EnqueueForSale: skipping sale {SaleId} (zero-total / not found)", saleId);
            return;
        }

        BackgroundJob.Enqueue<CreateInvoiceForSaleJob>(job => job.ExecuteAsync(saleId));
    }

    public async Task CreateForSaleAsync(Guid saleId)
    {
        // 50 concurrent callers all UPDLOCK-ing the SAME invoice_sequences row for the whole
        // duration of the enclosing transaction is a textbook SQL Server deadlock scenario —
        // confirmed empirically (error 1205, "chosen as the deadlock victim") under a 50-way
        // concurrency test. Hangfire's own AutomaticRetry on the enclosing job normally covers
        // this, but retrying here too makes the method robust for any caller, not just that job.
        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await CreateForSaleOnceAsync(saleId);
                return;
            }
            catch (Exception ex) when (IsDeadlockVictim(ex) && attempt < maxAttempts)
            {
                _logger.LogWarning(
                    "CreateForSaleAsync: deadlock victim on attempt {Attempt}/{MaxAttempts} for sale {SaleId} — retrying",
                    attempt, maxAttempts, saleId);

                // The failed attempt's tracked (but rolled-back) Invoice entity must be cleared
                // before retrying, or SaveChangesAsync would try to insert it a second time too.
                _dbContext.ChangeTracker.Clear();
                await Task.Delay(Random.Shared.Next(50, 250));
            }
        }
    }

    private async Task CreateForSaleOnceAsync(Guid saleId)
    {
        // Hangfire job scope has no ambient ITenantContext — IgnoreQueryFilters throughout and
        // resolve/filter by TenantId explicitly instead.
        var sale = await _dbContext.Sales
            .IgnoreQueryFilters()
            .Include(s => s.Member)
            .FirstOrDefaultAsync(s => s.Id == saleId);

        if (sale == null)
        {
            _logger.LogWarning("CreateForSaleAsync: sale {SaleId} not found", saleId);
            return;
        }

        var tenantId = sale.TenantId;
        const string type = "invoice";

        // Idempotency check.
        var existing = await _dbContext.Invoices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.SaleId == saleId && i.Type == type);

        if (existing != null)
        {
            _logger.LogInformation(
                "CreateForSaleAsync: invoice already exists for sale {SaleId} ({InvoiceNumber}) — no-op",
                saleId, existing.InvoiceNumber);
            return;
        }

        var lines = await _dbContext.SaleLines
            .IgnoreQueryFilters()
            .Where(l => l.SaleId == saleId)
            .Select(l => new { l.LineType, l.Description, l.DescriptionAr, l.Qty, l.UnitPrice, l.LineTotal })
            .ToListAsync();

        var linesSnapshot = JsonSerializer.Serialize(lines);

        var tenant = await _dbContext.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tenantId);
        var configuredVatRate = GetSettingDecimal(tenant?.Settings, TenantSettingsKeys.VatRate, 0m);
        // Record the rate actually applied to this sale (0 when no tax was charged), not just
        // whatever the tenant's VAT rate happens to be configured to right now.
        var vatRate = sale.TaxAmount > 0 ? configuredVatRate : 0m;

        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTimeZone);
        var year = today.Year;

        var isRelational = _dbContext.Database.IsRelational();
        var transaction = isRelational
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted)
            : null;

        try
        {
            var number = await AllocateNextNumberAsync(tenantId, year, type);
            var prefix = type == "invoice" ? "INV" : "CN";
            var invoiceNumber = $"{prefix}-{year}-{number:D6}";

            var invoice = new Invoice
            {
                TenantId = tenantId,
                Type = type,
                InvoiceNumber = invoiceNumber,
                SaleId = sale.Id,
                MemberNameSnapshot = sale.Member?.FullName ?? string.Empty,
                MemberPhoneSnapshot = sale.Member?.PhoneNumber ?? string.Empty,
                LinesSnapshot = linesSnapshot,
                Subtotal = sale.Subtotal,
                DiscountAmount = sale.DiscountAmount,
                VatRate = vatRate,
                VatAmount = sale.TaxAmount,
                Total = sale.Total,
                Currency = "EGP",
                IssuedAt = DateTime.UtcNow,
                Status = "issued"
            };

            _dbContext.Invoices.Add(invoice);
            await _dbContext.SaveChangesAsync();

            if (transaction != null)
                await transaction.CommitAsync();

            _logger.LogInformation("Invoice {InvoiceNumber} created for sale {SaleId}", invoiceNumber, saleId);
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync();
            throw; // let Hangfire's AutomaticRetry handle it
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
    }

    public async Task<Result<PagedResult<InvoiceDto>>> GetPagedAsync(Guid tenantId, InvoiceQueryRequest query)
    {
        try
        {
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);

            var q = _dbContext.Invoices.Where(i => i.TenantId == tenantId);

            if (query.From.HasValue)
                q = q.Where(i => i.IssuedAt >= query.From.Value);

            if (query.To.HasValue)
                q = q.Where(i => i.IssuedAt <= query.To.Value);

            if (query.MemberId.HasValue)
                q = q.Where(i => i.Sale != null && i.Sale.MemberId == query.MemberId.Value);

            if (!string.IsNullOrWhiteSpace(query.Status))
                q = q.Where(i => i.Status == query.Status);

            if (!string.IsNullOrWhiteSpace(query.Type))
                q = q.Where(i => i.Type == query.Type);

            if (!string.IsNullOrWhiteSpace(query.LineType))
            {
                var lineType = query.LineType.Trim();
                q = q.Where(i =>
                    i.Sale != null && i.Sale.Lines.Any(l => l.LineType == lineType));
            }

            var totalCount = await q.CountAsync();

            var items = await q
                .OrderByDescending(i => i.IssuedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(i => ToDto(i))
                .ToListAsync();

            return Result<PagedResult<InvoiceDto>>.Success(new PagedResult<InvoiceDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving invoices for tenant {TenantId}", tenantId);
            return Result<PagedResult<InvoiceDto>>.Failure("Failed to retrieve invoices / فشل جلب الفواتير", ex.Message);
        }
    }

    public async Task<Result<InvoiceDto>> GetByIdAsync(Guid id)
    {
        var invoice = await _dbContext.Invoices.FirstOrDefaultAsync(i => i.Id == id);
        if (invoice == null)
            return Result<InvoiceDto>.Failure("Invoice not found / الفاتورة غير موجودة");

        return Result<InvoiceDto>.Success(ToDto(invoice));
    }

    public async Task<Result<bool>> ResendAsync(Guid invoiceId)
    {
        var invoice = await _dbContext.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId);
        if (invoice == null)
            return Result<bool>.Failure("Invoice not found / الفاتورة غير موجودة");

        BackgroundJob.Enqueue<IInvoiceDeliveryJob>(job => job.Execute(invoiceId));

        return Result<bool>.Success(true, "Invoice delivery re-enqueued / تم إعادة جدولة إرسال الفاتورة");
    }

    public async Task<Result<Guid>> CreateCreditNoteAsync(Guid refundId)
    {
        try
        {
            // IgnoreQueryFilters: this may run with no ambient tenant context (future job scope),
            // same defensive posture as CreateForSaleOnceAsync.
            var refund = await _dbContext.Refunds.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == refundId);
            if (refund == null)
                return Result<Guid>.Failure("Refund not found / طلب الاسترداد غير موجود");

            // Idempotent: a retry (or a second call) just returns the credit note already issued.
            if (refund.CreditNoteInvoiceId.HasValue)
                return Result<Guid>.Success(refund.CreditNoteInvoiceId.Value);

            var tenantId = refund.TenantId;

            var sale = await _dbContext.Sales
                .IgnoreQueryFilters()
                .Include(s => s.Member)
                .FirstOrDefaultAsync(s => s.Id == refund.SaleId);

            if (sale == null)
                return Result<Guid>.Failure("Sale not found / عملية البيع غير موجودة");

            var originalInvoice = await _dbContext.Invoices
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.SaleId == refund.SaleId && i.Type == "invoice");

            // Positive-valued, per standard credit-note convention: it records the value credited
            // back, not a negative line item. Net revenue reconciles as
            // SUM(invoices.Total WHERE Type='invoice') - SUM(invoices.Total WHERE Type='credit_note').
            var linesSnapshot = JsonSerializer.Serialize(new[]
            {
                new
                {
                    LineType = "refund",
                    Description = $"Refund: {refund.Reason}",
                    DescriptionAr = (string?)null,
                    Qty = 1,
                    UnitPrice = refund.Amount,
                    LineTotal = refund.Amount
                }
            });

            var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTimeZone);
            var year = today.Year;
            const string type = "credit_note";

            var isRelational = _dbContext.Database.IsRelational();
            var transaction = isRelational
                ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted)
                : null;

            try
            {
                var number = await AllocateNextNumberAsync(tenantId, year, type);
                var invoiceNumber = $"CN-{year}-{number:D6}";

                var invoice = new Invoice
                {
                    TenantId = tenantId,
                    Type = type,
                    InvoiceNumber = invoiceNumber,
                    SaleId = refund.SaleId,
                    RefundId = refund.Id,
                    OriginalInvoiceId = originalInvoice?.Id,
                    MemberNameSnapshot = sale.Member?.FullName ?? string.Empty,
                    MemberPhoneSnapshot = sale.Member?.PhoneNumber ?? string.Empty,
                    LinesSnapshot = linesSnapshot,
                    Subtotal = refund.Amount,
                    DiscountAmount = 0m,
                    VatRate = 0m,
                    VatAmount = 0m,
                    Total = refund.Amount,
                    Currency = "EGP",
                    IssuedAt = DateTime.UtcNow,
                    Status = "issued"
                };

                _dbContext.Invoices.Add(invoice);
                refund.CreditNoteInvoiceId = invoice.Id;
                refund.UpdatedAtUtc = DateTime.UtcNow;

                await _dbContext.SaveChangesAsync();

                if (transaction != null)
                    await transaction.CommitAsync();

                _logger.LogInformation("Credit note {InvoiceNumber} created for refund {RefundId}", invoiceNumber, refundId);

                return Result<Guid>.Success(invoice.Id);
            }
            catch
            {
                if (transaction != null)
                    await transaction.RollbackAsync();
                throw;
            }
            finally
            {
                if (transaction != null)
                    await transaction.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating credit note for refund {RefundId}", refundId);
            return Result<Guid>.Failure("Failed to create credit note / فشل إنشاء إشعار دائن", ex.Message);
        }
    }

    public async Task<Result<bool>> VoidAsync(Guid invoiceId, string reason, Guid voidedByUserId)
    {
        try
        {
            var invoice = await _dbContext.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId);
            if (invoice == null)
                return Result<bool>.Failure("Invoice not found / الفاتورة غير موجودة");

            if (invoice.Status == "voided")
                return Result<bool>.Failure("Invoice is already voided / الفاتورة ملغاة بالفعل");

            // voidedByUserId is the JWT sub (ApplicationUser.Id) — resolve to app_users.Id, same
            // distinction CheckinService/SaleService rely on.
            var voidedByUserIdStr = voidedByUserId.ToString();
            var appUser = await _dbContext.AppUsers.FirstOrDefaultAsync(u => u.UserId == voidedByUserIdStr);
            if (appUser == null)
                return Result<bool>.Failure("Staff user not found / المستخدم غير موجود");

            invoice.Status = "voided";
            invoice.VoidReason = reason;
            invoice.VoidedByUserId = appUser.Id;
            invoice.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            await _auditService.LogAsync("invoice.void", "Invoice", invoiceId, null, new { reason });

            return Result<bool>.Success(true, "Invoice voided / تم إلغاء الفاتورة");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error voiding invoice {InvoiceId}", invoiceId);
            return Result<bool>.Failure("Failed to void invoice / فشل إلغاء الفاتورة", ex.Message);
        }
    }

    public async Task<Result<PaymentReceiptInfoDto>> GetPaymentInfoAsync(Guid paymentTransactionId)
    {
        var payment = await _dbContext.PaymentTransactions
            .FirstOrDefaultAsync(p => p.Id == paymentTransactionId);

        if (payment == null)
            return Result<PaymentReceiptInfoDto>.Failure("Payment not found / الدفعة غير موجودة");

        return Result<PaymentReceiptInfoDto>.Success(new PaymentReceiptInfoDto
        {
            Amount = payment.Amount,
            PaidAtUtc = payment.PaidAtUtc,
            Method = payment.Method
        });
    }

    public async Task<Result<Guid>> GetOriginalInvoiceIdForSaleAsync(Guid saleId)
    {
        var invoiceId = await _dbContext.Invoices
            .Where(i => i.SaleId == saleId && i.Type == "invoice")
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync();

        if (invoiceId == null)
            return Result<Guid>.Failure("No invoice exists for this sale / لا توجد فاتورة لعملية البيع هذه");

        return Result<Guid>.Success(invoiceId.Value);
    }

    // ========================================================================
    // PRIVATE HELPERS
    // ========================================================================

    /// <summary>
    /// Atomically increments and returns invoice_sequences.LastNumber for (tenantId, year, type),
    /// creating the row on first use. The UPDATE...OUTPUT is retried if the fallback INSERT loses
    /// a race to create that very first row — this only matters for the first-ever invoice issued
    /// for a given tenant/year/type; every subsequent call finds the row and just increments it.
    /// </summary>
    private async Task<int> AllocateNextNumberAsync(Guid tenantId, int year, string type)
    {
        const int maxAttempts = 10;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var results = await _dbContext.Database
                .SqlQuery<int>(
                    $"UPDATE invoice_sequences WITH (UPDLOCK) SET LastNumber = LastNumber + 1 OUTPUT INSERTED.LastNumber WHERE TenantId = {tenantId} AND Year = {year} AND Type = {type}")
                .ToListAsync();

            if (results.Count > 0)
                return results[0];

            try
            {
                await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO invoice_sequences (Id, TenantId, Year, Type, LastNumber) VALUES ({Guid.NewGuid()}, {tenantId}, {year}, {type}, 1)");

                return 1;
            }
            catch (Exception ex) when (IsUniqueConstraintViolation(ex))
            {
                // Another concurrent caller won the race to create this row first — loop back and
                // let the UPDATE...OUTPUT branch pick it up atomically on the next attempt.
            }
        }

        throw new InvalidOperationException(
            $"Could not allocate an invoice number for tenant {tenantId}/{year}/{type} after {maxAttempts} attempts.");
    }

    private static InvoiceDto ToDto(Invoice invoice) => new()
    {
        Id = invoice.Id,
        Type = invoice.Type,
        InvoiceNumber = invoice.InvoiceNumber,
        SaleId = invoice.SaleId,
        OriginalInvoiceId = invoice.OriginalInvoiceId,
        MemberNameSnapshot = invoice.MemberNameSnapshot,
        MemberPhoneSnapshot = invoice.MemberPhoneSnapshot,
        Lines = ParseLinesSnapshot(invoice.LinesSnapshot),
        Subtotal = invoice.Subtotal,
        DiscountAmount = invoice.DiscountAmount,
        VatRate = invoice.VatRate,
        VatAmount = invoice.VatAmount,
        Total = invoice.Total,
        Currency = invoice.Currency,
        IssuedAt = invoice.IssuedAt,
        PdfUrl = invoice.PdfUrl,
        Status = invoice.Status,
        VoidReason = invoice.VoidReason
    };

    private static List<InvoiceLineSnapshotDto> ParseLinesSnapshot(string? linesSnapshot)
    {
        if (string.IsNullOrWhiteSpace(linesSnapshot))
            return new List<InvoiceLineSnapshotDto>();

        try
        {
            return JsonSerializer.Deserialize<List<InvoiceLineSnapshotDto>>(linesSnapshot)
                ?? new List<InvoiceLineSnapshotDto>();
        }
        catch (JsonException)
        {
            return new List<InvoiceLineSnapshotDto>();
        }
    }

    private static bool IsDeadlockVictim(Exception ex) =>
        ex is SqlException { Number: 1205 } ||
        ex is DbUpdateException { InnerException: SqlException { Number: 1205 } };

    private static bool IsUniqueConstraintViolation(Exception ex) =>
        ex is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601) ||
        ex is DbUpdateException { InnerException: SqlException inner } && (inner.Number == 2627 || inner.Number == 2601);

    private static decimal GetSettingDecimal(string? settingsJson, string key, decimal defaultValue)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
            return defaultValue;

        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            return doc.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetDecimal()
                : defaultValue;
        }
        catch (JsonException)
        {
            return defaultValue;
        }
    }
}
