namespace GMS.Application.DTOs.Audit;

/// <summary>
/// Optional filters + pagination for GET /api/audit. All filter fields are optional.
/// </summary>
public class AuditEventQueryRequest
{
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? Action { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
