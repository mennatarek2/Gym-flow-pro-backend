namespace GMS.Application.DTOs.Invoices;

public class InvoiceLineSnapshotDto
{
    public string? LineType { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? DescriptionAr { get; set; }
    public int Qty { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}
