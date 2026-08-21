namespace GMS.Application.Interfaces;

/// <summary>
/// TEMPORARY (pre-P7) invoice delivery abstraction. Real PDF rendering + delivery (email/WhatsApp)
/// lands in P7 — until then, <see cref="Services.NullInvoiceDeliveryJob"/> is a no-op so invoice
/// creation itself isn't blocked on delivery being implemented.
/// </summary>
public interface IInvoiceDeliveryJob
{
    Task Execute(Guid invoiceId);
}
