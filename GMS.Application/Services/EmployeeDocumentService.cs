namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Employee documents reuse the same IFileStorageService as employee photos, but — unlike photos —
/// never expose FileUrl to the frontend: these are private HR records (National ID, contracts), so
/// the only way to read the bytes is DownloadAsync, which re-checks tenant/employee ownership on
/// every call and streams via IFileStorageService.TryReadAsync rather than handing back a public URL.
/// </summary>
public class EmployeeDocumentService : IEmployeeDocumentService
{
    private readonly GymFlowProDbContext _db;
    private readonly IFileStorageService _files;
    private readonly IAuditService _audit;
    private readonly ILogger<EmployeeDocumentService> _logger;

    public EmployeeDocumentService(GymFlowProDbContext db, IFileStorageService files, IAuditService audit, ILogger<EmployeeDocumentService> logger)
    {
        _db = db;
        _files = files;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<List<EmployeeDocumentDto>>> ListAsync(Guid tenantId, Guid employeeId)
    {
        var employeeExists = await _db.Employees.AnyAsync(e => e.Id == employeeId && e.TenantId == tenantId);
        if (!employeeExists)
            return Result<List<EmployeeDocumentDto>>.Failure("Employee not found / الموظف غير موجود");

        var rows = await _db.EmployeeDocuments.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.EmployeeId == employeeId)
            .OrderByDescending(d => d.CreatedAtUtc)
            .ToListAsync();

        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId);
        var today = MembershipOperational.TodayCairo();
        return Result<List<EmployeeDocumentDto>>.Success(rows.Select(d => Map(d, employee, today)).ToList());
    }

    public async Task<Result<List<EmployeeDocumentDto>>> ListAllAsync(Guid tenantId, string? expiryStatus = null)
    {
        var rows = await _db.EmployeeDocuments.AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .OrderBy(d => d.ExpiryDate)
            .ToListAsync();

        var employees = await _db.Employees.AsNoTracking().Where(e => e.TenantId == tenantId).ToDictionaryAsync(e => e.Id);
        var today = MembershipOperational.TodayCairo();
        var mapped = rows.Select(d => Map(d, employees.GetValueOrDefault(d.EmployeeId), today));

        if (!string.IsNullOrWhiteSpace(expiryStatus))
            mapped = mapped.Where(d => string.Equals(d.ExpiryStatus, expiryStatus, StringComparison.OrdinalIgnoreCase));

        return Result<List<EmployeeDocumentDto>>.Success(mapped.ToList());
    }

    public async Task<Result<EmployeeDocumentDto>> UploadAsync(
        Guid tenantId, Guid employeeId, Stream file, string fileName, string contentType,
        CreateEmployeeDocumentRequest request, Guid? actorAppUserId)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId && e.TenantId == tenantId);
        if (employee == null)
            return Result<EmployeeDocumentDto>.Failure("Employee not found / الموظف غير موجود");

        var documentType = request.DocumentType?.Trim() ?? string.Empty;
        if (!EmployeeDocumentTypes.All.Contains(documentType))
            return Result<EmployeeDocumentDto>.Failure("Invalid document type / نوع المستند غير صالح");

        if (request.ExpiryDate.HasValue && request.IssueDate.HasValue && request.ExpiryDate < request.IssueDate)
            return Result<EmployeeDocumentDto>.Failure("Expiry date cannot be before issue date / تاريخ الانتهاء لا يمكن أن يسبق تاريخ الإصدار");

        var folder = $"employee-documents-{tenantId:N}";
        var relativeUrl = await _files.UploadAsync(file, fileName, folder);

        var entity = new EmployeeDocument
        {
            TenantId = tenantId,
            EmployeeId = employeeId,
            DocumentType = documentType,
            FileUrl = relativeUrl,
            FileName = fileName,
            ContentType = contentType,
            IssueDate = request.IssueDate,
            ExpiryDate = request.ExpiryDate,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            UploadedByAppUserId = actorAppUserId,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.EmployeeDocuments.Add(entity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("employee_document.upload", "EmployeeDocument", entity.Id, null,
            new { entity.EmployeeId, entity.DocumentType, entity.FileName, entity.ExpiryDate });
        _logger.LogInformation("Document {Type} uploaded for employee {EmployeeId}", documentType, employeeId);

        var today = MembershipOperational.TodayCairo();
        return Result<EmployeeDocumentDto>.Success(Map(entity, employee, today));
    }

    public async Task<Result<bool>> DeleteAsync(Guid tenantId, Guid documentId, Guid? actorAppUserId)
    {
        var entity = await _db.EmployeeDocuments.FirstOrDefaultAsync(d => d.Id == documentId && d.TenantId == tenantId);
        if (entity == null)
            return Result<bool>.Failure("Document not found / المستند غير موجود");

        entity.IsDeleted = true;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("employee_document.delete", "EmployeeDocument", entity.Id, null,
            new { entity.EmployeeId, entity.DocumentType, entity.FileName, actorAppUserId });

        // Best-effort: leave the physical file if deletion fails — the DB row is already gone from
        // every query, so an orphaned blob is a storage-cleanup concern, never a data-integrity one.
        try { await _files.DeleteAsync(entity.FileUrl); } catch (Exception ex) { _logger.LogWarning(ex, "Could not delete document blob {Url}", entity.FileUrl); }

        return Result<bool>.Success(true);
    }

    public async Task<Result<(byte[] Bytes, string ContentType, string FileName)>> DownloadAsync(Guid tenantId, Guid documentId, Guid? restrictToEmployeeId = null)
    {
        var entity = await _db.EmployeeDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId && d.TenantId == tenantId);
        if (entity == null)
            return Result<(byte[], string, string)>.Failure("Document not found / المستند غير موجود");
        if (restrictToEmployeeId.HasValue && entity.EmployeeId != restrictToEmployeeId.Value)
            return Result<(byte[], string, string)>.Failure("Document not found / المستند غير موجود");

        var bytes = await _files.TryReadAsync(entity.FileUrl);
        if (bytes == null)
            return Result<(byte[], string, string)>.Failure("File is unavailable / الملف غير متاح");

        return Result<(byte[], string, string)>.Success((bytes, entity.ContentType, entity.FileName));
    }

    private static EmployeeDocumentDto Map(EmployeeDocument d, Employee? employee, DateOnly today) => new()
    {
        Id = d.Id,
        EmployeeId = d.EmployeeId,
        EmployeeName = employee != null ? $"{employee.FirstName} {employee.LastName}" : string.Empty,
        DocumentType = d.DocumentType,
        FileName = d.FileName,
        IssueDate = d.IssueDate,
        ExpiryDate = d.ExpiryDate,
        ExpiryStatus = ComputeExpiryStatus(d.ExpiryDate, today),
        Notes = d.Notes,
        CreatedAtUtc = d.CreatedAtUtc
    };

    private static string ComputeExpiryStatus(DateOnly? expiryDate, DateOnly today)
    {
        if (expiryDate == null)
            return DocumentExpiryStatuses.Valid;
        if (expiryDate < today)
            return DocumentExpiryStatuses.Expired;
        if (expiryDate <= today.AddDays(30))
            return DocumentExpiryStatuses.ExpiringSoon;
        return DocumentExpiryStatuses.Valid;
    }
}
