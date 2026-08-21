namespace GMS.Application.DTOs.Debtors;

/// <summary>
/// Outstanding sales for one member (Collect Payment). Same filter as the debtors total:
/// <c>partially_paid</c> and <c>AmountDue &gt; 0</c>.
/// </summary>
public class MemberOutstandingSalesDto
{
    public Guid MemberId { get; set; }
    public decimal TotalDue { get; set; }
    public List<OutstandingSaleDto> Sales { get; set; } = new();
}

public class OutstandingSaleDto
{
    public Guid SaleId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateOnly? DueDate { get; set; }
    public string Status { get; set; } = "partially_paid";
    public string Description { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public decimal Paid { get; set; }
    public decimal AmountDue { get; set; }
}
