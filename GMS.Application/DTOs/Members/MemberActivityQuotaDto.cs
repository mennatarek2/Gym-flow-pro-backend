namespace GMS.Application.DTOs.Members;

/// <summary>
/// Classes &amp; activities entitlement for the member's covering/current plan.
/// Limited = remaining/limit; unlimited/included have null remaining.
/// </summary>
public class MemberActivityQuotaDto
{
    public Guid ActivityId { get; set; }
    public string ActivityName { get; set; } = string.Empty;
    public string ActivityNameAr { get; set; } = string.Empty;
    public string ActivityKind { get; set; } = string.Empty;
    public string AccessMode { get; set; } = string.Empty;
    public int? QuotaLimit { get; set; }
    public int? QuotaRemaining { get; set; }
    public int? QuotaUsed { get; set; }
    public string? QuotaPeriod { get; set; }
}
