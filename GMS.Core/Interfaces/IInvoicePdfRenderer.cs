namespace GMS.Core.Interfaces;

using GMS.Core.Models;

/// <summary>
/// Renders a legal invoice/credit-note PDF from a flat snapshot model (no persistence dependency).
/// </summary>
public interface IInvoicePdfRenderer
{
    byte[] Render(InvoicePdfModel model);
}
