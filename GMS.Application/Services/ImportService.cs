namespace GMS.Application.Services;

using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Imports;
using GMS.Application.DTOs.Members;
using GMS.Application.Interfaces;
using GMS.Application.Jobs;
using GMS.Application.Utilities;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Excel/CSV bulk-import pipeline for members + current memberships. Each row's original cell
/// values are captured once at upload time, keyed by source header (ImportRow.RawJson) — the
/// current column mapping is re-applied on every validate/execute pass, so SetMappingAsync can
/// always correct a bad auto-detection without needing to re-read the uploaded file.
/// </summary>
public class ImportService : IImportService
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;
    private const int MaxRows = 10_000;
    private const int ExecuteChunkSize = 500;
    private const int FuzzyMatchThreshold = 2;
    private const int RollbackWindowDays = 7;

    /// <summary>Known header aliases → canonical target field. Matched exactly (case-insensitive)
    /// first, then by Levenshtein distance &lt;= <see cref="FuzzyMatchThreshold"/>.</summary>
    private static readonly Dictionary<string, string> HeaderAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = "fullName", ["full name"] = "fullName", ["fullname"] = "fullName", ["الاسم"] = "fullName",
        ["phone"] = "phoneNumber", ["mobile"] = "phoneNumber", ["phone number"] = "phoneNumber",
        ["موبايل"] = "phoneNumber", ["تليفون"] = "phoneNumber",
        ["plan"] = "planName", ["plan name"] = "planName", ["الخطة"] = "planName",
        ["start date"] = "startDate", ["تاريخ البداية"] = "startDate",
        ["end date"] = "endDate", ["تاريخ النهاية"] = "endDate",
        ["sessions remaining"] = "sessionsRemaining", ["جلسات متبقية"] = "sessionsRemaining",
        ["date of birth"] = "dateOfBirth", ["dob"] = "dateOfBirth", ["تاريخ الميلاد"] = "dateOfBirth"
    };

    private readonly GymFlowProDbContext _dbContext;
    private readonly IMemberService _memberService;
    private readonly IFileStorageService _fileStorage;
    private readonly IAuditService _auditService;
    private readonly ITenantContext _tenantContext;
    private readonly IFeatureAccessService _featureAccess;
    private readonly ILogger<ImportService> _logger;

    public ImportService(
        GymFlowProDbContext dbContext, IMemberService memberService, IFileStorageService fileStorage,
        IAuditService auditService, ITenantContext tenantContext, IFeatureAccessService featureAccess,
        ILogger<ImportService> logger)
    {
        _dbContext = dbContext;
        _memberService = memberService;
        _fileStorage = fileStorage;
        _auditService = auditService;
        _tenantContext = tenantContext;
        _featureAccess = featureAccess;
        _logger = logger;
    }

    public async Task<Result<ImportBatchDto>> UploadAsync(
        Stream fileStream, string fileName, string contentType, Guid staffUserId, Guid tenantId)
    {
        try
        {
            if (fileStream.Length > MaxFileSizeBytes)
                return Fail(ImportFailureReasons.FileTooLarge, "File exceeds the 5MB limit / الملف يتجاوز الحد الأقصى 5 ميجابايت");

            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (extension != ".xlsx" && extension != ".csv")
                return Fail(ImportFailureReasons.UnsupportedFileType, "Only .xlsx and .csv files are supported / يُدعم فقط ملفات .xlsx و.csv");

            var staffUser = await _dbContext.AppUsers
                .FirstOrDefaultAsync(u => u.UserId == staffUserId.ToString() && u.TenantId == tenantId);
            if (staffUser == null)
                return Fail(ImportFailureReasons.UnsupportedFileType, "Staff user not found / المستخدم غير موجود");

            using var buffer = new MemoryStream();
            await fileStream.CopyToAsync(buffer);
            buffer.Position = 0;

            var (headers, rawRows) = ParseFile(buffer, extension);

            if (rawRows.Count > MaxRows)
                return Fail(ImportFailureReasons.TooManyRows, $"File exceeds the {MaxRows}-row limit / الملف يتجاوز الحد الأقصى للصفوف");

            buffer.Position = 0;
            var blobUrl = await _fileStorage.UploadAsync(buffer, fileName, "imports");

            var mapping = AutoMapHeaders(headers);

            var batch = new ImportBatch
            {
                TenantId = tenantId,
                UploadedByUserId = staffUser.Id,
                FileName = fileName,
                FileBlobUrl = blobUrl,
                EntityScope = "members_memberships",
                Status = "validating",
                TotalRows = rawRows.Count,
                MappingJson = JsonSerializer.Serialize(mapping)
            };
            _dbContext.ImportBatches.Add(batch);

            for (var i = 0; i < rawRows.Count; i++)
            {
                _dbContext.ImportRows.Add(new ImportRow
                {
                    TenantId = tenantId,
                    BatchId = batch.Id,
                    RowNumber = i + 1,
                    RawJson = JsonSerializer.Serialize(rawRows[i]),
                    Status = "ok"
                });
            }

            await _dbContext.SaveChangesAsync();

            BackgroundJobEnqueue(batch.Id, tenantId);

            _logger.LogInformation("Import batch {BatchId} uploaded: {FileName}, {RowCount} rows", batch.Id, fileName, rawRows.Count);

            return Result<ImportBatchDto>.Success(ToDto(batch, mapping));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading import file {FileName}", fileName);
            return Result<ImportBatchDto>.Failure("Failed to upload import file / فشل رفع ملف الاستيراد", ex.Message);
        }
    }

    public async Task<Result<ImportBatchDto>> SetMappingAsync(Guid batchId, ColumnMapRequest mapping, Guid tenantId)
    {
        try
        {
            var batch = await _dbContext.ImportBatches.FirstOrDefaultAsync(b => b.Id == batchId && b.TenantId == tenantId);
            if (batch == null)
                return Fail(ImportFailureReasons.BatchNotFound, "Import batch not found / دفعة الاستيراد غير موجودة");

            if (batch.Status is not ("validating" or "dry_run_ready" or "failed"))
                return Fail(ImportFailureReasons.InvalidStatus,
                    "Mapping can only be changed before the import is executed / يمكن تعديل الربط فقط قبل تنفيذ الاستيراد");

            batch.MappingJson = JsonSerializer.Serialize(mapping.Mapping);
            batch.Status = "validating";
            batch.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            BackgroundJobEnqueue(batch.Id, tenantId);

            return Result<ImportBatchDto>.Success(ToDto(batch, mapping.Mapping));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting mapping for import batch {BatchId}", batchId);
            return Result<ImportBatchDto>.Failure("Failed to update column mapping / فشل تحديث ربط الأعمدة", ex.Message);
        }
    }

    public async Task<ImportBatchDto?> GetAsync(Guid batchId, Guid tenantId)
    {
        var batch = await _dbContext.ImportBatches.FirstOrDefaultAsync(b => b.Id == batchId && b.TenantId == tenantId);
        return batch == null ? null : ToDto(batch, DeserializeMapping(batch.MappingJson));
    }

    public async Task<byte[]?> GetErrorsCsvAsync(Guid batchId, Guid tenantId)
    {
        var batch = await _dbContext.ImportBatches.FirstOrDefaultAsync(b => b.Id == batchId && b.TenantId == tenantId);
        if (batch == null)
            return null;

        var mapping = DeserializeMapping(batch.MappingJson);

        var errorRows = await _dbContext.ImportRows
            .Where(r => r.BatchId == batchId && r.TenantId == tenantId && r.Status == "error")
            .OrderBy(r => r.RowNumber)
            .ToListAsync();

        var plans = await _dbContext.MembershipPlans
            .Where(p => p.TenantId == tenantId && p.IsActive)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("RowNumber,ErrorCodes,FullName,PhoneNumber,PlanName,PlanSuggestions");

        foreach (var row in errorRows)
        {
            var raw = DeserializeRaw(row.RawJson);
            var mapped = ApplyMapping(raw, mapping);

            var suggestions = (row.ErrorCodes ?? string.Empty).Contains(ImportRowErrorCodes.PlanUnmatched)
                ? string.Join(";", RankPlanCandidates(mapped.GetValueOrDefault("planName", string.Empty), plans).Take(3).Select(p => p.Name))
                : string.Empty;

            sb.Append(row.RowNumber).Append(',')
              .Append(CsvEscape(row.ErrorCodes ?? string.Empty)).Append(',')
              .Append(CsvEscape(mapped.GetValueOrDefault("fullName", string.Empty))).Append(',')
              .Append(CsvEscape(mapped.GetValueOrDefault("phoneNumber", string.Empty))).Append(',')
              .Append(CsvEscape(mapped.GetValueOrDefault("planName", string.Empty))).Append(',')
              .Append(CsvEscape(suggestions))
              .AppendLine();
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public async Task<Result<bool>> EnqueueExecuteAsync(Guid batchId, Guid tenantId)
    {
        var batch = await _dbContext.ImportBatches.FirstOrDefaultAsync(b => b.Id == batchId && b.TenantId == tenantId);
        if (batch == null)
            return Result<bool>.Failure($"{ImportFailureReasons.BatchNotFound}|Import batch not found / دفعة الاستيراد غير موجودة");

        if (batch.Status != "dry_run_ready")
            return Result<bool>.Failure(
                $"{ImportFailureReasons.InvalidStatus}|Batch must be dry_run_ready before executing / يجب أن تكون الدفعة جاهزة للتنفيذ التجريبي أولاً");

        batch.Status = "importing";
        batch.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        BackgroundJob.Enqueue<ExecuteImportJob>(job => job.ExecuteAsync(batchId, tenantId));

        return Result<bool>.Success(true);
    }

    public async Task<Result<ImportBatchDto>> RollbackAsync(Guid batchId, Guid managerUserId, Guid tenantId)
    {
        try
        {
            var batch = await _dbContext.ImportBatches.FirstOrDefaultAsync(b => b.Id == batchId && b.TenantId == tenantId);
            if (batch == null)
                return Fail(ImportFailureReasons.BatchNotFound, "Import batch not found / دفعة الاستيراد غير موجودة");

            if (batch.Status != "completed")
                return Fail(ImportFailureReasons.InvalidStatus, "Only a completed import can be rolled back / يمكن التراجع فقط عن استيراد مكتمل");

            if (batch.CompletedAt == null || DateTime.UtcNow - batch.CompletedAt.Value > TimeSpan.FromDays(RollbackWindowDays))
                return Fail(ImportFailureReasons.RollbackWindowExpired,
                    "The 7-day rollback window has expired / انتهت مهلة التراجع البالغة 7 أيام");

            var importedRows = await _dbContext.ImportRows
                .Where(r => r.BatchId == batchId && r.TenantId == tenantId && r.Status == "imported" && r.CreatedMemberId != null)
                .ToListAsync();

            var retainedCount = 0;
            var rolledBackCount = 0;

            foreach (var row in importedRows)
            {
                var memberId = row.CreatedMemberId!.Value;
                var membershipId = row.CreatedMembershipId;

                var hasActivity = await _dbContext.GymAttendances.IgnoreQueryFilters().AnyAsync(a => a.MemberId == memberId)
                    || await _dbContext.Sales.IgnoreQueryFilters().AnyAsync(s => s.MemberId == memberId)
                    || await _dbContext.PaymentTransactions.AnyAsync(p => p.MemberId == memberId);

                if (hasActivity)
                {
                    row.ErrorCodes = AppendCode(row.ErrorCodes, ImportRowErrorCodes.RetainedHasActivity);
                    retainedCount++;
                    continue;
                }

                if (membershipId.HasValue)
                {
                    var membership = await _dbContext.Memberships.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(m => m.Id == membershipId.Value);
                    if (membership != null)
                        _dbContext.Memberships.Remove(membership);
                }

                var member = await _dbContext.GymMembers.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == memberId);
                if (member != null)
                    _dbContext.GymMembers.Remove(member);

                row.ErrorCodes = AppendCode(row.ErrorCodes, ImportRowErrorCodes.RolledBack);
                row.CreatedMemberId = null;
                row.CreatedMembershipId = null;
                rolledBackCount++;
            }

            batch.Status = "rolled_back";
            batch.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            await _auditService.LogAsync("import.rollback", "ImportBatch", batch.Id, null,
                new { rolledBackCount, retainedCount });

            _logger.LogInformation("Import batch {BatchId} rolled back: {RolledBack} deleted, {Retained} retained",
                batch.Id, rolledBackCount, retainedCount);

            return Result<ImportBatchDto>.Success(ToDto(batch, DeserializeMapping(batch.MappingJson)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rolling back import batch {BatchId}", batchId);
            return Result<ImportBatchDto>.Failure("Failed to roll back import / فشل التراجع عن الاستيراد", ex.Message);
        }
    }

    public async Task<Result<ImportBatchDto>> CreateMissingPlansAsync(Guid batchId, List<ImportPlanSpec> plans, Guid tenantId)
    {
        try
        {
            var batch = await _dbContext.ImportBatches.FirstOrDefaultAsync(b => b.Id == batchId && b.TenantId == tenantId);
            if (batch == null)
                return Fail(ImportFailureReasons.BatchNotFound, "Import batch not found / دفعة الاستيراد غير موجودة");

            var existingNames = await _dbContext.MembershipPlans
                .Where(p => p.TenantId == tenantId)
                .Select(p => p.Name.ToLower())
                .ToListAsync();

            foreach (var spec in plans)
            {
                if (existingNames.Contains(spec.Name.ToLowerInvariant()))
                    continue;

                _dbContext.MembershipPlans.Add(new MembershipPlan
                {
                    TenantId = tenantId,
                    Name = spec.Name,
                    NameAr = spec.Name,
                    PlanType = spec.PlanType,
                    DurationDays = spec.DurationDays,
                    Price = spec.Price,
                    IsActive = true
                });
            }

            await _dbContext.SaveChangesAsync();

            batch.Status = "validating";
            batch.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            BackgroundJobEnqueue(batch.Id, tenantId);

            return Result<ImportBatchDto>.Success(ToDto(batch, DeserializeMapping(batch.MappingJson)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating missing plans for import batch {BatchId}", batchId);
            return Result<ImportBatchDto>.Failure("Failed to create plans / فشل إنشاء الخطط", ex.Message);
        }
    }

    public byte[] BuildTemplateXlsx()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Members");

        string[] headers =
        {
            "Name / الاسم", "Phone / موبايل", "Plan / الخطة", "Start Date / تاريخ البداية",
            "End Date / تاريخ النهاية", "Sessions Remaining / جلسات متبقية", "Date of Birth / تاريخ الميلاد"
        };

        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }

        var examples = new[]
        {
            new[] { "Ahmed Ali", "01001234567", "Monthly Unlimited", "2026-01-01", "2026-02-01", "", "1996-05-10" },
            new[] { "Sara Mostafa", "+201112223334", "Session Pack 10", "2026-01-05", "2026-04-05", "10", "1998-11-22" },
            new[] { "Omar Hassan", "0020155566778", "Annual Plan", "2026-01-10", "2027-01-10", "", "1990-03-15" }
        };

        for (var r = 0; r < examples.Length; r++)
            for (var c = 0; c < examples[r].Length; c++)
                ws.Cell(r + 2, c + 1).Value = examples[r][c];

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    // ========================================================================
    // HANGFIRE JOB ENTRY POINTS
    // ========================================================================

    public async Task ValidateAsync(Guid batchId, Guid tenantId)
    {
        var batch = await _dbContext.ImportBatches.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == batchId && b.TenantId == tenantId);

        if (batch == null)
        {
            _logger.LogWarning("ValidateAsync: batch {BatchId} not found", batchId);
            return;
        }

        if (!await IsImportsFeatureEnabledAsync(tenantId))
        {
            _logger.LogInformation("ValidateAsync: imports feature disabled for tenant {TenantId} — no-op", tenantId);
            return;
        }

        try
        {
            var mapping = DeserializeMapping(batch.MappingJson);

            var rows = await _dbContext.ImportRows.IgnoreQueryFilters()
                .Where(r => r.BatchId == batchId && r.TenantId == tenantId)
                .OrderBy(r => r.RowNumber)
                .ToListAsync();

            var plans = await _dbContext.MembershipPlans.IgnoreQueryFilters()
                .Where(p => p.TenantId == tenantId && !p.IsDeleted && p.IsActive)
                .ToListAsync();

            var existingPhones = (await _dbContext.GymMembers.IgnoreQueryFilters()
                    .Where(m => m.TenantId == tenantId && !m.IsDeleted)
                    .Select(m => m.PhoneNumber)
                    .ToListAsync())
                .ToHashSet();

            var phonesSeenInFile = new HashSet<string>();
            var okCount = 0;
            var errorCount = 0;

            foreach (var row in rows)
            {
                var raw = DeserializeRaw(row.RawJson);
                var mapped = ApplyMapping(raw, mapping);

                var errors = new List<string>();

                var normalizedPhone = PhoneNormalizer.Normalize(mapped.GetValueOrDefault("phoneNumber"));
                if (normalizedPhone == null)
                {
                    errors.Add(ImportRowErrorCodes.PhoneInvalid);
                }
                else if (!phonesSeenInFile.Add(normalizedPhone))
                {
                    errors.Add(ImportRowErrorCodes.PhoneDupFile);
                }

                var startDate = ParseDate(mapped.GetValueOrDefault("startDate"));
                var endDate = ParseDate(mapped.GetValueOrDefault("endDate"));
                if (startDate.HasValue && endDate.HasValue && endDate.Value < startDate.Value)
                    errors.Add(ImportRowErrorCodes.DateRangeInvalid);

                var (matchedPlan, _) = MatchPlan(mapped.GetValueOrDefault("planName", string.Empty), plans);
                if (matchedPlan == null)
                    errors.Add(ImportRowErrorCodes.PlanUnmatched);

                if (errors.Count > 0)
                {
                    row.Status = "error";
                    row.ErrorCodes = string.Join(",", errors);
                    errorCount++;
                }
                else if (normalizedPhone != null && existingPhones.Contains(normalizedPhone))
                {
                    row.Status = "skipped";
                    row.ErrorCodes = ImportRowErrorCodes.PhoneExists;
                }
                else
                {
                    row.Status = "ok";
                    row.ErrorCodes = null;
                    okCount++;
                }

                row.UpdatedAtUtc = DateTime.UtcNow;
            }

            batch.TotalRows = rows.Count;
            batch.OkRows = okCount;
            batch.ErrorRows = errorCount;
            batch.Status = "dry_run_ready";
            batch.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("ValidateAsync: batch {BatchId} — {Ok} ok, {Error} error, {Total} total",
                batchId, okCount, errorCount, rows.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ValidateAsync failed for batch {BatchId}", batchId);
            batch.Status = "failed";
            batch.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task ExecuteAsync(Guid batchId, Guid tenantId)
    {
        var batch = await _dbContext.ImportBatches.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == batchId && b.TenantId == tenantId);

        if (batch == null)
        {
            _logger.LogWarning("ExecuteAsync: batch {BatchId} not found", batchId);
            return;
        }

        if (!await IsImportsFeatureEnabledAsync(tenantId))
        {
            _logger.LogInformation("ExecuteAsync: imports feature disabled for tenant {TenantId} — no-op", tenantId);
            return;
        }

        // Hangfire job scope has no ambient tenant — MemberService/MemberRepository query filters
        // would otherwise hide the just-inserted member on reload (NRE in MapToDetailDto).
        var tenant = await _dbContext.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted);
        if (tenant == null)
        {
            _logger.LogWarning("ExecuteAsync: tenant {TenantId} not found", tenantId);
            return;
        }
        _tenantContext.SetTenant(tenant.Id, tenant.Name, tenant.TimeZone);

        try
        {
            var mapping = DeserializeMapping(batch.MappingJson);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            while (true)
            {
                var chunk = await _dbContext.ImportRows.IgnoreQueryFilters()
                    .Where(r => r.BatchId == batchId && r.TenantId == tenantId && r.Status == "ok")
                    .OrderBy(r => r.RowNumber)
                    .Take(ExecuteChunkSize)
                    .ToListAsync();

                if (chunk.Count == 0)
                    break;

                var plans = await _dbContext.MembershipPlans.IgnoreQueryFilters()
                    .Where(p => p.TenantId == tenantId && !p.IsDeleted && p.IsActive)
                    .ToListAsync();

                foreach (var row in chunk)
                {
                    var raw = DeserializeRaw(row.RawJson);
                    var mapped = ApplyMapping(raw, mapping);

                    var normalizedPhone = PhoneNormalizer.Normalize(mapped.GetValueOrDefault("phoneNumber"));
                    var (plan, _) = MatchPlan(mapped.GetValueOrDefault("planName", string.Empty), plans);

                    if (normalizedPhone == null || plan == null)
                    {
                        row.Status = "error";
                        row.ErrorCodes = normalizedPhone == null ? ImportRowErrorCodes.PhoneInvalid : ImportRowErrorCodes.PlanUnmatched;
                        continue;
                    }

                    var dateOfBirth = ParseDate(mapped.GetValueOrDefault("dateOfBirth"))
                        ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20));

                    Result<MemberDetailDto> createResult;
                    try
                    {
                        createResult = await _memberService.CreateMemberAsync(tenantId, new CreateMemberRequest
                        {
                            FullName = mapped.GetValueOrDefault("fullName", string.Empty),
                            FullNameAr = string.Empty,
                            Phone = normalizedPhone,
                            DateOfBirth = dateOfBirth
                        });
                    }
                    catch (DbUpdateException ex)
                    {
                        // Soft-deleted member can still occupy the phone unique index until a filtered
                        // index is applied; detach the failed insert so the batch can continue.
                        _logger.LogWarning(ex, "ExecuteAsync: CreateMember failed for batch {BatchId} row {Row}",
                            batchId, row.RowNumber);
                        foreach (var entry in _dbContext.ChangeTracker.Entries<GymMember>()
                                     .Where(e => e.State == EntityState.Added).ToList())
                            entry.State = EntityState.Detached;

                        row.Status = "error";
                        row.ErrorCodes = ImportRowErrorCodes.PhoneExists;
                        row.UpdatedAtUtc = DateTime.UtcNow;
                        continue;
                    }

                    if (!createResult.IsSuccess)
                    {
                        row.Status = "error";
                        row.ErrorCodes = createResult.Error;
                        continue;
                    }

                    var startDate = ParseDate(mapped.GetValueOrDefault("startDate")) ?? today;
                    var endDate = ParseDate(mapped.GetValueOrDefault("endDate")) ?? startDate.AddDays(plan.DurationDays);

                    int? sessionsRemaining = null;
                    if (plan.PlanType == "session_pack" && int.TryParse(mapped.GetValueOrDefault("sessionsRemaining"), out var sessions))
                        sessionsRemaining = sessions;

                    var membership = new Membership
                    {
                        TenantId = tenantId,
                        MemberId = createResult.Data!.Id,
                        PlanId = plan.Id,
                        StartDate = startDate,
                        EndDate = endDate,
                        Status = endDate < today ? "expired" : "active",
                        SessionsRemaining = sessionsRemaining,
                        PaymentMethod = "imported",
                        AmountPaid = 0m
                    };
                    _dbContext.Memberships.Add(membership);
                    await _dbContext.SaveChangesAsync();

                    row.Status = "imported";
                    row.CreatedMemberId = createResult.Data.Id;
                    row.CreatedMembershipId = membership.Id;
                    row.UpdatedAtUtc = DateTime.UtcNow;
                }

                await _dbContext.SaveChangesAsync();
            }

            batch.Status = "completed";
            batch.CompletedAt = DateTime.UtcNow;
            batch.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("ExecuteAsync: batch {BatchId} completed", batchId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExecuteAsync failed for batch {BatchId}", batchId);
            // A failed CreateMember insert can leave an Added GymMember in the tracker; clearing
            // lets us persist Status=failed instead of re-throwing the same unique-key error.
            _dbContext.ChangeTracker.Clear();
            batch.Status = "failed";
            batch.UpdatedAtUtc = DateTime.UtcNow;
            _dbContext.ImportBatches.Update(batch);
            await _dbContext.SaveChangesAsync();
        }
    }

    // ========================================================================
    // PRIVATE HELPERS
    // ========================================================================

    /// <summary>Feature-flag no-op guard for the two Hangfire job entry points — the jobs themselves
    /// stay on the schedule; only the per-tenant work is skipped when "imports" is disabled.</summary>
    private Task<bool> IsImportsFeatureEnabledAsync(Guid tenantId) =>
        _featureAccess.IsEnabledAsync(tenantId, "imports");

    private static void BackgroundJobEnqueue(Guid batchId, Guid tenantId) =>
        BackgroundJob.Enqueue<ValidateImportJob>(job => job.ExecuteAsync(batchId, tenantId));

    private static (List<string> Headers, List<Dictionary<string, string>> Rows) ParseFile(Stream stream, string extension) =>
        extension == ".xlsx" ? ParseXlsx(stream) : ParseCsv(stream);

    private static (List<string>, List<Dictionary<string, string>>) ParseXlsx(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.First();
        var usedRows = worksheet.RowsUsed().ToList();

        if (usedRows.Count == 0)
            return (new List<string>(), new List<Dictionary<string, string>>());

        var headerRow = usedRows[0];
        var lastCol = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;
        var headers = Enumerable.Range(1, lastCol).Select(c => headerRow.Cell(c).GetString().Trim()).ToList();

        var rows = new List<Dictionary<string, string>>();
        for (var i = 1; i < usedRows.Count; i++)
        {
            var dataRow = usedRows[i];
            var dict = new Dictionary<string, string>();
            for (var c = 0; c < headers.Count; c++)
                dict[headers[c]] = dataRow.Cell(c + 1).GetString().Trim();

            rows.Add(dict);
        }

        return (headers, rows);
    }

    private static (List<string>, List<Dictionary<string, string>>) ParseCsv(Stream stream)
    {
        // leaveOpen: true — the caller (UploadAsync) reuses this same buffered stream afterward to
        // upload the raw file bytes; StreamReader's default Dispose() would otherwise close it too.
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,
            MissingFieldFound = null
        });

        csv.Read();
        csv.ReadHeader();
        var headers = csv.HeaderRecord?.Select(h => h.Trim()).ToList() ?? new List<string>();

        var rows = new List<Dictionary<string, string>>();
        while (csv.Read())
        {
            var dict = new Dictionary<string, string>();
            foreach (var h in headers)
                dict[h] = (csv.GetField(h) ?? string.Empty).Trim();

            rows.Add(dict);
        }

        return (headers, rows);
    }

    private static Dictionary<string, string> AutoMapHeaders(List<string> headers)
    {
        var mapping = new Dictionary<string, string>();

        foreach (var header in headers)
        {
            var trimmed = header.Trim();
            if (HeaderAliases.TryGetValue(trimmed, out var exact))
            {
                mapping[header] = exact;
                continue;
            }

            string? bestField = null;
            var bestDistance = int.MaxValue;

            foreach (var (alias, field) in HeaderAliases)
            {
                var distance = LevenshteinDistance(trimmed.ToLowerInvariant(), alias.ToLowerInvariant());
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestField = field;
                }
            }

            if (bestField != null && bestDistance <= FuzzyMatchThreshold)
                mapping[header] = bestField;
        }

        return mapping;
    }

    private static Dictionary<string, string> ApplyMapping(Dictionary<string, string> raw, Dictionary<string, string> mapping)
    {
        var mapped = new Dictionary<string, string>();
        foreach (var (sourceHeader, targetField) in mapping)
        {
            if (raw.TryGetValue(sourceHeader, out var value))
                mapped[targetField] = value;
        }
        return mapped;
    }

    private static (MembershipPlan? Plan, List<MembershipPlan> Ranked) MatchPlan(string planNameCell, List<MembershipPlan> plans)
    {
        var ranked = RankPlanCandidates(planNameCell, plans);
        var best = ranked.FirstOrDefault();
        return (best != null && PlanDistance(planNameCell, best) <= FuzzyMatchThreshold ? best : null, ranked);
    }

    private static List<MembershipPlan> RankPlanCandidates(string planNameCell, List<MembershipPlan> plans) =>
        plans.OrderBy(p => PlanDistance(planNameCell, p)).ToList();

    private static int PlanDistance(string planNameCell, MembershipPlan plan)
    {
        var cell = (planNameCell ?? string.Empty).Trim().ToLowerInvariant();
        return Math.Min(
            LevenshteinDistance(cell, plan.Name.Trim().ToLowerInvariant()),
            LevenshteinDistance(cell, plan.NameAr.Trim().ToLowerInvariant()));
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) dp[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + cost);
            }
        }

        return dp[a.Length, b.Length];
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;

        string[] formats = { "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "d/M/yyyy", "yyyy/MM/dd" };
        foreach (var format in formats)
            if (DateOnly.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
                return exact;

        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var serial))
        {
            try { return DateOnly.FromDateTime(DateTime.FromOADate(serial)); }
            catch (ArgumentException) { /* not a valid OA date serial */ }
        }

        return null;
    }

    private static string AppendCode(string? existing, string code) =>
        string.IsNullOrEmpty(existing) ? code : $"{existing},{code}";

    private static Dictionary<string, string> DeserializeMapping(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();

    private static Dictionary<string, string> DeserializeRaw(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;

    private static ImportBatchDto ToDto(ImportBatch batch, Dictionary<string, string> mapping) => new()
    {
        Id = batch.Id,
        FileName = batch.FileName,
        Status = batch.Status,
        TotalRows = batch.TotalRows,
        OkRows = batch.OkRows,
        ErrorRows = batch.ErrorRows,
        Mapping = mapping,
        CompletedAt = batch.CompletedAt,
        CreatedAtUtc = batch.CreatedAtUtc
    };

    private static Result<ImportBatchDto> Fail(string code, string message) =>
        Result<ImportBatchDto>.Failure($"{code}|{message}");
}
