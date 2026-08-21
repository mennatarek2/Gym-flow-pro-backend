namespace GMS.Platform.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Platform.Constants;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

public class AutomationEnrollmentService : IAutomationEnrollmentService
{
    private readonly PlatformDbContext _db;
    private readonly ILogger<AutomationEnrollmentService> _logger;

    public AutomationEnrollmentService(PlatformDbContext db, ILogger<AutomationEnrollmentService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<AutomationEnrollment> EnrollAsync(
        string sequenceKey,
        string subjectType,
        Guid subjectId,
        Guid? tenantId,
        DateTime firstRunAtUtc,
        int initialStep = 0,
        CancellationToken cancellationToken = default)
    {
        sequenceKey = sequenceKey.Trim().ToLowerInvariant();
        subjectType = subjectType.Trim().ToLowerInvariant();

        var existing = await _db.AutomationEnrollments
            .FirstOrDefaultAsync(
                e => e.SequenceKey == sequenceKey &&
                     e.SubjectType == subjectType &&
                     e.SubjectId == subjectId &&
                     e.HaltedReason == null,
                cancellationToken);

        if (existing != null)
            return existing;

        var enrollment = new AutomationEnrollment
        {
            SequenceKey = sequenceKey,
            SubjectType = subjectType,
            SubjectId = subjectId,
            TenantId = tenantId,
            Step = initialStep,
            NextRunAtUtc = firstRunAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.AutomationEnrollments.Add(enrollment);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Race on unique active index — return the winner.
            var raced = await _db.AutomationEnrollments
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    e => e.SequenceKey == sequenceKey &&
                         e.SubjectType == subjectType &&
                         e.SubjectId == subjectId &&
                         e.HaltedReason == null,
                    cancellationToken);
            if (raced != null)
                return raced;
            throw;
        }

        _logger.LogInformation(
            "Enrolled automation {SequenceKey} for {SubjectType}/{SubjectId} step {Step} at {NextRun}",
            sequenceKey, subjectType, subjectId, initialStep, firstRunAtUtc);

        return enrollment;
    }

    public async Task<bool> HaltAsync(
        string subjectType,
        Guid subjectId,
        string reason,
        string? sequenceKey = null,
        CancellationToken cancellationToken = default)
    {
        subjectType = subjectType.Trim().ToLowerInvariant();
        reason = reason.Trim().ToLowerInvariant();

        var query = _db.AutomationEnrollments
            .Where(e => e.SubjectType == subjectType &&
                        e.SubjectId == subjectId &&
                        e.HaltedReason == null);

        if (!string.IsNullOrWhiteSpace(sequenceKey))
        {
            var key = sequenceKey.Trim().ToLowerInvariant();
            query = query.Where(e => e.SequenceKey == key);
        }

        var active = await query.ToListAsync(cancellationToken);
        if (active.Count == 0)
            return false;

        var now = DateTime.UtcNow;
        foreach (var enrollment in active)
        {
            enrollment.HaltedReason = reason;
            enrollment.HaltedAtUtc = now;
            enrollment.UpdatedAtUtc = now;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Halted {Count} automation enrollment(s) for {SubjectType}/{SubjectId}: {Reason}",
            active.Count, subjectType, subjectId, reason);

        return true;
    }

    public async Task<AutomationEnrollment?> GetActiveAsync(
        string subjectType,
        Guid subjectId,
        string? sequenceKey = null,
        CancellationToken cancellationToken = default)
    {
        subjectType = subjectType.Trim().ToLowerInvariant();
        var query = _db.AutomationEnrollments.AsNoTracking()
            .Where(e => e.SubjectType == subjectType &&
                        e.SubjectId == subjectId &&
                        e.HaltedReason == null);

        if (!string.IsNullOrWhiteSpace(sequenceKey))
        {
            var key = sequenceKey.Trim().ToLowerInvariant();
            query = query.Where(e => e.SequenceKey == key);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }
}
