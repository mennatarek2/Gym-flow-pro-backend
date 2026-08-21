namespace GMS.Core.Entities;

public class PlanEntitlement : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid PlanId { get; set; }
    public Guid ActivityId { get; set; }
    public string AccessMode { get; set; } = "included";
    public int? QuotaLimit { get; set; }
    public string? QuotaPeriod { get; set; }

    public MembershipPlan? Plan { get; set; }
    public Activity? Activity { get; set; }
}
