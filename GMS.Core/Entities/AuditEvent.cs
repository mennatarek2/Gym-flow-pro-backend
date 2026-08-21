namespace GMS.Core.Entities;

/// <summary>
/// Immutable audit trail record for sensitive/state-changing actions.
/// Never soft-deleted or hard-deleted.
/// </summary>
public class AuditEvent : BaseEntity
{
    public Guid TenantId { get; set; }

    /// <summary>AppUser.Id (domain PK) of the staff member who performed the action — NOT AspNetUsers.Id. Null for system/anonymous actions.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>Dotted action identifier, e.g. "checkin.manual", "sale.discount.override".</summary>
    public string Action { get; set; } = string.Empty;

    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }

    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>
    /// When set, the action was performed under a platform support impersonation token
    /// (JWT claim impersonated_by_platform_user_id).
    /// </summary>
    public Guid? ImpersonatedByPlatformUserId { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public AppUser? ActorUser { get; set; }
}
