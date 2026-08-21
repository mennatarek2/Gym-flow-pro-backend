namespace GMS.Application.DTOs.CallSheet;

public class RenewalRateDto
{
    public Guid StaffUserId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public int TotalCalled { get; set; }
    public int Renewed { get; set; }
    public decimal RenewalRatePercent { get; set; }
}
