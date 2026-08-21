namespace GMS.Application.DTOs.Shifts;

public class ShiftOpenSummaryDto
{
    public List<ShiftDto> OpenShifts { get; set; } = new();
    public decimal TotalCashInDrawers { get; set; }
}
