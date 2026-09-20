namespace GMS.Platform.Services;

using Microsoft.EntityFrameworkCore;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Helpers;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

public class PlatformCustomerService : IPlatformCustomerService
{
    private readonly PlatformDbContext _db;
    private readonly IPlatformAuditService _audit;
    private readonly ILocalLicenseService _licenses;

    public PlatformCustomerService(
        PlatformDbContext db,
        IPlatformAuditService audit,
        ILocalLicenseService licenses)
    {
        _db = db;
        _audit = audit;
        _licenses = licenses;
    }

    public async Task<List<PlatformCustomerListItemDto>> ListCustomersAsync(string? status, CancellationToken ct = default)
    {
        var q = _db.Customers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(c => c.Status == status);
        var rows = await q.OrderBy(c => c.BusinessName).ToListAsync(ct);
        var openCounts = await _db.SupportTickets.AsNoTracking()
            .Where(t => t.Status != PlatformSupportTicketStatuses.Resolved && t.Status != PlatformSupportTicketStatuses.Closed)
            .GroupBy(t => t.CustomerId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return rows.Select(c => ToListItem(c, openCounts.GetValueOrDefault(c.Id))).ToList();
    }

    public async Task<PlatformCustomerDetailDto?> GetCustomerAsync(Guid id, CancellationToken ct = default)
    {
        var c = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return c == null ? null : ToDetail(c);
    }

    public async Task<PlatformCustomerProfileDto?> GetProfileAsync(Guid id, bool includeFullLicenseKey = false, CancellationToken ct = default)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (customer == null) return null;

        var contracts = await _db.Contracts.AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .Where(x => x.CustomerId == id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        var latest = contracts.FirstOrDefault();
        var paid = contracts.SelectMany(c => c.Payments).Sum(p => p.Amount);
        var total = contracts.Where(c => c.Status != PlatformContractStatuses.Cancelled).Sum(c => c.Total);
        var purchased = contracts
            .Where(c => c.Status != PlatformContractStatuses.Cancelled)
            .SelectMany(c => c.Items)
            .OrderBy(i => i.NameSnapshot)
            .Select(ToItemDto)
            .ToList();

        LocalLicenseListItemDto? licenseDto = null;
        var licenseDtos = new List<LocalLicenseListItemDto>();
        LocalInstallationDto? installationDto = null;
        string? lastValidation = null;
        var licenseRows = await _db.LocalLicenses.AsNoTracking()
            .Where(l => l.CustomerId == id)
            .OrderByDescending(l => l.CreatedAtUtc)
            .ToListAsync(ct);
        foreach (var license in licenseRows)
        {
            var detail = await _licenses.GetDetailAsync(license.Id, includeFullLicenseKey, ct);
            if (detail == null) continue;
            licenseDtos.Add(detail);
        }
        licenseDto = PickPreferredLicense(licenseDtos);
        if (licenseDto is LocalLicenseDetailDto preferredDetail)
        {
            installationDto = preferredDetail.Installations.FirstOrDefault(i => i.Status == LocalInstallationStatuses.Active)
                ?? preferredDetail.Installations.FirstOrDefault();
            lastValidation = installationDto?.LastValidatedAtUtc?.ToString("o");
        }
        else if (licenseDto != null)
        {
            var detail = await _licenses.GetDetailAsync(licenseDto.Id, includeFullLicenseKey, ct);
            installationDto = detail?.Installations.FirstOrDefault(i => i.Status == LocalInstallationStatuses.Active)
                ?? detail?.Installations.FirstOrDefault();
            lastValidation = installationDto?.LastValidatedAtUtc?.ToString("o");
        }

        var openTickets = await _db.SupportTickets.AsNoTracking()
            .CountAsync(t => t.CustomerId == id
                && t.Status != PlatformSupportTicketStatuses.Resolved
                && t.Status != PlatformSupportTicketStatuses.Closed, ct);

        return new PlatformCustomerProfileDto
        {
            Customer = ToDetail(customer),
            LatestContract = latest == null ? null : ToContractDto(latest, customer.BusinessName),
            PurchasedItems = purchased,
            ContractTotal = total,
            PaidAmount = paid,
            OutstandingAmount = Math.Max(0, total - paid),
            PaymentStatus = PlatformContractPaymentStatuses.FromAmounts(total, paid),
            License = licenseDto,
            Licenses = licenseDtos,
            Installation = installationDto,
            OpenSupportTicketCount = openTickets,
            LastValidation = lastValidation,
        };
    }

    public async Task<PlatformCustomerDetailDto> CreateCustomerAsync(UpsertPlatformCustomerRequest request, Guid actorId, CancellationToken ct = default)
    {
        var customer = new PlatformCustomer();
        ApplyCustomer(customer, request, isCreate: true, actorId);
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(actorId, "platform.customer.created", after: new { customer.Id, customer.BusinessName });
        return ToDetail(customer);
    }

    public async Task<PlatformCustomerDetailDto?> UpdateCustomerAsync(Guid id, UpsertPlatformCustomerRequest request, Guid actorId, CancellationToken ct = default)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (customer == null) return null;
        var before = new
        {
            customer.Id,
            customer.BusinessName,
            customer.Status,
            customer.Email,
            customer.TenantId,
        };
        ApplyCustomer(customer, request, isCreate: false, actorId);
        customer.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(
            actorId,
            "platform.customer.updated",
            tenantId: customer.TenantId,
            before: before,
            after: new
            {
                customer.Id,
                customer.BusinessName,
                customer.Status,
                customer.Email,
                customer.TenantId,
            });
        return ToDetail(customer);
    }

    public async Task<PlatformCustomerDetailDto?> SetCustomerCloudLinkAsync(Guid id, Guid? tenantId, Guid actorId, CancellationToken ct = default)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (customer == null) return null;

        if (tenantId is Guid tid)
        {
            var taken = await _db.Customers.AsNoTracking()
                .AnyAsync(c => c.TenantId == tid && c.Id != id, ct);
            var exists = await CloudGymExistsNotDeletedAsync(tid, ct);
            CloudTenantLinkRules.EnsureCanLink(exists, taken);
        }

        var before = new { customer.Id, customer.TenantId };
        customer.TenantId = tenantId;
        customer.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(
            actorId,
            "platform.customer.cloud_link",
            tenantId: customer.TenantId,
            before: before,
            after: new { customer.Id, customer.TenantId });
        return ToDetail(customer);
    }

    /// <summary>
    /// dbo.tenants lives on the shared gym DB (not platform schema). Match PlatformTenantReadService:
    /// existence + IsDeleted = 0. Non-relational hosts (in-memory tests) skip the SQL probe.
    /// </summary>
    private async Task<bool> CloudGymExistsNotDeletedAsync(Guid tenantId, CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
            return true;

        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) 1
            FROM dbo.tenants
            WHERE Id = @tenantId AND IsDeleted = 0
            """;
        var param = command.CreateParameter();
        param.ParameterName = "@tenantId";
        param.Value = tenantId;
        command.Parameters.Add(param);

        var result = await command.ExecuteScalarAsync(ct);
        return result != null && result != DBNull.Value;
    }

    public async Task<InitiateOwnerPasswordResetResult?> InitiateOwnerPasswordResetAsync(Guid id, string reason, Guid actorId, CancellationToken ct = default)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (customer == null) return null;

        // Existing AdminService.ResetStaffPasswordAsync refuses Owner accounts, and Local gym
        // passwords live on the gym PC — the platform never stores them. We record an audited
        // request; we do not generate, view, or return a password.
        customer.PasswordResetInitiatedAtUtc = DateTime.UtcNow;
        customer.PasswordResetInitiatedByPlatformAdminUserId = actorId;
        customer.OwnerAccountStatus = PlatformOwnerAccountStatuses.ResetRequested;
        customer.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(
            actorId,
            "platform.customer.password_reset_initiated",
            tenantId: customer.TenantId,
            after: new { customer.Id, ownerEmail = customer.OwnerEmail, reason });

        var message = customer.TenantId == null
            ? "Reset recorded. The gym owner password is not stored on the platform — complete the reset on the gym PC with the owner. A password is never returned."
            : "Reset recorded. To set a new Cloud Owner password use POST /platform-api/tenants/{tenantId}/users/{staffId}/reset-password. Local desk passwords stay on the gym PC. A password is never returned.";
        return new InitiateOwnerPasswordResetResult { Initiated = true, Message = message };
    }

    public async Task<List<PlatformCatalogProductDto>> ListProductsAsync(bool includeInactive, CancellationToken ct = default)
    {
        var q = _db.CatalogProducts.AsNoTracking().AsQueryable();
        if (!includeInactive) q = q.Where(p => p.IsActive);
        var rows = await q.OrderBy(p => p.Name).ToListAsync(ct);
        return rows.Select(ToProductDto).ToList();
    }

    public async Task<PlatformCatalogProductDto> CreateProductAsync(UpsertCatalogProductRequest request, Guid actorId, CancellationToken ct = default)
    {
        ValidateProduct(request);
        var product = new PlatformCatalogProduct();
        ApplyProduct(product, request);
        _db.CatalogProducts.Add(product);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(actorId, "platform.catalog_product.created", after: new { product.Id, product.Sku, product.Name });
        return ToProductDto(product);
    }

    public async Task<PlatformCatalogProductDto?> UpdateProductAsync(Guid id, UpsertCatalogProductRequest request, Guid actorId, CancellationToken ct = default)
    {
        ValidateProduct(request);
        var product = await _db.CatalogProducts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product == null) return null;
        var before = new { product.Name, product.DefaultPrice, product.IsActive };
        ApplyProduct(product, request);
        product.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(actorId, "platform.catalog_product.updated", before: before, after: new { product.Name, product.DefaultPrice, product.IsActive });
        return ToProductDto(product);
    }

    public async Task<List<PlatformContractDto>> ListContractsAsync(Guid? customerId, CancellationToken ct = default)
    {
        var q = _db.Contracts.AsNoTracking().Include(c => c.Items).Include(c => c.Payments).Include(c => c.Customer).AsQueryable();
        if (customerId is Guid id) q = q.Where(c => c.CustomerId == id);
        var rows = await q.OrderByDescending(c => c.CreatedAtUtc).ToListAsync(ct);
        return rows.Select(c => ToContractDto(c, c.Customer?.BusinessName)).ToList();
    }

    public async Task<PlatformContractDto?> GetContractAsync(Guid id, CancellationToken ct = default)
    {
        var c = await _db.Contracts.AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .Include(x => x.Customer)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return c == null ? null : ToContractDto(c, c.Customer?.BusinessName);
    }

    public async Task<PlatformContractDto> CreateContractAsync(CreatePlatformContractRequest request, Guid actorId, CancellationToken ct = default)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
            ?? throw new ArgumentException("Customer was not found.");
        if (request.Items == null || request.Items.Count == 0)
            throw new ArgumentException("At least one contract item is required.");

        var contract = new PlatformContract
        {
            CustomerId = customer.Id,
            ContractNumber = await NextNumberAsync("CTR", "HY-CTR", ct),
            ContractDate = request.ContractDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Status = PlatformContractStatuses.Draft,
            Discount = Money(request.Discount),
            Notes = request.Notes?.Trim(),
            CreatedByPlatformAdminUserId = actorId,
        };

        var sort = 0;
        foreach (var input in request.Items)
            contract.Items.Add(await BuildItemAsync(input, sort++, ct));

        Recalc(contract);
        _db.Contracts.Add(contract);
        if (customer.Status == PlatformCustomerStatuses.Prospect)
            customer.Status = PlatformCustomerStatuses.Active;
        customer.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(actorId, "platform.contract.created", after: new { contract.Id, contract.ContractNumber, contract.Total, items = contract.Items.Count });
        return (await GetContractAsync(contract.Id, ct))!;
    }

    public async Task<PlatformContractDto?> ChangeContractStatusAsync(Guid id, string status, Guid actorId, CancellationToken ct = default)
    {
        var contract = await _db.Contracts.Include(c => c.Payments).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract == null) return null;
        var to = status.Trim();
        if (!PlatformContractStatuses.All.Contains(to))
            throw new ArgumentException("Unknown contract status.");
        if (!PlatformContractStatuses.CanTransition(contract.Status, to))
            throw new InvalidOperationException($"Cannot move contract from {contract.Status} to {to}.");
        var from = contract.Status;
        contract.Status = to;
        contract.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(actorId, "platform.contract.status_changed", before: new { from }, after: new { to, contract.ContractNumber });
        return await GetContractAsync(id, ct);
    }

    public async Task<List<PlatformCustomerPaymentDto>> ListPaymentsAsync(Guid? customerId, Guid? contractId, CancellationToken ct = default)
    {
        var q = _db.CustomerPayments.AsNoTracking().Include(p => p.Contract).AsQueryable();
        if (customerId is Guid cid) q = q.Where(p => p.CustomerId == cid);
        if (contractId is Guid ctid) q = q.Where(p => p.ContractId == ctid);
        var rows = await q.OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.CreatedAtUtc).ToListAsync(ct);
        return rows.Select(ToPaymentDto).ToList();
    }

    public async Task<PlatformCustomerPaymentDto> RecordPaymentAsync(RecordCustomerPaymentRequest request, Guid actorId, CancellationToken ct = default)
    {
        if (request.Amount <= 0) throw new ArgumentException("Amount must be positive.");
        if (!PlatformPaymentMethods.All.Contains(request.PaymentMethod))
            throw new ArgumentException("Unknown payment method.");

        var contract = await _db.Contracts.Include(c => c.Payments).FirstOrDefaultAsync(c => c.Id == request.ContractId, ct)
            ?? throw new ArgumentException("Contract was not found.");

        var payment = new PlatformCustomerPayment
        {
            CustomerId = contract.CustomerId,
            ContractId = contract.Id,
            Amount = Money(request.Amount),
            PaymentDate = request.PaymentDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            PaymentMethod = request.PaymentMethod.Trim(),
            Reference = request.Reference?.Trim(),
            Notes = request.Notes?.Trim(),
            RecordedByPlatformAdminUserId = actorId,
        };
        _db.CustomerPayments.Add(payment);
        contract.Payments.Add(payment);
        Recalc(contract);
        contract.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(actorId, "platform.payment.recorded", after: new { payment.Id, payment.Amount, contract.ContractNumber, contract.PaymentStatus });
        return ToPaymentDto(payment);
    }

    public async Task<List<PlatformSupportTicketDto>> ListTicketsAsync(Guid? customerId, string? status, CancellationToken ct = default)
    {
        var q = _db.SupportTickets.AsNoTracking().Include(t => t.Customer).AsQueryable();
        if (customerId is Guid id) q = q.Where(t => t.CustomerId == id);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(t => t.Status == status);
        var rows = await q.OrderByDescending(t => t.CreatedAtUtc).ToListAsync(ct);
        return rows.Select(ToTicketDto).ToList();
    }

    public async Task<PlatformSupportTicketDto?> GetTicketAsync(Guid id, CancellationToken ct = default)
    {
        var tkt = await _db.SupportTickets.AsNoTracking().Include(t => t.Customer).FirstOrDefaultAsync(t => t.Id == id, ct);
        return tkt == null ? null : ToTicketDto(tkt);
    }

    public async Task<PlatformSupportTicketDto> CreateTicketAsync(CreateSupportTicketRequest request, Guid actorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Description))
            throw new ArgumentException("Subject and description are required.");
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
            ?? throw new ArgumentException("Customer was not found.");
        if (request.ContractId is Guid contractId)
        {
            var belongs = await _db.Contracts.AsNoTracking()
                .AnyAsync(c => c.Id == contractId && c.CustomerId == customer.Id, ct);
            if (!belongs)
                throw new ArgumentException("Contract does not belong to this customer.");
        }
        if (request.LocalLicenseId is Guid licenseId)
        {
            var license = await _db.LocalLicenses.AsNoTracking().FirstOrDefaultAsync(l => l.Id == licenseId, ct)
                ?? throw new ArgumentException("License was not found.");
            if (license.CustomerId != customer.Id)
                throw new ArgumentException("License does not belong to this customer.");
        }
        if (request.LocalInstallationId is Guid installationId)
        {
            var installation = await _db.LocalInstallations.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == installationId, ct)
                ?? throw new ArgumentException("Installation was not found.");
            if (request.LocalLicenseId is Guid expectedLicense && installation.LicenseId != expectedLicense)
                throw new ArgumentException("Installation does not belong to the selected license.");
            var installLicense = await _db.LocalLicenses.AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == installation.LicenseId, ct)
                ?? throw new ArgumentException("License was not found.");
            if (installLicense.CustomerId != customer.Id)
                throw new ArgumentException("Installation does not belong to this customer.");
        }
        var priority = string.IsNullOrWhiteSpace(request.Priority) ? PlatformSupportTicketPriorities.Normal : request.Priority.Trim();
        if (!PlatformSupportTicketPriorities.All.Contains(priority))
            throw new ArgumentException("Unknown priority.");

        var ticket = new PlatformSupportTicket
        {
            TicketNumber = await NextNumberAsync("SUP", "HY-SUP", ct),
            CustomerId = customer.Id,
            ContractId = request.ContractId,
            LocalLicenseId = request.LocalLicenseId,
            LocalInstallationId = request.LocalInstallationId,
            Subject = request.Subject.Trim(),
            Description = request.Description.Trim(),
            Priority = priority,
            Status = PlatformSupportTicketStatuses.Open,
            CreatedByPlatformAdminUserId = actorId,
        };
        _db.SupportTickets.Add(ticket);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(actorId, "platform.support_ticket.created", after: new { ticket.Id, ticket.TicketNumber, ticket.Subject });
        ticket.Customer = customer;
        return ToTicketDto(ticket);
    }

    public async Task<PlatformSupportTicketDto?> UpdateTicketAsync(Guid id, UpdateSupportTicketRequest request, Guid actorId, CancellationToken ct = default)
    {
        var ticket = await _db.SupportTickets.Include(t => t.Customer).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket == null) return null;
        if (string.Equals(ticket.Status, PlatformSupportTicketStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            if (RequestsClosedTicketChange(ticket, request))
                throw new ArgumentException("This ticket is closed and cannot be changed.");
            return ToTicketDto(ticket);
        }
        var before = new { ticket.Status, ticket.AssignedToPlatformAdminUserId };
        if (request.AssignedToPlatformAdminUserId.HasValue)
        {
            var assigneeId = request.AssignedToPlatformAdminUserId.Value;
            var assignee = await _db.PlatformAdminUsers.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == assigneeId, ct);
            if (assignee == null)
                throw new ArgumentException("Assignee was not found.");
            if (!assignee.IsActive)
                throw new ArgumentException("Assignee is inactive.");
        }
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim();
            if (!PlatformSupportTicketStatuses.All.Contains(status))
                throw new ArgumentException("Unknown ticket status.");
            if (!string.Equals(status, ticket.Status, StringComparison.OrdinalIgnoreCase))
            {
                if (!PlatformSupportTicketStatuses.CanTransition(ticket.Status, status))
                    throw new ArgumentException("This ticket status change is not allowed.");
                ticket.Status = status;
                if (status is PlatformSupportTicketStatuses.Resolved or PlatformSupportTicketStatuses.Closed)
                    ticket.ResolvedAtUtc ??= DateTime.UtcNow;
            }
        }
        if (!string.IsNullOrWhiteSpace(request.Priority))
        {
            if (!PlatformSupportTicketPriorities.All.Contains(request.Priority.Trim()))
                throw new ArgumentException("Unknown priority.");
            ticket.Priority = request.Priority.Trim();
        }
        if (request.AssignedToPlatformAdminUserId.HasValue)
            ticket.AssignedToPlatformAdminUserId = request.AssignedToPlatformAdminUserId;
        if (request.Resolution != null)
            ticket.Resolution = request.Resolution.Trim();
        ticket.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(actorId, "platform.support_ticket.updated", before: before, after: new { ticket.Status, ticket.AssignedToPlatformAdminUserId, ticket.TicketNumber });
        return ToTicketDto(ticket);
    }

    async Task<PlatformContractItem> BuildItemAsync(ContractItemInput input, int sort, CancellationToken ct)
    {
        if (input.Quantity <= 0) throw new ArgumentException("Quantity must be positive.");
        PlatformCatalogProduct? product = null;
        if (input.CatalogProductId is Guid pid)
            product = await _db.CatalogProducts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid, ct)
                ?? throw new ArgumentException("Catalog product was not found.");

        var name = product?.Name ?? input.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Item name or catalog product is required.");

        var unit = Money(input.UnitPrice ?? product?.DefaultPrice ?? 0);
        var discount = Money(input.DiscountAmount);
        var qty = Money(input.Quantity);
        var line = Money((qty * unit) - discount);
        if (line < 0) line = 0;

        return new PlatformContractItem
        {
            CatalogProductId = product?.Id,
            SkuSnapshot = product?.Sku ?? "CUSTOM",
            NameSnapshot = name,
            ProductTypeSnapshot = product?.ProductType ?? PlatformCatalogProductTypes.Service,
            DescriptionSnapshot = product?.Description,
            Quantity = qty,
            UnitPrice = unit,
            DiscountAmount = discount,
            LineTotal = line,
            SortOrder = sort,
        };
    }

    static void Recalc(PlatformContract contract)
    {
        contract.Subtotal = Money(contract.Items.Sum(i => i.Quantity * i.UnitPrice));
        var afterLines = Money(contract.Items.Sum(i => i.LineTotal));
        contract.Total = Money(afterLines - contract.Discount);
        if (contract.Total < 0) contract.Total = 0;
        var paid = Money(contract.Payments.Sum(p => p.Amount));
        contract.PaymentStatus = PlatformContractPaymentStatuses.FromAmounts(contract.Total, paid);
    }

    async Task<string> NextNumberAsync(string kind, string prefix, CancellationToken ct)
    {
        var year = DateTime.UtcNow.Year;
        var row = await _db.NumberSequences.FirstOrDefaultAsync(s => s.Kind == kind && s.Year == year, ct);
        if (row == null)
        {
            row = new PlatformNumberSequence { Kind = kind, Year = year, LastNumber = 0 };
            _db.NumberSequences.Add(row);
        }
        row.LastNumber += 1;
        return $"{prefix}-{year}-{row.LastNumber:0000}";
    }

    static void ApplyCustomer(PlatformCustomer c, UpsertPlatformCustomerRequest r, bool isCreate, Guid actorId)
    {
        if (string.IsNullOrWhiteSpace(r.BusinessName) || string.IsNullOrWhiteSpace(r.OwnerName))
            throw new ArgumentException("BusinessName and OwnerName are required.");
        c.BusinessName = r.BusinessName.Trim();
        c.OwnerName = r.OwnerName.Trim();
        c.Phone = r.Phone?.Trim();
        c.WhatsApp = r.WhatsApp?.Trim();
        c.Email = r.Email?.Trim();
        c.Address = r.Address?.Trim();
        var method = string.IsNullOrWhiteSpace(r.PreferredContactMethod) ? PlatformContactMethods.WhatsApp : r.PreferredContactMethod.Trim();
        if (!PlatformContactMethods.All.Contains(method))
            throw new ArgumentException("Unknown preferred contact method.");
        c.PreferredContactMethod = method;
        c.Notes = r.Notes?.Trim();
        if (!string.IsNullOrWhiteSpace(r.Status))
        {
            if (!PlatformCustomerStatuses.All.Contains(r.Status.Trim()))
                throw new ArgumentException("Unknown customer status.");
            c.Status = r.Status.Trim();
        }
        else if (isCreate)
            c.Status = PlatformCustomerStatuses.Prospect;
        c.LeadSource = r.LeadSource?.Trim();
        c.AssignedSalesRepPlatformAdminUserId = r.AssignedSalesRepPlatformAdminUserId;
        // TenantId is Ops/Admin-only via SetCustomerCloudLinkAsync — never from Sales upsert.
        c.OwnerUsername = r.OwnerUsername?.Trim();
        c.OwnerEmail = r.OwnerEmail?.Trim();
        if (isCreate)
        {
            c.CreatedByPlatformAdminUserId = actorId;
            c.OwnerAccountCreatedAtUtc = DateTime.UtcNow;
            c.OwnerAccountStatus = PlatformOwnerAccountStatuses.Unknown;
        }
    }

    static void ApplyProduct(PlatformCatalogProduct p, UpsertCatalogProductRequest r)
    {
        p.Sku = r.Sku.Trim();
        p.Name = r.Name.Trim();
        p.Description = r.Description?.Trim();
        p.ProductType = r.ProductType.Trim();
        p.DefaultPrice = Money(r.DefaultPrice);
        p.IsActive = r.IsActive;
    }

    static void ValidateProduct(UpsertCatalogProductRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Sku) || string.IsNullOrWhiteSpace(r.Name))
            throw new ArgumentException("Sku and Name are required.");
        if (!PlatformCatalogProductTypes.All.Contains(r.ProductType.Trim()))
            throw new ArgumentException("Unknown product type.");
        if (r.DefaultPrice < 0) throw new ArgumentException("DefaultPrice cannot be negative.");
    }

    static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    static LocalLicenseListItemDto? PickPreferredLicense(List<LocalLicenseListItemDto> licenses)
    {
        if (licenses.Count == 0) return null;
        return licenses
            .OrderByDescending(l => l.Status switch
            {
                LocalLicenseStatuses.Active => 4,
                LocalLicenseStatuses.PendingActivation => 3,
                LocalLicenseStatuses.Created => 2,
                LocalLicenseStatuses.Suspended => 1,
                _ => 0,
            })
            .ThenByDescending(l => l.CreatedAtUtc)
            .First();
    }

    static PlatformCustomerListItemDto ToListItem(PlatformCustomer c, int openTickets) => new()
    {
        Id = c.Id,
        BusinessName = c.BusinessName,
        OwnerName = c.OwnerName,
        Phone = c.Phone,
        Email = c.Email,
        Status = c.Status,
        LeadSource = c.LeadSource,
        AssignedSalesRepPlatformAdminUserId = c.AssignedSalesRepPlatformAdminUserId,
        TenantId = c.TenantId,
        OpenTicketCount = openTickets,
        CreatedAtUtc = c.CreatedAtUtc,
    };

    static PlatformCustomerDetailDto ToDetail(PlatformCustomer c) => new()
    {
        Id = c.Id,
        BusinessName = c.BusinessName,
        OwnerName = c.OwnerName,
        Phone = c.Phone,
        Email = c.Email,
        WhatsApp = c.WhatsApp,
        Address = c.Address,
        PreferredContactMethod = c.PreferredContactMethod,
        Notes = c.Notes,
        Status = c.Status,
        LeadSource = c.LeadSource,
        AssignedSalesRepPlatformAdminUserId = c.AssignedSalesRepPlatformAdminUserId,
        CreatedByPlatformAdminUserId = c.CreatedByPlatformAdminUserId,
        TenantId = c.TenantId,
        OwnerUsername = c.OwnerUsername,
        OwnerEmail = c.OwnerEmail,
        OwnerAccountStatus = c.OwnerAccountStatus,
        OwnerAccountCreatedAtUtc = c.OwnerAccountCreatedAtUtc,
        OwnerLastLoginAtUtc = c.OwnerLastLoginAtUtc,
        PasswordResetInitiatedAtUtc = c.PasswordResetInitiatedAtUtc,
        OpenTicketCount = 0,
        CreatedAtUtc = c.CreatedAtUtc,
        UpdatedAtUtc = c.UpdatedAtUtc,
    };

    static PlatformCatalogProductDto ToProductDto(PlatformCatalogProduct p) => new()
    {
        Id = p.Id,
        Sku = p.Sku,
        Name = p.Name,
        Description = p.Description,
        ProductType = p.ProductType,
        DefaultPrice = p.DefaultPrice,
        Currency = p.Currency,
        IsActive = p.IsActive,
    };

    static PlatformContractItemDto ToItemDto(PlatformContractItem i) => new()
    {
        Id = i.Id,
        CatalogProductId = i.CatalogProductId,
        SkuSnapshot = i.SkuSnapshot,
        NameSnapshot = i.NameSnapshot,
        ProductTypeSnapshot = i.ProductTypeSnapshot,
        DescriptionSnapshot = i.DescriptionSnapshot,
        Quantity = i.Quantity,
        UnitPrice = i.UnitPrice,
        DiscountAmount = i.DiscountAmount,
        LineTotal = i.LineTotal,
    };

    static PlatformContractDto ToContractDto(PlatformContract c, string? customerName)
    {
        var paid = Money(c.Payments.Sum(p => p.Amount));
        return new PlatformContractDto
        {
            Id = c.Id,
            CustomerId = c.CustomerId,
            CustomerName = customerName,
            ContractNumber = c.ContractNumber,
            ContractDate = c.ContractDate,
            StartDate = c.StartDate,
            EndDate = c.EndDate,
            Status = c.Status,
            Currency = c.Currency,
            Subtotal = c.Subtotal,
            Discount = c.Discount,
            Total = c.Total,
            PaidAmount = paid,
            OutstandingAmount = Math.Max(0, c.Total - paid),
            PaymentStatus = c.PaymentStatus,
            Notes = c.Notes,
            CreatedAtUtc = c.CreatedAtUtc,
            Items = c.Items.OrderBy(i => i.SortOrder).Select(ToItemDto).ToList(),
        };
    }

    static PlatformCustomerPaymentDto ToPaymentDto(PlatformCustomerPayment p) => new()
    {
        Id = p.Id,
        CustomerId = p.CustomerId,
        ContractId = p.ContractId,
        ContractNumber = p.Contract?.ContractNumber,
        Amount = p.Amount,
        Currency = p.Currency,
        PaymentDate = p.PaymentDate,
        PaymentMethod = p.PaymentMethod,
        Reference = p.Reference,
        Notes = p.Notes,
        CreatedAtUtc = p.CreatedAtUtc,
    };

    static PlatformSupportTicketDto ToTicketDto(PlatformSupportTicket t) => new()
    {
        Id = t.Id,
        TicketNumber = t.TicketNumber,
        CustomerId = t.CustomerId,
        CustomerName = t.Customer?.BusinessName,
        ContractId = t.ContractId,
        LocalLicenseId = t.LocalLicenseId,
        LocalInstallationId = t.LocalInstallationId,
        Subject = t.Subject,
        Description = t.Description,
        Priority = t.Priority,
        Status = t.Status,
        AssignedToPlatformAdminUserId = t.AssignedToPlatformAdminUserId,
        CreatedAtUtc = t.CreatedAtUtc,
        UpdatedAtUtc = t.UpdatedAtUtc,
        ResolvedAtUtc = t.ResolvedAtUtc,
        Resolution = t.Resolution,
    };

    static bool RequestsClosedTicketChange(PlatformSupportTicket ticket, UpdateSupportTicketRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Status)
            && !string.Equals(request.Status.Trim(), ticket.Status, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(request.Priority)
            && !string.Equals(request.Priority.Trim(), ticket.Priority, StringComparison.OrdinalIgnoreCase))
            return true;
        if (request.AssignedToPlatformAdminUserId.HasValue
            && request.AssignedToPlatformAdminUserId != ticket.AssignedToPlatformAdminUserId)
            return true;
        if (request.Resolution != null
            && !string.Equals(request.Resolution.Trim(), ticket.Resolution ?? string.Empty, StringComparison.Ordinal))
            return true;
        return false;
    }
}
