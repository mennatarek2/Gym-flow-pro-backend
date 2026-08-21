namespace GMS.Application.DTOs.Provisioning;

using System.ComponentModel.DataAnnotations;

/// <summary>Platform Ops+ request to provision a new gym tenant + Owner.</summary>
public class ProvisionTenantRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? NameAr { get; set; }

    [Required, MaxLength(100)]
    public string City { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Address { get; set; }

    [Required, MaxLength(40)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Optional override. If omitted, a unique GYM-XXXXXX code is generated.</summary>
    [MaxLength(32)]
    public string? GymCode { get; set; }

    [MaxLength(64)]
    public string? TimeZone { get; set; }

    [Required, MaxLength(200)]
    public string OwnerFullName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(256)]
    public string OwnerEmail { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string OwnerPassword { get; set; } = string.Empty;

    /// <summary>Platform plan tier for StartTrial (default growth).</summary>
    [MaxLength(32)]
    public string Tier { get; set; } = "growth";
}

public class ProvisionTenantResponse
{
    public Guid TenantId { get; set; }
    public string GymCode { get; set; } = string.Empty;
    public Guid OwnerUserId { get; set; }
    public string OwnerEmail { get; set; } = string.Empty;
    public bool TrialStarted { get; set; }
    public string? TrialError { get; set; }
}
