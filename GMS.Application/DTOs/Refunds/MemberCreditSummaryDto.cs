namespace GMS.Application.DTOs.Refunds;

/// <summary>Response body for GET /api/members/{id}/credits.</summary>
public class MemberCreditSummaryDto
{
    public decimal Balance { get; set; }
    public List<MemberCreditEntryDto> Entries { get; set; } = new();
}
