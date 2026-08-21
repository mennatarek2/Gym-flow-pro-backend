namespace GMS.Application.DTOs.Admin;

public class UpdateTaxSettingsRequest
{
    public bool VatEnabled { get; set; }
    public decimal VatRate { get; set; }
    public string? TaxRegistrationNumber { get; set; }
    public string? InvoiceFooterText { get; set; }
    public string? InvoiceFooterTextAr { get; set; }
}
