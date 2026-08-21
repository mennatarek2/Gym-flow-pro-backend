namespace GMS.Application.DTOs.Activities;

public class PlanEntitlementDto
{
    public Guid Id { get; set; }
    public Guid ActivityId { get; set; }
    public string ActivityName { get; set; } = "";
    public string ActivityKind { get; set; } = "";
    public string AccessMode { get; set; } = "";
    public int? QuotaLimit { get; set; }
    public string? QuotaPeriod { get; set; }
}

public class UpsertPlanEntitlementRequest
{
    public Guid ActivityId { get; set; }
    public string? AccessMode { get; set; }
    public int? QuotaLimit { get; set; }
    public string? QuotaPeriod { get; set; }
}
