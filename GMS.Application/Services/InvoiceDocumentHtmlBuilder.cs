namespace GMS.Application.Services;

using System.Globalization;
using System.Net;
using System.Text;
using GMS.Core.Models;

public enum InvoiceDocumentLayout
{
    Thermal80mm,
    StandardA4
}

/// <summary>
/// Self-contained HTML for browser print. Same <see cref="InvoicePdfModel"/> as the PDF renderer.
/// Does not recalculate money — only presents snapshot fields that exist.
/// </summary>
public static class InvoiceDocumentHtmlBuilder
{
    public static string Build(InvoicePdfModel model, InvoiceDocumentLayout layout)
    {
        return layout == InvoiceDocumentLayout.StandardA4
            ? BuildStandard(model)
            : BuildThermal(model);
    }

    private static string BuildThermal(InvoicePdfModel model)
    {
        var isCredit = model.Type == "credit_note";
        var gym = Enc(DisplayGymName(model));
        var gymAr = Enc(model.TenantNameAr);
        var title = isCredit ? "CREDIT NOTE" : "INVOICE";
        var titleAr = isCredit ? "إشعار دائن" : "فاتورة";
        var member = Enc(DisplayMemberName(model));
        var primary = Enc(model.PrimaryColor);
        var sb = new StringBuilder(4096);

        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
          .Append("<title>").Append(Enc(model.InvoiceNumber)).Append("</title><style>")
          .Append("@page{size:80mm auto;margin:0}")
          .Append("@media print{html,body{width:80mm!important;margin:0!important;padding:0!important;")
          .Append("background:#fff!important;-webkit-print-color-adjust:exact!important;print-color-adjust:exact!important}}")
          .Append("*{box-sizing:border-box}body{width:80mm;max-width:80mm;margin:0;padding:6mm 5mm;")
          .Append("font-family:'Segoe UI','IBM Plex Sans','IBM Plex Sans Arabic',Tahoma,sans-serif;")
          .Append("font-size:11px;color:#1A1A1A;line-height:1.35}")
          .Append(".brand{text-align:center;margin-bottom:8px}")
          .Append(".logo{max-width:42mm;max-height:16mm;object-fit:contain;display:block;margin:0 auto 4px}")
          .Append(".gym{font-weight:800;font-size:14px;line-height:1.2}")
          .Append(".gym-ar{font-weight:700;font-size:12px;direction:rtl}")
          .Append(".short{font-size:10px;color:#4A4A4A;margin-top:2px}")
          .Append(".contact{font-size:9px;color:#4A4A4A;margin-top:4px}")
          .Append(".rule{border:0;border-top:1px dashed #bbb;margin:8px 0}")
          .Append(".kicker{text-align:center;font-size:9px;letter-spacing:.14em;font-weight:700;color:")
          .Append(primary).Append("}")
          .Append(".num{text-align:center;font-weight:800;font-size:13px;margin:2px 0 6px}")
          .Append(".row{display:flex;justify-content:space-between;gap:8px;margin:2px 0}")
          .Append(".muted{color:#6B6B6B}")
          .Append("table{width:100%;border-collapse:collapse;margin:6px 0}")
          .Append("td{padding:3px 0;vertical-align:top}td.r{text-align:right;white-space:nowrap}")
          .Append(".total{font-weight:800;font-size:13px;margin-top:4px}")
          .Append(".void{text-align:center;font-weight:800;color:#991B1B;border:1px solid #991B1B;padding:3px;margin:6px 0}")
          .Append(".foot{text-align:center;font-size:9px;color:#6B6B6B;margin-top:8px}")
          .Append("</style></head><body>");

        sb.Append("<div class=\"brand\">");
        AppendLogo(sb, model, "logo");
        sb.Append("<div class=\"gym\">").Append(gym).Append("</div>");
        if (ShowGymAr(model)) sb.Append("<div class=\"gym-ar\">").Append(gymAr).Append("</div>");
        if (!string.IsNullOrWhiteSpace(model.ShortName))
            sb.Append("<div class=\"short\">").Append(Enc(model.ShortName)).Append("</div>");
        AppendContactLines(sb, model, "contact");
        sb.Append("</div>");

        if (string.Equals(model.Status, "voided", StringComparison.OrdinalIgnoreCase))
            sb.Append("<div class=\"void\">VOIDED / ملغاة</div>");

        sb.Append("<div class=\"kicker\">").Append(title).Append(" · ").Append(titleAr).Append("</div>");
        sb.Append("<div class=\"num\">").Append(Enc(model.InvoiceNumber)).Append("</div>");
        sb.Append("<div class=\"row\"><span class=\"muted\">Date</span><span>")
          .Append(model.IssuedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append("</span></div>");
        sb.Append("<div class=\"row\"><span class=\"muted\">").Append(Enc(model.CustomerLabel)).Append("</span><span>")
          .Append(member).Append("</span></div>");
        if (!string.IsNullOrWhiteSpace(model.MemberPhone))
            sb.Append("<div class=\"row\"><span class=\"muted\">Phone</span><span>")
              .Append(Enc(model.MemberPhone)).Append("</span></div>");
        if (!string.IsNullOrWhiteSpace(model.PaymentMethod))
            sb.Append("<div class=\"row\"><span class=\"muted\">Payment</span><span>")
              .Append(Enc(FormatPaymentMethod(model.PaymentMethod))).Append("</span></div>");

        sb.Append("<hr class=\"rule\"><table>");
        var n = 1;
        foreach (var line in model.Lines)
        {
            var desc = string.IsNullOrWhiteSpace(line.DescriptionAr)
                ? line.Description
                : line.Description + " / " + line.DescriptionAr;
            var qty = line.Qty <= 0 ? 1 : line.Qty;
            sb.Append("<tr><td>").Append(n++).Append(". ").Append(Enc(desc))
              .Append(" ×").Append(qty).Append("</td><td class=\"r\">")
              .Append(Money(Signed(model, line.LineTotal), model.Currency)).Append("</td></tr>");
        }
        sb.Append("</table><hr class=\"rule\">");

        AppendMoneyRow(sb, "Subtotal", Signed(model, model.Subtotal), model.Currency, false);
        if (model.DiscountAmount != 0)
            AppendMoneyRow(sb, "Discount", Signed(model, model.DiscountAmount), model.Currency, false);
        AppendMoneyRow(sb, VatLabel(model), Signed(model, model.VatAmount), model.Currency, false);
        sb.Append("<div class=\"row total\"><span>TOTAL</span><span>")
          .Append(Money(Signed(model, model.Total), model.Currency)).Append("</span></div>");

        if (model.PaymentAmount.HasValue)
        {
            sb.Append("<hr class=\"rule\"><div class=\"row total\"><span>Payment received</span><span>")
              .Append(Money(model.PaymentAmount.Value, model.Currency)).Append("</span></div>");
            if (model.PaidAt.HasValue)
                sb.Append("<div class=\"row\"><span class=\"muted\">Paid</span><span>")
                  .Append(model.PaidAt.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
                  .Append("</span></div>");
        }

        sb.Append("<div class=\"foot\">Thank you for choosing ").Append(gym).Append("</div>");
        if (!string.IsNullOrWhiteSpace(model.FooterText))
            sb.Append("<div class=\"foot\">").Append(Enc(model.FooterText)).Append("</div>");
        if (!string.IsNullOrWhiteSpace(model.FooterTextAr))
            sb.Append("<div class=\"foot\" dir=\"rtl\">").Append(Enc(model.FooterTextAr)).Append("</div>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string BuildStandard(InvoicePdfModel model)
    {
        var isCredit = model.Type == "credit_note";
        var gym = Enc(DisplayGymName(model));
        var gymAr = Enc(model.TenantNameAr);
        var title = isCredit ? "CREDIT NOTE" : "INVOICE";
        var titleAr = isCredit ? "إشعار دائن" : "فاتورة";
        var member = Enc(DisplayMemberName(model));
        var primary = Enc(model.PrimaryColor);
        var accent = Enc(model.AccentColor);
        var sb = new StringBuilder(8192);

        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
          .Append("<title>").Append(Enc(model.InvoiceNumber)).Append("</title><style>")
          .Append("@page{size:A4;margin:12mm}")
          .Append("@media print{html,body{background:#fff!important;-webkit-print-color-adjust:exact!important;print-color-adjust:exact!important}")
          .Append("body{padding:0!important}.sheet{box-shadow:none;border:0;margin:0}}")
          .Append("*{box-sizing:border-box}body{margin:0;padding:16px;background:#F6F7F9;color:#1A1A1A;")
          .Append("font-family:'Segoe UI','IBM Plex Sans','IBM Plex Sans Arabic',Tahoma,sans-serif}")
          .Append(".sheet{max-width:190mm;margin:0 auto;background:#fff;border:1px solid #E8E8E8;padding:12mm 12mm 10mm}")
          .Append(".hdr{display:flex;justify-content:space-between;gap:16px;align-items:flex-start;")
          .Append("border-bottom:3px solid ").Append(primary).Append(";padding-bottom:14px;margin-bottom:18px}")
          .Append(".brand{display:flex;gap:12px;align-items:center;min-width:0}")
          .Append(".logo{width:56px;height:56px;object-fit:contain;background:#fff;border:1px solid #E8E8E8;border-radius:10px}")
          .Append(".mark{width:56px;height:56px;border-radius:10px;background:").Append(primary)
          .Append(";flex-shrink:0}")
          .Append(".gym{font-weight:800;font-size:22px;line-height:1.15}")
          .Append(".gym-ar{font-weight:700;font-size:15px;color:#4A4A4A;direction:rtl;margin-top:2px}")
          .Append(".short{font-size:12px;color:#6B6B6B;margin-top:4px}")
          .Append(".contact{font-size:11px;color:#4A4A4A;margin-top:8px;line-height:1.45}")
          .Append(".doc{text-align:right}")
          .Append(".kicker{font-size:11px;letter-spacing:.16em;font-weight:800;color:").Append(primary).Append("}")
          .Append(".num{display:inline-block;margin-top:6px;padding:6px 12px;border-radius:999px;background:")
          .Append(accent).Append(";color:#0D0D0D;font-weight:800;font-size:13px}")
          .Append(".cards{display:grid;grid-template-columns:1fr 1fr 1fr;gap:10px;margin-bottom:18px}")
          .Append(".card{background:#F5F5F5;border-radius:10px;padding:10px 12px}")
          .Append(".card .k{font-size:10px;font-weight:700;color:#8C8C8C;text-transform:uppercase;letter-spacing:.04em}")
          .Append(".card .v{font-size:14px;font-weight:700;margin-top:3px}")
          .Append("table.items{width:100%;border-collapse:collapse;margin-bottom:16px}")
          .Append("table.items th{text-align:left;font-size:11px;letter-spacing:.04em;text-transform:uppercase;")
          .Append("background:").Append(primary).Append(";color:#0D0D0D;padding:9px 10px}")
          .Append("table.items td{padding:10px;border-bottom:1px solid #E8E8E8;font-size:13px;vertical-align:top}")
          .Append("table.items td.r,table.items th.r{text-align:right}")
          .Append(".totals{margin-left:auto;width:min(280px,100%);background:#F5F5F5;border-radius:12px;padding:12px 14px}")
          .Append(".totals .row{display:flex;justify-content:space-between;gap:12px;margin:4px 0;font-size:13px}")
          .Append(".totals .grand{font-size:18px;font-weight:800;margin-top:8px;padding-top:8px;")
          .Append("border-top:2px solid ").Append(primary).Append("}")
          .Append(".notes{margin-top:18px;font-size:12px;color:#4A4A4A}")
          .Append(".void{font-weight:800;color:#991B1B;border:1px solid #FECACA;background:#FEE2E2;")
          .Append("padding:8px 12px;border-radius:8px;margin-bottom:12px}")
          .Append(".foot{margin-top:22px;padding-top:12px;border-top:1px solid #E8E8E8;text-align:center;")
          .Append("font-size:11px;color:#6B6B6B}")
          .Append(".foot strong{color:#1A1A1A}")
          .Append("@media(max-width:720px){.cards{grid-template-columns:1fr 1fr}.sheet{padding:16px}}")
          .Append("</style></head><body><div class=\"sheet\">");

        if (string.Equals(model.Status, "voided", StringComparison.OrdinalIgnoreCase))
            sb.Append("<div class=\"void\">VOIDED / ملغاة</div>");

        sb.Append("<div class=\"hdr\"><div class=\"brand\">");
        if (!AppendLogo(sb, model, "logo"))
            sb.Append("<div class=\"mark\"></div>");
        sb.Append("<div><div class=\"gym\">").Append(gym).Append("</div>");
        if (ShowGymAr(model)) sb.Append("<div class=\"gym-ar\">").Append(gymAr).Append("</div>");
        if (!string.IsNullOrWhiteSpace(model.ShortName))
            sb.Append("<div class=\"short\">").Append(Enc(model.ShortName)).Append("</div>");
        AppendContactLines(sb, model, "contact");
        sb.Append("</div></div><div class=\"doc\"><div class=\"kicker\">").Append(title)
          .Append("<div dir=\"rtl\" style=\"letter-spacing:0;margin-top:2px\">").Append(titleAr)
          .Append("</div></div><div class=\"num\">").Append(Enc(model.InvoiceNumber)).Append("</div></div></div>");

        sb.Append("<div class=\"cards\">");
        AppendCard(sb, "From", DisplayGymName(model));
        AppendCard(sb, "Date", model.IssuedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        AppendCard(sb, model.CustomerLabel, DisplayMemberName(model));
        if (!string.IsNullOrWhiteSpace(model.PaymentMethod))
            AppendCard(sb, "Payment", FormatPaymentMethod(model.PaymentMethod));
        if (!string.IsNullOrWhiteSpace(model.MemberPhone))
            AppendCard(sb, "Phone", model.MemberPhone);
        if (!string.IsNullOrWhiteSpace(model.TaxRegistrationNumber))
            AppendCard(sb, "Tax ID", model.TaxRegistrationNumber);
        sb.Append("</div>");

        sb.Append("<table class=\"items\"><thead><tr>")
          .Append("<th>#</th><th>Item / Description</th><th class=\"r\">Qty</th>")
          .Append("<th class=\"r\">Unit price</th><th class=\"r\">Total</th></tr></thead><tbody>");
        var i = 1;
        foreach (var line in model.Lines)
        {
            var desc = Enc(line.Description);
            var descAr = string.IsNullOrWhiteSpace(line.DescriptionAr) ? null : Enc(line.DescriptionAr);
            var qty = line.Qty <= 0 ? 1 : line.Qty;
            sb.Append("<tr><td>").Append(i++).Append("</td><td>").Append(desc);
            if (descAr != null)
                sb.Append("<div dir=\"rtl\" style=\"color:#6B6B6B;font-size:12px\">").Append(descAr).Append("</div>");
            sb.Append("</td><td class=\"r\">").Append(qty)
              .Append("</td><td class=\"r\">").Append(Money(Signed(model, line.UnitPrice), model.Currency))
              .Append("</td><td class=\"r\">").Append(Money(Signed(model, line.LineTotal), model.Currency))
              .Append("</td></tr>");
        }
        sb.Append("</tbody></table>");

        sb.Append("<div class=\"totals\">");
        AppendTotalRow(sb, "Subtotal", Signed(model, model.Subtotal), model.Currency, false);
        AppendTotalRow(sb, "Discount", Signed(model, model.DiscountAmount), model.Currency, false);
        AppendTotalRow(sb, VatLabel(model), Signed(model, model.VatAmount), model.Currency, false);
        sb.Append("<div class=\"row grand\"><span>TOTAL</span><span>")
          .Append(Money(Signed(model, model.Total), model.Currency)).Append("</span></div></div>");

        if (model.PaymentAmount.HasValue)
        {
            sb.Append("<div class=\"notes\"><strong>Payment received</strong> ")
              .Append(Money(model.PaymentAmount.Value, model.Currency));
            if (!string.IsNullOrWhiteSpace(model.PaymentMethod))
                sb.Append(" · ").Append(Enc(FormatPaymentMethod(model.PaymentMethod)));
            if (model.PaidAt.HasValue)
                sb.Append(" · ").Append(model.PaidAt.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            sb.Append("</div>");
        }

        if (!string.IsNullOrWhiteSpace(model.FooterText) || !string.IsNullOrWhiteSpace(model.FooterTextAr))
        {
            sb.Append("<div class=\"notes\">");
            if (!string.IsNullOrWhiteSpace(model.FooterText)) sb.Append(Enc(model.FooterText));
            if (!string.IsNullOrWhiteSpace(model.FooterTextAr))
                sb.Append("<div dir=\"rtl\">").Append(Enc(model.FooterTextAr)).Append("</div>");
            sb.Append("</div>");
        }

        sb.Append("<div class=\"foot\">Thank you for choosing <strong>").Append(gym)
          .Append("</strong><div>This is a computer generated invoice and does not require a signature.</div></div>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static bool AppendLogo(StringBuilder sb, InvoicePdfModel model, string cssClass)
    {
        var src = !string.IsNullOrWhiteSpace(model.LogoDataUri)
            ? model.LogoDataUri
            : IsSafeLogoUrl(model.LogoUrl) ? model.LogoUrl : null;
        if (src == null) return false;
        sb.Append("<img class=\"").Append(cssClass).Append("\" src=\"").Append(Enc(src))
          .Append("\" alt=\"").Append(Enc(DisplayGymName(model))).Append("\">");
        return true;
    }

    private static void AppendContactLines(StringBuilder sb, InvoicePdfModel model, string cssClass)
    {
        var bits = new List<string>();
        if (!string.IsNullOrWhiteSpace(model.Address)) bits.Add(Enc(model.Address));
        if (!string.IsNullOrWhiteSpace(model.PhoneNumber)) bits.Add(Enc(model.PhoneNumber));
        if (!string.IsNullOrWhiteSpace(model.Email)) bits.Add(Enc(model.Email));
        if (!string.IsNullOrWhiteSpace(model.TaxRegistrationNumber))
            bits.Add("Tax ID: " + Enc(model.TaxRegistrationNumber));
        if (bits.Count == 0) return;
        sb.Append("<div class=\"").Append(cssClass).Append("\">")
          .Append(string.Join("<br>", bits)).Append("</div>");
    }

    private static void AppendCard(StringBuilder sb, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append("<div class=\"card\"><div class=\"k\">").Append(Enc(label))
          .Append("</div><div class=\"v\">").Append(Enc(value)).Append("</div></div>");
    }

    private static void AppendMoneyRow(StringBuilder sb, string label, decimal amount, string currency, bool bold)
    {
        sb.Append("<div class=\"row").Append(bold ? " total" : "").Append("\"><span class=\"muted\">")
          .Append(Enc(label)).Append("</span><span>").Append(Money(amount, currency)).Append("</span></div>");
    }

    private static void AppendTotalRow(StringBuilder sb, string label, decimal amount, string currency, bool _)
    {
        sb.Append("<div class=\"row\"><span>").Append(Enc(label)).Append("</span><span>")
          .Append(Money(amount, currency)).Append("</span></div>");
    }

    private static string DisplayGymName(InvoicePdfModel model)
        => string.IsNullOrWhiteSpace(model.TenantName) ? "GymFlowPro Gym" : model.TenantName.Trim();

    private static string DisplayMemberName(InvoicePdfModel model)
        => string.IsNullOrWhiteSpace(model.MemberName) ? "Walk-in Customer" : model.MemberName.Trim();

    private static bool ShowGymAr(InvoicePdfModel model)
        => !string.IsNullOrWhiteSpace(model.TenantNameAr)
           && !string.Equals(model.TenantName?.Trim(), model.TenantNameAr.Trim(), StringComparison.OrdinalIgnoreCase);

    private static decimal Signed(InvoicePdfModel model, decimal value)
        => model.Type == "credit_note" ? -value : value;

    private static string VatLabel(InvoicePdfModel model)
    {
        var pct = model.VatRate <= 1m ? model.VatRate * 100m : model.VatRate;
        return $"VAT ({pct:0}%)";
    }

    private static string FormatPaymentMethod(string method)
    {
        return method.Trim().ToLowerInvariant() switch
        {
            "cash" => "Cash",
            "card" or "card_paymob" => "Card",
            "wallet" or "vodafone" => "Wallet",
            "bank" or "instapay" => "Bank",
            "fawry" => "Fawry",
            "account_credit" => "Account credit",
            _ => method.Trim()
        };
    }

    private static string Money(decimal amount, string currency)
        => string.Format(CultureInfo.InvariantCulture, "{0:N2} {1}", amount, currency);

    private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static bool IsSafeLogoUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (url.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            return !url.Contains("..", StringComparison.Ordinal);
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                   && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
        return url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);
    }
}
