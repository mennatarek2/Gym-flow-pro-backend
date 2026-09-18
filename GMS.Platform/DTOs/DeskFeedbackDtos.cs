namespace GMS.Platform.DTOs;

public class SubmitDeskFeedbackRequest
{
    public string Category { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
    public string? PageUrl { get; set; }
    /// <summary>Optional client idempotency key (UUID). Same key + tenant returns the existing row.</summary>
    public string? ClientRequestId { get; set; }
}

public class DeskFeedbackActorContext
{
    public Guid TenantId { get; set; }
    public string? GymCode { get; set; }
    public string? GymName { get; set; }
    public Guid SenderUserId { get; set; }
    public string SenderRole { get; set; } = string.Empty;
    public string? SenderEmail { get; set; }
    public string? SenderDisplayName { get; set; }
    /// <summary>When set (license-key ingest), wins over TenantId/GymCode customer lookup.</summary>
    public Guid? PreferredCustomerId { get; set; }
}

/// <summary>License-key authenticated desk feedback from HyMotion Local → Platform.</summary>
public class IngestLocalDeskFeedbackRequest
{
    public string LicenseKey { get; set; } = string.Empty;
    public string InstallationId { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public Guid SenderUserId { get; set; }
    public string SenderRole { get; set; } = string.Empty;
    public string? SenderEmail { get; set; }
    public string? SenderDisplayName { get; set; }
    public string? GymCode { get; set; }
    public string? GymName { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
    public string? PageUrl { get; set; }
    public string? ClientRequestId { get; set; }
}

public class UpdateDeskFeedbackRequest
{
    public string? Status { get; set; }
    public string? InternalNote { get; set; }
    public string? ResponseToCustomer { get; set; }
}

public class DeskFeedbackDto
{
    public Guid Id { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public Guid TenantId { get; set; }
    public string? GymCode { get; set; }
    public string? GymName { get; set; }
    public Guid SenderUserId { get; set; }
    public string SenderRole { get; set; } = string.Empty;
    public string? SenderEmail { get; set; }
    public string? SenderDisplayName { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
    public string? PageUrl { get; set; }
    public string? InternalNote { get; set; }
    public string? ResponseToCustomer { get; set; }
    public Guid? ReviewedByPlatformAdminUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public bool AlreadySubmitted { get; set; }
}
