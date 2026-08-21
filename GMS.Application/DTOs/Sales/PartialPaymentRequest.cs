namespace GMS.Application.DTOs.Sales;

/// <summary>Presence of this object signals the caller intends to leave a balance due.</summary>
public class PartialPaymentRequest
{
    public DateOnly DueDate { get; set; }
}
