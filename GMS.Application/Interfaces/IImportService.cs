namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Imports;

/// <summary>
/// Excel/CSV bulk-import pipeline for members + current memberships: upload → auto-detected column
/// mapping → background validation (dry run) → background execution → time-boxed rollback.
/// </summary>
public interface IImportService
{
    /// <summary>Saves the file, sniffs headers for an auto-detected mapping, and enqueues
    /// ValidateImportJob. Rejects files over 5MB or 10,000 rows.</summary>
    Task<Result<ImportBatchDto>> UploadAsync(
        Stream fileStream, string fileName, string contentType, Guid staffUserId, Guid tenantId);

    /// <summary>Overrides the column mapping and re-enqueues validation.</summary>
    Task<Result<ImportBatchDto>> SetMappingAsync(Guid batchId, ColumnMapRequest mapping, Guid tenantId);

    Task<ImportBatchDto?> GetAsync(Guid batchId, Guid tenantId);

    /// <summary>CSV of every 'error' row (RowNumber, ErrorCodes, and the mapped field values).</summary>
    Task<byte[]?> GetErrorsCsvAsync(Guid batchId, Guid tenantId);

    /// <summary>Validates the batch is 'dry_run_ready' and enqueues ExecuteImportJob.</summary>
    Task<Result<bool>> EnqueueExecuteAsync(Guid batchId, Guid tenantId);

    /// <summary>Manager+ only, within 7 days of completion. Deletes created memberships then
    /// members in reverse order, except rows with post-import activity (attendance/sale/payment),
    /// which are retained and reported as such.</summary>
    Task<Result<ImportBatchDto>> RollbackAsync(Guid batchId, Guid managerUserId, Guid tenantId);

    /// <summary>Creates MembershipPlans for names the import referenced but couldn't match, then
    /// re-enqueues validation so PLAN_UNMATCHED rows get a chance to resolve.</summary>
    Task<Result<ImportBatchDto>> CreateMissingPlansAsync(Guid batchId, List<ImportPlanSpec> plans, Guid tenantId);

    /// <summary>A blank bilingual-header xlsx template with 3 example rows.</summary>
    byte[] BuildTemplateXlsx();

    // ── Hangfire job entry points (called from ValidateImportJob/ExecuteImportJob via DI) ──

    /// <summary>Re-maps every row's already-stored raw cell data using the batch's current mapping
    /// and validates it, then sets the batch to 'dry_run_ready' (or 'failed' on an unrecoverable
    /// error). Runs with no ambient tenant context — filters explicitly by tenantId throughout.</summary>
    Task ValidateAsync(Guid batchId, Guid tenantId);

    /// <summary>Processes 'ok' rows in chunks of 500 (inherently resumable — already-'imported'
    /// rows are never reprocessed), creating a GymMember + Membership per row. Runs with no ambient
    /// tenant context — filters explicitly by tenantId throughout.</summary>
    Task ExecuteAsync(Guid batchId, Guid tenantId);
}
