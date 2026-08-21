namespace GMS.Infrastructure.Services;

using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using GMS.Core.Interfaces;
using GMS.Core.Models;

/// <summary>
/// Renders a bilingual A4 invoice/credit-note PDF branded from Gym Identity
/// (name, logo, colors, contact). Platform invoices reuse the same renderer with their own model.
/// </summary>
public class InvoicePdfRenderer : IInvoicePdfRenderer
{
    private const char LeftToRightIsolate = '⁦';
    private const char PopDirectionalIsolate = '⁩';

    static InvoicePdfRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Render(InvoicePdfModel model)
    {
        var isCreditNote = model.Type == "credit_note";
        var qrBytes = GenerateQrCode(model.InvoiceNumber);
        var gymName = string.IsNullOrWhiteSpace(model.TenantName) ? "GymFlowPro Gym" : model.TenantName.Trim();
        var memberName = string.IsNullOrWhiteSpace(model.MemberName) ? "Walk-in Customer" : model.MemberName.Trim();
        var primary = ParseHex(model.PrimaryColor) ?? Color.FromRGB(122, 204, 0);
        var ink = Colors.Grey.Darken4;
        var muted = Colors.Grey.Darken1;
        var isVoided = string.Equals(model.Status, "voided", StringComparison.OrdinalIgnoreCase);
        var sign = isCreditNote ? -1m : 1m;
        var amountColor = isCreditNote ? Colors.Red.Medium : ink;
        var titleEn = isCreditNote ? "CREDIT NOTE" : "INVOICE";
        var titleAr = isCreditNote ? "إشعار دائن" : "فاتورة";

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(28);
                page.MarginVertical(24);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(ink));

                page.Content().Layers(layers =>
                {
                    layers.PrimaryLayer().Column(column =>
                    {
                        column.Spacing(10);

                        column.Item().Row(row =>
                        {
                            row.RelativeItem().Row(brand =>
                            {
                                if (TryLogo(model.LogoImageBytes, out var logoBytes))
                                {
                                    brand.ConstantItem(52).Height(44).Image(logoBytes).FitArea();
                                    brand.ConstantItem(10);
                                }

                                brand.RelativeItem().Column(header =>
                                {
                                    header.Item().Text(gymName).FontSize(18).Bold();
                                    if (!string.IsNullOrWhiteSpace(model.TenantNameAr)
                                        && !string.Equals(gymName, model.TenantNameAr.Trim(), StringComparison.OrdinalIgnoreCase))
                                        header.Item().AlignRight().Text(model.TenantNameAr).FontSize(12).Bold();
                                    if (!string.IsNullOrWhiteSpace(model.ShortName))
                                        header.Item().Text(model.ShortName).FontSize(9).FontColor(muted);
                                    foreach (var line in ContactLines(model))
                                        header.Item().Text(line).FontSize(8).FontColor(muted);
                                });
                            });

                            row.ConstantItem(16);
                            row.ConstantItem(200).AlignRight().Column(doc =>
                            {
                                doc.Item().Text(titleEn).FontSize(11).Bold().FontColor(isCreditNote ? Colors.Red.Medium : primary);
                                doc.Item().AlignRight().Text(titleAr).FontSize(11).Bold().FontColor(isCreditNote ? Colors.Red.Medium : primary);
                                doc.Item().PaddingTop(6).Background(primary).Padding(6)
                                    .Text(Isolate(model.InvoiceNumber)).FontSize(11).Bold().FontColor(Colors.Grey.Darken4);
                                doc.Item().PaddingTop(8).Width(56).Image(qrBytes);
                            });
                        });

                        column.Item().Height(3).Background(primary);

                        if (isVoided)
                        {
                            column.Item().Border(1).BorderColor(Colors.Red.Medium).Background(Colors.Red.Lighten4)
                                .Padding(8).Text("VOIDED / ملغاة").Bold().FontColor(Colors.Red.Darken2);
                        }

                        column.Item().Row(info =>
                        {
                            InfoCard(info.RelativeItem(), "From", gymName);
                            info.ConstantItem(8);
                            InfoCard(info.RelativeItem(), "Date", model.IssuedAt.ToString("yyyy-MM-dd HH:mm"));
                            info.ConstantItem(8);
                            InfoCard(info.RelativeItem(), model.CustomerLabel, memberName);
                        });

                        if (!string.IsNullOrWhiteSpace(model.MemberPhone) || !string.IsNullOrWhiteSpace(model.PaymentMethod)
                            || !string.IsNullOrWhiteSpace(model.TaxRegistrationNumber))
                        {
                            column.Item().Row(info =>
                            {
                                if (!string.IsNullOrWhiteSpace(model.MemberPhone))
                                {
                                    InfoCard(info.RelativeItem(), "Phone", Isolate(model.MemberPhone));
                                    info.ConstantItem(8);
                                }
                                if (!string.IsNullOrWhiteSpace(model.PaymentMethod))
                                {
                                    InfoCard(info.RelativeItem(), "Payment", model.PaymentMethod);
                                    info.ConstantItem(8);
                                }
                                if (!string.IsNullOrWhiteSpace(model.TaxRegistrationNumber))
                                    InfoCard(info.RelativeItem(), "Tax ID", Isolate(model.TaxRegistrationNumber));
                                else
                                    info.RelativeItem();
                            });
                        }

                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(22);
                                columns.RelativeColumn(5);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(primary).Padding(6).Text("#").Bold();
                                header.Cell().Background(primary).Padding(6).Text("Item / Description").Bold();
                                header.Cell().Background(primary).Padding(6).AlignRight().Text("Qty").Bold();
                                header.Cell().Background(primary).Padding(6).AlignRight().Text("Unit price").Bold();
                                header.Cell().Background(primary).Padding(6).AlignRight().Text("Total").Bold();
                            });

                            var i = 1;
                            foreach (var line in model.Lines)
                            {
                                var description = string.IsNullOrWhiteSpace(line.DescriptionAr)
                                    ? line.Description
                                    : $"{line.Description} / {line.DescriptionAr}";
                                var qty = line.Qty <= 0 ? 1 : line.Qty;

                                table.Cell().PaddingVertical(6).Text(i.ToString());
                                table.Cell().PaddingVertical(6).Text(description);
                                table.Cell().PaddingVertical(6).AlignRight().Text(qty.ToString());
                                table.Cell().PaddingVertical(6).AlignRight().Text($"{sign * line.UnitPrice:N2}").FontColor(amountColor);
                                table.Cell().PaddingVertical(6).AlignRight().Text($"{sign * line.LineTotal:N2}").FontColor(amountColor);
                                i++;
                            }
                        });

                        column.Item().AlignRight().Width(220).Background(Colors.Grey.Lighten4).Padding(10).Column(totals =>
                        {
                            totals.Item().Row(r =>
                            {
                                r.RelativeItem().Text("Subtotal");
                                r.ConstantItem(90).AlignRight().Text($"{sign * model.Subtotal:N2} {model.Currency}").FontColor(amountColor);
                            });
                            totals.Item().Row(r =>
                            {
                                r.RelativeItem().Text("Discount");
                                r.ConstantItem(90).AlignRight().Text($"{sign * model.DiscountAmount:N2} {model.Currency}").FontColor(amountColor);
                            });
                            var vatPct = model.VatRate <= 1m ? model.VatRate * 100m : model.VatRate;
                            totals.Item().Row(r =>
                            {
                                r.RelativeItem().Text($"VAT ({vatPct:0}%)");
                                r.ConstantItem(90).AlignRight().Text($"{sign * model.VatAmount:N2} {model.Currency}").FontColor(amountColor);
                            });
                            totals.Item().PaddingTop(6).BorderTop(2).BorderColor(primary).PaddingTop(6).Row(r =>
                            {
                                r.RelativeItem().Text("TOTAL").Bold().FontSize(12);
                                r.ConstantItem(110).AlignRight().Text($"{sign * model.Total:N2} {model.Currency}").Bold().FontSize(12).FontColor(amountColor);
                            });
                        });

                        if (model.PaymentAmount.HasValue)
                        {
                            column.Item().Text(text =>
                            {
                                text.Span("Payment received: ").Bold();
                                text.Span($"{model.PaymentAmount.Value:N2} {model.Currency}");
                                if (!string.IsNullOrWhiteSpace(model.PaymentMethod))
                                    text.Span($" · {model.PaymentMethod}");
                                if (model.PaidAt.HasValue)
                                    text.Span($" · {model.PaidAt:yyyy-MM-dd HH:mm}");
                            });
                        }

                        if (!string.IsNullOrWhiteSpace(model.FooterText) || !string.IsNullOrWhiteSpace(model.FooterTextAr))
                        {
                            if (!string.IsNullOrWhiteSpace(model.FooterText))
                                column.Item().Text(model.FooterText).FontSize(8).FontColor(muted);
                            if (!string.IsNullOrWhiteSpace(model.FooterTextAr))
                                column.Item().AlignRight().Text(model.FooterTextAr).FontSize(8).FontColor(muted);
                        }

                        column.Item().PaddingTop(8).AlignCenter().Column(foot =>
                        {
                            foot.Item().Text($"Thank you for choosing {gymName}").FontSize(9);
                            foot.Item().Text("This is a computer generated invoice and does not require a signature.")
                                .FontSize(8).FontColor(muted);
                        });
                    });

                    layers.Layer().AlignCenter().AlignMiddle().Rotate(-30).Text(text =>
                    {
                        text.Span("Original / أصل").FontSize(48).FontColor(Colors.Grey.Lighten3).Bold();
                    });
                });
            });
        });

        return document.GeneratePdf();
    }

    private static void InfoCard(IContainer box, string label, string value)
    {
        box.Background(Colors.Grey.Lighten4).Padding(8).Column(c =>
        {
            c.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
            c.Item().Text(value).FontSize(10).Bold();
        });
    }

    private static IEnumerable<string> ContactLines(InvoicePdfModel model)
    {
        if (!string.IsNullOrWhiteSpace(model.Address)) yield return model.Address!;
        if (!string.IsNullOrWhiteSpace(model.PhoneNumber)) yield return Isolate(model.PhoneNumber!);
        if (!string.IsNullOrWhiteSpace(model.Email)) yield return Isolate(model.Email!);
        if (!string.IsNullOrWhiteSpace(model.TaxRegistrationNumber))
            yield return $"Tax ID: {Isolate(model.TaxRegistrationNumber!)}";
    }

    private static bool TryLogo(byte[]? bytes, out byte[] logoBytes)
    {
        logoBytes = bytes ?? Array.Empty<byte>();
        return bytes != null && bytes.Length > 32;
    }

    private static Color? ParseHex(string? hex)
    {
        var v = (hex ?? string.Empty).Trim();
        if (v.StartsWith('#')) v = v[1..];
        if (v.Length != 6) return null;
        try
        {
            var r = Convert.ToByte(v[..2], 16);
            var g = Convert.ToByte(v[2..4], 16);
            var b = Convert.ToByte(v[4..6], 16);
            return Color.FromRGB(r, g, b);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Isolate(string value) => $"{LeftToRightIsolate}{value}{PopDirectionalIsolate}";

    private static byte[] GenerateQrCode(string invoiceNumber)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(invoiceNumber, QRCodeGenerator.ECCLevel.Q);
        var pngQrCode = new PngByteQRCode(data);
        return pngQrCode.GetGraphic(10);
    }
}
