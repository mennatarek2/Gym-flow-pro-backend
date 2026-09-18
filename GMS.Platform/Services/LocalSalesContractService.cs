namespace GMS.Platform.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

public class LocalSalesContractService : ILocalSalesContractService
{
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly PlatformDbContext _db;
    private readonly IPlatformAuditService _audit;

    public LocalSalesContractService(PlatformDbContext db, IPlatformAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<LocalSalesContractTermsDto> GetTermsAsync(CancellationToken ct = default)
    {
        var row = await EnsureTermsAsync(ct);
        return ToTermsDto(row);
    }

    public async Task<LocalSalesContractTermsDto> UpdateTermsAsync(UpsertLocalSalesContractTermsRequest request, Guid actorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.TermsEn) || string.IsNullOrWhiteSpace(request.TermsAr))
            throw new ArgumentException("English and Arabic terms are required.");

        var row = await EnsureTermsAsync(ct);
        row.TermsEn = request.TermsEn.Trim();
        row.TermsAr = request.TermsAr.Trim();
        row.UpdatedByPlatformAdminUserId = actorId;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(actorId, "platform.local_sales_contract_terms.updated", after: new { row.Id, row.UpdatedAtUtc });
        return ToTermsDto(row);
    }

    public async Task<List<LocalSalesContractDocumentDto>> ListIssuedAsync(Guid? customerId, Guid? contractId, CancellationToken ct = default)
    {
        var q = _db.LocalSalesContractDocuments.AsNoTracking().AsQueryable();
        if (customerId is Guid cid) q = q.Where(d => d.CustomerId == cid);
        if (contractId is Guid ctid) q = q.Where(d => d.ContractId == ctid);
        var rows = await q.OrderByDescending(d => d.IssuedAtUtc).ToListAsync(ct);
        return rows.Select(ToDocDto).ToList();
    }

    public async Task<LocalSalesContractDocumentDto?> GetIssuedAsync(Guid contractId, CancellationToken ct = default)
    {
        var row = await _db.LocalSalesContractDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.ContractId == contractId, ct);
        return row == null ? null : ToDocDto(row);
    }

    public async Task<LocalSalesContractHtmlDto> PreviewAsync(Guid contractId, string? language, CancellationToken ct = default)
    {
        var lang = LocalSalesContractLanguages.Normalize(language);
        var snapshot = await BuildLiveSnapshotAsync(contractId, lang, ct);
        return new LocalSalesContractHtmlDto
        {
            ContractId = contractId,
            CustomerId = Guid.Empty,
            ContractNumber = snapshot.ContractNumber,
            Language = lang,
            Status = "preview",
            Html = LocalSalesContractHtmlBuilder.Build(snapshot),
            Issued = false,
        };
    }

    public async Task<LocalSalesContractHtmlDto> IssueAsync(Guid contractId, string? language, Guid actorId, CancellationToken ct = default)
    {
        var existing = await _db.LocalSalesContractDocuments
            .FirstOrDefaultAsync(d => d.ContractId == contractId, ct);
        if (existing != null)
            throw new InvalidOperationException("This contract already has an issued sales contract. Reprint instead.");

        var contract = await _db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId, ct)
            ?? throw new ArgumentException("Contract was not found.");
        if (string.Equals(contract.Status, PlatformContractStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot issue a sales contract for a cancelled sale.");

        var lang = LocalSalesContractLanguages.Normalize(language);
        var snapshot = await BuildLiveSnapshotAsync(contractId, lang, ct);

        var doc = new LocalSalesContractDocument
        {
            ContractId = contract.Id,
            CustomerId = contract.CustomerId,
            ContractNumber = contract.ContractNumber,
            Language = lang,
            Status = LocalSalesContractDocumentStatuses.Issued,
            IssuedAtUtc = DateTime.UtcNow,
            IssuedByPlatformAdminUserId = actorId,
            PrintCount = 0,
            SnapshotJson = JsonSerializer.Serialize(snapshot, JsonOpts),
        };
        _db.LocalSalesContractDocuments.Add(doc);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(actorId, "platform.local_sales_contract.issued", after: new { doc.Id, doc.ContractNumber, doc.Language, snapshot.PaidAmount, snapshot.OutstandingAmount });
        return ToHtmlDto(doc, snapshot, issued: true);
    }

    public async Task<LocalSalesContractHtmlDto?> GetIssuedHtmlAsync(Guid contractId, CancellationToken ct = default)
    {
        var row = await _db.LocalSalesContractDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.ContractId == contractId, ct);
        if (row == null) return null;
        var snapshot = JsonSerializer.Deserialize<LocalSalesContractSnapshot>(row.SnapshotJson, JsonOpts)
            ?? throw new InvalidOperationException("Issued contract snapshot could not be read.");
        return ToHtmlDto(row, snapshot, issued: true);
    }

    public async Task<LocalSalesContractHtmlDto> ReprintAsync(Guid contractId, Guid actorId, CancellationToken ct = default)
    {
        var row = await _db.LocalSalesContractDocuments.FirstOrDefaultAsync(d => d.ContractId == contractId, ct)
            ?? throw new InvalidOperationException("No issued sales contract exists to reprint.");
        row.PrintCount += 1;
        row.LastPrintedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(actorId, "platform.local_sales_contract.reprinted", after: new { row.Id, row.ContractNumber, row.PrintCount });
        var snapshot = JsonSerializer.Deserialize<LocalSalesContractSnapshot>(row.SnapshotJson, JsonOpts)
            ?? throw new InvalidOperationException("Issued contract snapshot could not be read.");
        return ToHtmlDto(row, snapshot, issued: true);
    }

    async Task<LocalSalesContractSnapshot> BuildLiveSnapshotAsync(Guid contractId, string language, CancellationToken ct)
    {
        var contract = await _db.Contracts.AsNoTracking()
            .Include(c => c.Items)
            .Include(c => c.Payments)
            .Include(c => c.Customer)
            .FirstOrDefaultAsync(c => c.Id == contractId, ct)
            ?? throw new ArgumentException("Contract was not found.");

        var customer = contract.Customer
            ?? await _db.Customers.AsNoTracking().FirstAsync(c => c.Id == contract.CustomerId, ct);

        var paid = Money(contract.Payments.Sum(p => p.Amount));
        var outstanding = Math.Max(0, Money(contract.Total) - paid);

        var license = await _db.LocalLicenses.AsNoTracking()
            .Where(l => l.ContractId == contract.Id)
            .OrderByDescending(l => l.IssuedAtUtc)
            .FirstOrDefaultAsync(ct);

        string? gymCode = null;
        LocalSalesContractLicenseSnap? licenseSnap = null;
        if (license != null)
        {
            var install = await _db.LocalInstallations.AsNoTracking()
                .Where(i => i.LicenseId == license.Id)
                .OrderByDescending(i => i.Status == LocalInstallationStatuses.Active)
                .ThenByDescending(i => i.LastValidatedAtUtc)
                .FirstOrDefaultAsync(ct);
            gymCode = install?.GymCode;
            licenseSnap = new LocalSalesContractLicenseSnap
            {
                Edition = license.Edition,
                DeviceLimit = license.DeviceLimit,
                LicenseReference = LocalSalesContractHtmlBuilder.MaskLicenseKey(license.LicenseKey),
                IssuedOn = (license.IssuedAtUtc ?? license.CreatedAtUtc).ToString("yyyy-MM-dd"),
                GymCode = gymCode,
            };
        }

        var terms = await EnsureTermsAsync(ct);
        var termsText = language == LocalSalesContractLanguages.Ar ? terms.TermsAr : terms.TermsEn;

        return new LocalSalesContractSnapshot
        {
            ContractNumber = contract.ContractNumber,
            IssuedOn = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            Language = language,
            SellerName = "HyMotion",
            Customer = new LocalSalesContractCustomerSnap
            {
                BusinessName = customer.BusinessName,
                OwnerName = customer.OwnerName,
                Phone = customer.Phone ?? customer.WhatsApp,
                Email = customer.Email,
                Address = customer.Address,
                GymCode = gymCode,
            },
            Items = contract.Items.OrderBy(i => i.SortOrder).Select(i => new LocalSalesContractItemSnap
            {
                Sku = i.SkuSnapshot,
                Name = i.NameSnapshot,
                ProductType = i.ProductTypeSnapshot,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                DiscountAmount = i.DiscountAmount,
                LineTotal = i.LineTotal,
            }).ToList(),
            Subtotal = contract.Subtotal,
            Discount = contract.Discount,
            Total = contract.Total,
            PaidAmount = paid,
            OutstandingAmount = outstanding,
            PaymentStatus = PlatformContractPaymentStatuses.FromAmounts(contract.Total, paid),
            Currency = contract.Currency,
            Payments = contract.Payments
                .OrderBy(p => p.PaymentDate)
                .ThenBy(p => p.CreatedAtUtc)
                .Select(p => new LocalSalesContractPaymentSnap
                {
                    PaymentDate = p.PaymentDate.ToString("yyyy-MM-dd"),
                    PaymentMethod = p.PaymentMethod,
                    Reference = p.Reference,
                    Amount = p.Amount,
                }).ToList(),
            License = licenseSnap,
            Terms = termsText,
        };
    }

    async Task<LocalSalesContractTerms> EnsureTermsAsync(CancellationToken ct)
    {
        var row = await _db.LocalSalesContractTerms.OrderBy(t => t.UpdatedAtUtc).FirstOrDefaultAsync(ct);
        if (row != null) return row;
        row = new LocalSalesContractTerms
        {
            TermsEn = LocalSalesContractTermsDefaults.En,
            TermsAr = LocalSalesContractTermsDefaults.Ar,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        _db.LocalSalesContractTerms.Add(row);
        await _db.SaveChangesAsync(ct);
        return row;
    }

    static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    static LocalSalesContractTermsDto ToTermsDto(LocalSalesContractTerms row) => new()
    {
        TermsEn = row.TermsEn,
        TermsAr = row.TermsAr,
        UpdatedAtUtc = row.UpdatedAtUtc,
    };

    static LocalSalesContractDocumentDto ToDocDto(LocalSalesContractDocument row) => new()
    {
        Id = row.Id,
        ContractId = row.ContractId,
        CustomerId = row.CustomerId,
        ContractNumber = row.ContractNumber,
        Language = row.Language,
        Status = row.Status,
        IssuedAtUtc = row.IssuedAtUtc,
        PrintCount = row.PrintCount,
        LastPrintedAtUtc = row.LastPrintedAtUtc,
    };

    static LocalSalesContractHtmlDto ToHtmlDto(LocalSalesContractDocument row, LocalSalesContractSnapshot snapshot, bool issued) => new()
    {
        Id = row.Id,
        ContractId = row.ContractId,
        CustomerId = row.CustomerId,
        ContractNumber = row.ContractNumber,
        Language = row.Language,
        Status = row.Status,
        IssuedAtUtc = row.IssuedAtUtc,
        PrintCount = row.PrintCount,
        LastPrintedAtUtc = row.LastPrintedAtUtc,
        Html = LocalSalesContractHtmlBuilder.Build(snapshot),
        Issued = issued,
    };
}
