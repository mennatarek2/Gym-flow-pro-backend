namespace GMS.Platform.DTOs;

public class RiskQueueItemDto
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GymCode { get; set; } = string.Empty;
    public string? PlanTier { get; set; }
    public string? SubscriptionStatus { get; set; }
    public int Score { get; set; }
    public string RiskBand { get; set; } = string.Empty;
    public DateTime ComputedAtUtc { get; set; }
    public Guid? AssignedPlatformUserId { get; set; }
    public DateTime? AssignedAtUtc { get; set; }
    public string? ContributingFactorsJson { get; set; }
    public string? Summary { get; set; }
    public List<RiskQueueOutcomeDto> RecentOutcomes { get; set; } = new();
}

public class RiskQueueOutcomeDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PlatformUserId { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class AssignRiskQueueRequest
{
    /// <summary>Null clears the assignee.</summary>
    public Guid? AssignedPlatformUserId { get; set; }
}

public class RecordRiskQueueOutcomeRequest
{
    public string Outcome { get; set; } = string.Empty;
    public string? Note { get; set; }
}
