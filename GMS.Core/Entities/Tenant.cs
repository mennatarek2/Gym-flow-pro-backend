namespace GMS.Core.Entities;

/// <summary>
/// Represents a tenant (gym organization) in the system.
/// Each tenant has isolated data in shared-schema multi-tenancy model.
/// </summary>
public class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string GymCode { get; set; } = string.Empty; // Unique code for tenant resolution (e.g. "GYM-CAIRO-01")
    public string City { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string LogoUrl { get; set; } = string.Empty;
    public string TimeZone { get; set; } = "Africa/Cairo";
    public string Currency { get; set; } = "EGP";
    public int MaxMembers { get; set; } = 1000;
    public bool IsActive { get; set; } = true;
    public DateTime SubscriptionStartDate { get; set; }
    public DateTime? SubscriptionEndDate { get; set; }

    /// <summary>JSON blob of tenant-level settings — see <see cref="Constants.TenantSettingsKeys"/> for known keys.</summary>
    public string? Settings { get; set; }

    // Navigation
    public ICollection<GymMember> Members { get; set; } = new List<GymMember>();
    public ICollection<MembershipPlan> MembershipPlans { get; set; } = new List<MembershipPlan>();
    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
    public ICollection<GymAttendance> Attendances { get; set; } = new List<GymAttendance>();
    public ICollection<MemberInvitation> Invitations { get; set; } = new List<MemberInvitation>();
    public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
}
