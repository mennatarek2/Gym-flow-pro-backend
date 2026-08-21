namespace GMS.Core.Entities;

using GMS.Core.Constants;

public class Activity : BaseEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DescriptionAr { get; set; } = string.Empty;
    public string Kind { get; set; } = ActivityKinds.Class;
    public string? SystemKey { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public bool BookingRequired { get; set; } = true;
    public int? DefaultCapacity { get; set; }
    public int? DefaultDurationMinutes { get; set; }
    public decimal? DropInPrice { get; set; }
    public bool VisibleToMembers { get; set; } = true;

    public Tenant? Tenant { get; set; }
    public ICollection<ActivitySchedule> Schedules { get; set; } = new List<ActivitySchedule>();
    public ICollection<ActivitySession> Sessions { get; set; } = new List<ActivitySession>();
    public ICollection<PlanEntitlement> PlanEntitlements { get; set; } = new List<PlanEntitlement>();
}
