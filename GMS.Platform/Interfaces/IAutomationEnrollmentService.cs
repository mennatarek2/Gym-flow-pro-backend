namespace GMS.Platform.Interfaces;

using GMS.Platform.Entities;

public sealed class AutomationStepResult
{
    /// <summary>Advance to another step; null means sequence completed (auto-halt as completed).</summary>
    public int? NextStep { get; init; }
    public DateTime? NextRunAtUtc { get; init; }
    public bool Halt { get; init; }
    public string? HaltReason { get; init; }

    public static AutomationStepResult Schedule(int nextStep, DateTime nextRunAtUtc) => new()
    {
        NextStep = nextStep,
        NextRunAtUtc = nextRunAtUtc
    };

    public static AutomationStepResult Complete(string reason = "completed") => new()
    {
        Halt = true,
        HaltReason = reason
    };

    public static AutomationStepResult HaltNow(string reason) => new()
    {
        Halt = true,
        HaltReason = reason
    };
}

/// <summary>Per-sequence step executor. Register one implementation per SequenceKey.</summary>
public interface IAutomationSequenceHandler
{
    string SequenceKey { get; }

    Task<AutomationStepResult> ExecuteStepAsync(
        AutomationEnrollment enrollment,
        CancellationToken cancellationToken = default);
}

public interface IAutomationEnrollmentService
{
    /// <summary>Idempotent: no-op if an active enrollment already exists for the subject.</summary>
    Task<AutomationEnrollment> EnrollAsync(
        string sequenceKey,
        string subjectType,
        Guid subjectId,
        Guid? tenantId,
        DateTime firstRunAtUtc,
        int initialStep = 0,
        CancellationToken cancellationToken = default);

    /// <summary>Event-driven halt (e.g. payment webhook). Sub-minute — does not wait for the runner.</summary>
    Task<bool> HaltAsync(
        string subjectType,
        Guid subjectId,
        string reason,
        string? sequenceKey = null,
        CancellationToken cancellationToken = default);

    Task<AutomationEnrollment?> GetActiveAsync(
        string subjectType,
        Guid subjectId,
        string? sequenceKey = null,
        CancellationToken cancellationToken = default);
}

public interface IProcessAutomationEnrollmentsJob
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
