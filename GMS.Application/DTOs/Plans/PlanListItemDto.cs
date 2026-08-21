namespace GMS.Application.DTOs.Plans;

/// <summary>
/// Lightweight DTO for membership plan list/search endpoints.
/// </summary>
public class PlanListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "EGP";
    public int DurationDays { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
