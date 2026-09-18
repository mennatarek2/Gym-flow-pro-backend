namespace GMS.Platform.Services;

using Microsoft.EntityFrameworkCore;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

public class DeskFeedbackService : IDeskFeedbackService
{
    public const int MessageMinLength = 10;
    public const int MessageMaxLength = 4000;
    public const int SubjectMaxLength = 200;
    public const int DuplicateWindowMinutes = 5;

    private readonly PlatformDbContext _db;
    private readonly IPlatformAuditService _audit;

    public DeskFeedbackService(PlatformDbContext db, IPlatformAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<DeskFeedbackDto> SubmitAsync(SubmitDeskFeedbackRequest request, DeskFeedbackActorContext actor, CancellationToken ct = default)
    {
        if (actor.TenantId == Guid.Empty)
            throw new ArgumentException("Tenant context is required.");
        if (actor.SenderUserId == Guid.Empty)
            throw new ArgumentException("Sender is required.");

        var category = (request.Category ?? string.Empty).Trim().ToLowerInvariant();
        if (!DeskFeedbackCategories.All.Contains(category))
            throw new ArgumentException("Unknown feedback category.");

        var message = (request.Message ?? string.Empty).Trim();
        if (message.Length < MessageMinLength)
            throw new ArgumentException($"Message must be at least {MessageMinLength} characters.");
        if (message.Length > MessageMaxLength)
            throw new ArgumentException($"Message must be at most {MessageMaxLength} characters.");

        var subject = string.IsNullOrWhiteSpace(request.Subject) ? null : request.Subject.Trim();
        if (subject != null && subject.Length > SubjectMaxLength)
            throw new ArgumentException($"Subject must be at most {SubjectMaxLength} characters.");

        var clientRequestId = string.IsNullOrWhiteSpace(request.ClientRequestId) ? null : request.ClientRequestId.Trim();
        if (clientRequestId != null && clientRequestId.Length > 64)
            throw new ArgumentException("ClientRequestId is too long.");

        if (clientRequestId != null)
        {
            var existingByKey = await _db.DeskFeedback
                .AsNoTracking()
                .Include(f => f.Customer)
                .FirstOrDefaultAsync(f => f.TenantId == actor.TenantId && f.ClientRequestId == clientRequestId, ct);
            if (existingByKey != null)
                return ToDto(existingByKey, alreadySubmitted: true);
        }

        var since = DateTime.UtcNow.AddMinutes(-DuplicateWindowMinutes);
        var recentDuplicate = await _db.DeskFeedback
            .AsNoTracking()
            .Include(f => f.Customer)
            .Where(f => f.TenantId == actor.TenantId
                        && f.SenderUserId == actor.SenderUserId
                        && f.Category == category
                        && f.Message == message
                        && f.CreatedAtUtc >= since)
            .OrderByDescending(f => f.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (recentDuplicate != null)
            return ToDto(recentDuplicate, alreadySubmitted: true);

        var customerId = await ResolveCustomerIdAsync(actor, ct);
        var row = new DeskFeedback
        {
            CustomerId = customerId,
            TenantId = actor.TenantId,
            GymCode = Truncate(actor.GymCode, 64),
            GymName = Truncate(actor.GymName, 200),
            SenderUserId = actor.SenderUserId,
            SenderRole = Truncate(actor.SenderRole, 40) ?? "staff",
            SenderEmail = Truncate(actor.SenderEmail, 200),
            SenderDisplayName = Truncate(actor.SenderDisplayName, 200),
            Category = category,
            Subject = subject,
            Message = message,
            Status = DeskFeedbackStatuses.New,
            AppVersion = Truncate(request.AppVersion, 80),
            PageUrl = Truncate(request.PageUrl, 500),
            ClientRequestId = clientRequestId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        _db.DeskFeedback.Add(row);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(
            actor.SenderUserId,
            "platform.desk_feedback.submitted",
            tenantId: actor.TenantId,
            after: new { row.Id, row.Category, row.CustomerId });

        if (customerId is Guid cid)
            row.Customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, ct);

        return ToDto(row, alreadySubmitted: false);
    }

    public async Task<DeskFeedbackDto> SubmitFromLocalLicenseAsync(IngestLocalDeskFeedbackRequest request, CancellationToken ct = default)
    {
        var key = request.LicenseKey?.Trim() ?? "";
        var installationId = request.InstallationId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(installationId))
            throw new ArgumentException("licenseKey and installationId are required.");
        if (request.SenderUserId == Guid.Empty)
            throw new ArgumentException("SenderUserId is required.");

        var license = await _db.LocalLicenses
            .Include(l => l.Installations)
            .FirstOrDefaultAsync(l => l.LicenseKey == key, ct)
            ?? throw new ArgumentException("License was not found.");

        var bound = license.Installations.FirstOrDefault(i =>
            string.Equals(i.InstallationId, installationId, StringComparison.OrdinalIgnoreCase));
        var otherActive = license.Installations.FirstOrDefault(i =>
            i.Status == LocalInstallationStatuses.Active
            && !string.Equals(i.InstallationId, installationId, StringComparison.OrdinalIgnoreCase));
        if (bound == null && otherActive != null)
            throw new ArgumentException("Installation does not match this license.");

        // Never trust body TenantId — spoofing can mis-attribute feedback or hijack customer links.
        // Cloud-linked customers use their TenantId; otherwise partition by license Id.
        Guid? cloudTenantId = null;
        if (license.CustomerId is Guid linkedCustomerId)
        {
            cloudTenantId = await _db.Customers.AsNoTracking()
                .Where(c => c.Id == linkedCustomerId)
                .Select(c => c.TenantId)
                .FirstOrDefaultAsync(ct);
        }
        var boundTenantId = cloudTenantId ?? license.Id;

        var gymCode = string.IsNullOrWhiteSpace(request.GymCode) ? bound?.GymCode : request.GymCode.Trim();
        var gymName = string.IsNullOrWhiteSpace(request.GymName) ? bound?.GymName : request.GymName.Trim();
        var appVersion = string.IsNullOrWhiteSpace(request.AppVersion) ? bound?.AppVersion : request.AppVersion.Trim();

        var actor = new DeskFeedbackActorContext
        {
            TenantId = boundTenantId,
            GymCode = gymCode,
            GymName = gymName,
            SenderUserId = request.SenderUserId,
            SenderRole = string.IsNullOrWhiteSpace(request.SenderRole) ? "staff" : request.SenderRole.Trim(),
            SenderEmail = request.SenderEmail,
            SenderDisplayName = request.SenderDisplayName,
            PreferredCustomerId = license.CustomerId,
        };

        return await SubmitAsync(
            new SubmitDeskFeedbackRequest
            {
                Category = request.Category,
                Subject = request.Subject,
                Message = request.Message,
                AppVersion = appVersion,
                PageUrl = request.PageUrl,
                ClientRequestId = request.ClientRequestId,
            },
            actor,
            ct);
    }

    public async Task<List<DeskFeedbackDto>> ListAsync(
        Guid? customerId,
        Guid? tenantId,
        string? category,
        string? status,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        var q = _db.DeskFeedback.AsNoTracking().Include(f => f.Customer).AsQueryable();
        if (customerId is Guid cid) q = q.Where(f => f.CustomerId == cid);
        if (tenantId is Guid tid) q = q.Where(f => f.TenantId == tid);
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(f => f.Category == category.Trim());
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(f => f.Status == status.Trim());
        if (from is DateOnly fromDay)
        {
            var fromUtc = fromDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(f => f.CreatedAtUtc >= fromUtc);
        }
        if (to is DateOnly toDay)
        {
            var toUtc = toDay.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc);
            q = q.Where(f => f.CreatedAtUtc <= toUtc);
        }

        var rows = await q.OrderByDescending(f => f.CreatedAtUtc).Take(500).ToListAsync(ct);
        return rows.Select(r => ToDto(r)).ToList();
    }

    public async Task<DeskFeedbackDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.DeskFeedback.AsNoTracking().Include(f => f.Customer).FirstOrDefaultAsync(f => f.Id == id, ct);
        return row == null ? null : ToDto(row);
    }

    public async Task<DeskFeedbackDto?> UpdateAsync(Guid id, UpdateDeskFeedbackRequest request, Guid actorPlatformUserId, CancellationToken ct = default)
    {
        var row = await _db.DeskFeedback.Include(f => f.Customer).FirstOrDefaultAsync(f => f.Id == id, ct);
        if (row == null) return null;

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim().ToLowerInvariant();
            if (!DeskFeedbackStatuses.All.Contains(status))
                throw new ArgumentException("Unknown feedback status.");
            row.Status = status;
        }

        if (request.InternalNote != null)
        {
            var note = request.InternalNote.Trim();
            if (note.Length > 2000)
                throw new ArgumentException("Internal note must be at most 2000 characters.");
            row.InternalNote = string.IsNullOrEmpty(note) ? null : note;
        }

        if (request.ResponseToCustomer != null)
        {
            var response = request.ResponseToCustomer.Trim();
            if (response.Length > 2000)
                throw new ArgumentException("Response must be at most 2000 characters.");
            row.ResponseToCustomer = string.IsNullOrEmpty(response) ? null : response;
        }

        row.ReviewedByPlatformAdminUserId = actorPlatformUserId;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(
            actorPlatformUserId,
            "platform.desk_feedback.updated",
            tenantId: row.TenantId,
            after: new { row.Id, row.Status });

        return ToDto(row);
    }

    async Task<Guid?> ResolveCustomerIdAsync(DeskFeedbackActorContext actor, CancellationToken ct)
    {
        if (actor.PreferredCustomerId is Guid preferred && preferred != Guid.Empty)
        {
            var exists = await _db.Customers.AsNoTracking().AnyAsync(c => c.Id == preferred, ct);
            if (exists) return preferred;
        }

        var byTenant = await _db.Customers.AsNoTracking()
            .Where(c => c.TenantId == actor.TenantId)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
        if (byTenant != null) return byTenant;

        if (!string.IsNullOrWhiteSpace(actor.GymCode))
        {
            var code = actor.GymCode.Trim();
            var byInstall = await (
                from install in _db.LocalInstallations.AsNoTracking()
                join license in _db.LocalLicenses.AsNoTracking() on install.LicenseId equals license.Id
                where install.GymCode == code && license.CustomerId != null
                orderby install.LastValidatedAtUtc descending
                select license.CustomerId
            ).FirstOrDefaultAsync(ct);
            if (byInstall != null) return byInstall;
        }

        return null;
    }

    static DeskFeedbackDto ToDto(DeskFeedback row, bool alreadySubmitted = false) => new()
    {
        Id = row.Id,
        CustomerId = row.CustomerId,
        CustomerName = row.Customer?.BusinessName,
        TenantId = row.TenantId,
        GymCode = row.GymCode,
        GymName = row.GymName,
        SenderUserId = row.SenderUserId,
        SenderRole = row.SenderRole,
        SenderEmail = row.SenderEmail,
        SenderDisplayName = row.SenderDisplayName,
        Category = row.Category,
        Subject = row.Subject,
        Message = row.Message,
        Status = row.Status,
        AppVersion = row.AppVersion,
        PageUrl = row.PageUrl,
        InternalNote = row.InternalNote,
        ResponseToCustomer = row.ResponseToCustomer,
        ReviewedByPlatformAdminUserId = row.ReviewedByPlatformAdminUserId,
        CreatedAtUtc = row.CreatedAtUtc,
        UpdatedAtUtc = row.UpdatedAtUtc,
        AlreadySubmitted = alreadySubmitted,
    };

    static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
