namespace GMS.Application.Services;

using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using GMS.Application.DTOs.Admin;
using GMS.Application.DTOs.Members;

/// <summary>
/// Self-contained HTML for browser-print member access cards (MAC-P0 Phase 1).
/// Identity only — no plan / expiry / price (C2).
/// Phase A branding: tenant logo + primary mark color (defaults when unset).
/// </summary>
public static class AccessCardHtmlBuilder
{
    private static readonly Regex SafeHex = new(@"^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    public static string Build(
        MemberDetailDto member,
        string gymName,
        string gymNameAr,
        string? logoUrl = null,
        string? primaryColor = null,
        bool showGymLogo = true,
        string? barcodePayload = null)
    {
        var name = WebUtility.HtmlEncode((member.FullName ?? string.Empty).Trim());
        var nameAr = WebUtility.HtmlEncode((member.FullNameAr ?? string.Empty).Trim());
        var memberNumberRaw = (member.MemberNumber ?? string.Empty).Trim();
        // Prefer Assigned AccessCard.Code when provided; else legacy MemberNumber barcode.
        var numberRaw = !string.IsNullOrWhiteSpace(barcodePayload)
            ? barcodePayload.Trim()
            : memberNumberRaw;
        var number = WebUtility.HtmlEncode(numberRaw);
        var gymEn = (gymName ?? string.Empty).Trim();
        var gymArRaw = (gymNameAr ?? string.Empty).Trim();
        var showGymAr = !string.IsNullOrWhiteSpace(gymArRaw)
                        && !string.Equals(gymEn, gymArRaw, StringComparison.OrdinalIgnoreCase);
        var gym = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(gymEn) ? "Gym" : gymEn);
        var gymAr = WebUtility.HtmlEncode(gymArRaw);
        var showNameAr = !string.IsNullOrWhiteSpace(member.FullNameAr)
                         && !string.Equals(
                             (member.FullName ?? string.Empty).Trim(),
                             (member.FullNameAr ?? string.Empty).Trim(),
                             StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(memberNumberRaw))
            throw new InvalidOperationException("MemberNumber is required to render an access card.");
        if (string.IsNullOrEmpty(numberRaw))
            throw new InvalidOperationException("Barcode payload is required to render an access card.");

        var color = NormalizeHex(primaryColor) ?? BrandingDefaults.PrimaryColor;
        var logo = (logoUrl ?? string.Empty).Trim();
        var useLogo = showGymLogo && !string.IsNullOrWhiteSpace(logo) && IsSafeLogoUrl(logo);
        var logoEncoded = useLogo ? WebUtility.HtmlEncode(logo) : null;

        var bars = Code128SvgRenderer.ToHtmlBars(numberRaw, modulePx: 2, heightPx: 46);

        var sb = new StringBuilder(4096);
        AppendDocumentStart(sb, number, color, screenCentered: true);
        sb.Append("<div class=\"sheet\">");
        AppendCardShellOpen(sb);
        AppendHeader(sb, useLogo, logoEncoded, gym, gymAr, showGymAr);
        AppendMemberIdentity(sb, name, nameAr, showNameAr);
        AppendScan(sb, bars, number);
        AppendHint(sb, memberHint: true);
        AppendCardShellClose(sb);
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// Blank PVC stock sheet for a batch: <b>gym identity first</b> + AccessCard.Code barcode.
    /// Front = identity + barcode; back = large gym logo. No member PII.
    /// HyMotion only as tiny powered-by on the back. CR80 86×54 mm — two pages per card (front then back).
    /// </summary>
    public static string BuildBlankStockBatch(
        IReadOnlyList<string> codes,
        string gymName,
        string gymNameAr,
        string? logoUrl = null,
        string? primaryColor = null,
        bool showGymLogo = true)
    {
        if (codes == null || codes.Count == 0)
            throw new InvalidOperationException("At least one card code is required.");

        var color = NormalizeHex(primaryColor) ?? BrandingDefaults.PrimaryColor;
        var logo = (logoUrl ?? string.Empty).Trim();
        var useLogo = showGymLogo && !string.IsNullOrWhiteSpace(logo) && IsSafeLogoUrl(logo);
        var logoEncoded = useLogo ? WebUtility.HtmlEncode(logo) : null;
        var gymEn = (gymName ?? string.Empty).Trim();
        var gymArRaw = (gymNameAr ?? string.Empty).Trim();
        var showGymAr = !string.IsNullOrWhiteSpace(gymArRaw)
                        && !string.Equals(gymEn, gymArRaw, StringComparison.OrdinalIgnoreCase);
        var gym = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(gymEn) ? "Gym" : gymEn);
        var gymAr = WebUtility.HtmlEncode(gymArRaw);

        var title = WebUtility.HtmlEncode($"{(string.IsNullOrWhiteSpace(gymEn) ? "Gym" : gymEn)} · Access cards · {codes.Count}");
        var sb = new StringBuilder(4096 + codes.Count * 2200);
        AppendBlankStockDocumentStart(sb, title, color);

        sb.Append("<div class=\"toolbar no-print\">")
          .Append("<div class=\"toolbar-title\">").Append(gym)
          .Append(" — batch print (").Append(codes.Count).Append(")</div>")
          .Append("<div class=\"toolbar-sub\">Blank PVC · front barcode + back gym logo · AccessCard.Code</div>")
          .Append("<button type=\"button\" onclick=\"window.print()\">Print</button>")
          .Append("</div>");

        sb.Append("<div class=\"preview-grid no-print\">");
        var previewCount = Math.Min(codes.Count, 3);
        for (var i = 0; i < previewCount; i++)
        {
            var code = (codes[i] ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(code)) continue;
            sb.Append("<div class=\"preview-pair\">")
              .Append("<div class=\"face-tag\">Front · ").Append(WebUtility.HtmlEncode(code)).Append("</div>");
            AppendGymStockFace(sb, useLogo, logoEncoded, gym, gymAr, showGymAr, color, code);
            sb.Append("<div class=\"face-tag\">Back</div>");
            AppendGymStockBack(sb, useLogo, logoEncoded, gym, gymAr, showGymAr, color);
            sb.Append("</div>");
        }
        if (codes.Count > previewCount)
        {
            sb.Append("<div class=\"preview-more\">+")
              .Append(codes.Count - previewCount)
              .Append(" more on print…</div>");
        }
        sb.Append("</div>");

        foreach (var raw in codes)
        {
            var code = (raw ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(code)) continue;
            sb.Append("<div class=\"sheet print-only\">");
            AppendGymStockFace(sb, useLogo, logoEncoded, gym, gymAr, showGymAr, color, code);
            sb.Append("</div>");
            sb.Append("<div class=\"sheet print-only\">");
            AppendGymStockBack(sb, useLogo, logoEncoded, gym, gymAr, showGymAr, color);
            sb.Append("</div>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>Front CR80 — gym brand + AccessCard.Code barcode only.</summary>
    private static void AppendGymStockFace(
        StringBuilder sb,
        bool useLogo,
        string? logoEncoded,
        string gym,
        string gymAr,
        bool showGymAr,
        string color,
        string codeRaw)
    {
        var number = WebUtility.HtmlEncode(codeRaw);
        var bars = Code128SvgRenderer.ToHtmlBars(codeRaw, modulePx: 2, heightPx: 48);

        sb.Append("<div class=\"card stock\">")
          .Append("<div class=\"accent\" style=\"background:")
          .Append(color)
          .Append("\"></div>")
          .Append("<div class=\"stock-hdr\">");
        if (useLogo)
            sb.Append("<img class=\"logo-lg\" src=\"").Append(logoEncoded).Append("\" alt=\"\">");
        else
            sb.Append("<div class=\"mark-lg\" style=\"background:")
              .Append(color)
              .Append("\" aria-hidden=\"true\"></div>");
        sb.Append("<div class=\"stock-names\">")
          .Append("<div class=\"gym-en\">").Append(gym).Append("</div>");
        if (showGymAr)
            sb.Append("<div class=\"gym-ar\">").Append(gymAr).Append("</div>");
        sb.Append("</div></div>")
          .Append("<div class=\"stock-label\">ACCESS CARD</div>")
          .Append("<div class=\"stock-label-ar\">كارنيه الدخول</div>")
          .Append("<div class=\"scan stock-scan\">")
          .Append("<div class=\"bars\">").Append(bars).Append("</div>")
          .Append("<div class=\"num\">").Append(number).Append("</div>")
          .Append("</div>")
          .Append("</div>");
    }

    /// <summary>Back CR80 — large centered gym logo (or brand mark), subtle names, tiny powered-by.</summary>
    private static void AppendGymStockBack(
        StringBuilder sb,
        bool useLogo,
        string? logoEncoded,
        string gym,
        string gymAr,
        bool showGymAr,
        string color)
    {
        sb.Append("<div class=\"card stock back\">")
          .Append("<div class=\"back-body\">");
        if (useLogo)
            sb.Append("<img class=\"logo-back\" src=\"").Append(logoEncoded).Append("\" alt=\"\">");
        else
            sb.Append("<div class=\"mark-back\" style=\"background:")
              .Append(color)
              .Append("\" aria-hidden=\"true\"></div>");
        sb.Append("<div class=\"back-gym\">").Append(gym).Append("</div>");
        if (showGymAr)
            sb.Append("<div class=\"back-gym-ar\">").Append(gymAr).Append("</div>");
        sb.Append("</div>")
          .Append("<div class=\"accent accent-bottom\" style=\"background:")
          .Append(color)
          .Append("\"></div>")
          .Append("<div class=\"powered\">Powered by HyMotion</div>")
          .Append("</div>");
    }

    private static void AppendBlankStockDocumentStart(StringBuilder sb, string titleEncoded, string color)
    {
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
          .Append("<title>").Append(titleEncoded).Append("</title>")
          .Append("<style>")
          .Append("@page{size:86mm 54mm;margin:0}")
          .Append("@media print{")
          .Append(".no-print{display:none!important}")
          .Append(".print-only{display:flex!important}")
          .Append("html,body{margin:0!important;padding:0!important;background:#fff!important;")
          .Append("-webkit-print-color-adjust:exact!important;print-color-adjust:exact!important}")
          .Append(".sheet{width:86mm!important;height:54mm!important;page-break-after:always;")
          .Append("break-after:page;margin:0!important;padding:1.6mm!important;")
          .Append("display:flex!important;align-items:center;justify-content:center;")
          .Append("box-shadow:none!important}")
          .Append(".sheet:last-child{page-break-after:auto;break-after:auto}")
          .Append(".card,.scan,.bars,.bc,.mark-lg,.logo-lg,.logo-back,.mark-back,.accent,span,img{")
          .Append("-webkit-print-color-adjust:exact!important;print-color-adjust:exact!important}")
          .Append("}")
          .Append("*{box-sizing:border-box;margin:0;padding:0}")
          .Append("html,body{color:#0D0D0D;font-family:'Segoe UI','IBM Plex Sans',Tahoma,sans-serif;")
          .Append("background:#E8EAED}")
          .Append(".print-only{display:none}")
          .Append(".sheet{width:86mm;height:54mm;display:flex;align-items:center;justify-content:center;padding:1.6mm}")
          .Append(".card.stock{width:82.8mm;height:50.8mm;background:#fff;border:1px solid #D8D8D8;")
          .Append("border-radius:2.4mm;padding:0;display:flex;flex-direction:column;overflow:hidden;")
          .Append("position:relative}")
          .Append(".accent{height:2.2mm;width:100%;flex:0 0 auto}")
          .Append(".accent-bottom{margin-top:auto}")
          .Append(".stock-hdr{display:flex;align-items:center;gap:2.8mm;padding:3.2mm 3.6mm 1.2mm;")
          .Append("flex:0 0 auto}")
          .Append(".logo-lg{width:11mm;height:11mm;border-radius:2mm;object-fit:contain;flex-shrink:0;")
          .Append("background:#fff;border:0.25mm solid #EBEBEB}")
          .Append(".mark-lg{width:11mm;height:11mm;border-radius:2mm;flex-shrink:0;")
          .Append("-webkit-print-color-adjust:exact;print-color-adjust:exact}")
          .Append(".stock-names{min-width:0;flex:1}")
          .Append(".gym-en{font-weight:800;font-size:4.4mm;line-height:1.1;color:#0D0D0D;")
          .Append("letter-spacing:-0.02em;text-transform:uppercase;")
          .Append("white-space:nowrap;overflow:hidden;text-overflow:ellipsis}")
          .Append(".gym-ar{font-weight:700;font-size:3.2mm;color:#4A4A4A;direction:rtl;margin-top:.6mm;")
          .Append("white-space:nowrap;overflow:hidden;text-overflow:ellipsis}")
          .Append(".stock-label{text-align:center;font-weight:700;font-size:2.6mm;letter-spacing:.28em;")
          .Append("color:").Append(color).Append(";margin-top:1mm;")
          .Append("-webkit-print-color-adjust:exact;print-color-adjust:exact}")
          .Append(".stock-label-ar{text-align:center;font-size:2.1mm;color:#8C8C8C;direction:rtl;")
          .Append("margin-top:.3mm;margin-bottom:.6mm}")
          .Append(".stock-scan{flex:1;display:flex;flex-direction:column;align-items:center;")
          .Append("justify-content:center;gap:1.2mm;padding:0 3mm 2.4mm;min-height:0}")
          .Append(".bars{width:100%;display:flex;justify-content:center;align-items:center;")
          .Append("overflow:visible;min-height:12mm}")
          .Append(".bc{max-width:74mm;transform-origin:center center}")
          .Append(".num{font-family:Consolas,'Courier New',monospace;font-weight:700;")
          .Append("font-size:3.2mm;letter-spacing:.14em;color:#0D0D0D}")
          .Append(".card.back{justify-content:stretch}")
          .Append(".back-body{flex:1;display:flex;flex-direction:column;align-items:center;")
          .Append("justify-content:center;gap:1.8mm;padding:4mm 4mm 2mm;min-height:0}")
          .Append(".logo-back{width:22mm;height:22mm;border-radius:3mm;object-fit:contain;")
          .Append("background:#fff;border:0.3mm solid #EBEBEB}")
          .Append(".mark-back{width:22mm;height:22mm;border-radius:3mm;flex-shrink:0;")
          .Append("-webkit-print-color-adjust:exact;print-color-adjust:exact}")
          .Append(".back-gym{font-weight:800;font-size:3.4mm;letter-spacing:-0.02em;")
          .Append("text-transform:uppercase;text-align:center;color:#0D0D0D}")
          .Append(".back-gym-ar{font-weight:700;font-size:2.6mm;color:#4A4A4A;direction:rtl;")
          .Append("text-align:center}")
          .Append(".powered{text-align:center;font-size:1.55mm;color:#B0B0B0;letter-spacing:.04em;")
          .Append("padding:0.8mm 0 1.4mm;flex:0 0 auto}")
          .Append(".toolbar{position:sticky;top:0;z-index:5;display:flex;flex-wrap:wrap;align-items:center;")
          .Append("gap:12px;padding:14px 20px;background:#fff;border-bottom:1px solid #E8E8E8}")
          .Append(".toolbar-title{font-weight:700;font-size:15px;color:#0D0D0D}")
          .Append(".toolbar-sub{font-size:12px;color:#6B6B6B;flex:1}")
          .Append(".toolbar button{border:none;background:")
          .Append(color)
          .Append(";color:#0D0D0D;font-weight:700;padding:8px 16px;border-radius:8px;")
          .Append("cursor:pointer;font-size:13px}")
          .Append(".preview-grid{display:flex;flex-wrap:wrap;gap:28px;padding:28px;justify-content:center}")
          .Append(".preview-pair{display:flex;flex-direction:column;gap:10px;align-items:center}")
          .Append(".preview-pair .card{box-shadow:0 10px 32px rgba(13,13,13,.14)}")
          .Append(".face-tag{font-size:11px;font-weight:700;letter-spacing:.06em;text-transform:uppercase;")
          .Append("color:#6B6B6B}")
          .Append(".preview-more{align-self:center;font-size:13px;color:#6B6B6B;font-weight:600}")
          .Append("</style></head><body>");
    }

    private static void AppendDocumentStart(
        StringBuilder sb, string titleEncoded, string color, bool screenCentered, bool multiPage = false)
    {
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
          .Append("<title>").Append(titleEncoded).Append("</title>")
          .Append("<style>")
          .Append("@page{size:86mm 54mm;margin:0}")
          .Append("@media print{")
          .Append(".no-print{display:none!important}")
          .Append(".print-only{display:flex!important}")
          .Append("html,body{margin:0!important;padding:0!important;background:#fff!important;")
          .Append("-webkit-print-color-adjust:exact!important;print-color-adjust:exact!important}")
          .Append(".sheet{width:86mm!important;height:54mm!important;page-break-after:always;")
          .Append("break-after:page;margin:0!important;padding:2mm!important;")
          .Append("display:flex!important;align-items:center;justify-content:center;")
          .Append("box-shadow:none!important}")
          .Append(".sheet:last-child{page-break-after:auto;break-after:auto}")
          .Append(".card,.scan,.bars,.bc,.mark,.logo,span,img{")
          .Append("-webkit-print-color-adjust:exact!important;print-color-adjust:exact!important}")
          .Append("}")
          .Append("*{box-sizing:border-box;margin:0;padding:0}")
          .Append("html,body{color:#1A1A1A;font-family:'Segoe UI','IBM Plex Sans',Tahoma,sans-serif;")
          .Append("background:#ECEEF2}")
          .Append(".print-only{display:none}")
          .Append(".sheet{width:86mm;height:54mm;display:flex;align-items:center;justify-content:center;padding:2mm}")
          .Append(".card{width:82mm;height:50mm;background:#fff;border:1px solid #E8E8E8;border-radius:3mm;")
          .Append("padding:2.8mm 3.4mm 2.4mm;display:flex;flex-direction:column;overflow:visible}")
          .Append(".hdr{display:flex;align-items:flex-start;gap:2.2mm;flex:0 0 auto}")
          .Append(".mark{width:6.5mm;height:6.5mm;border-radius:1.4mm;background:")
          .Append(color)
          .Append(";flex-shrink:0;-webkit-print-color-adjust:exact;print-color-adjust:exact}")
          .Append(".logo{width:6.5mm;height:6.5mm;border-radius:1.4mm;object-fit:contain;flex-shrink:0;")
          .Append("background:#fff;border:0.2mm solid #E8E8E8}")
          .Append(".brand-col{min-width:0;flex:1}")
          .Append(".product{font-weight:700;font-size:3.2mm;line-height:1.15;color:#0D0D0D}")
          .Append(".gym{font-size:2.5mm;font-weight:600;color:#4A4A4A;margin-top:.5mm;")
          .Append("white-space:nowrap;overflow:hidden;text-overflow:ellipsis}")
          .Append(".gym-ar{font-size:2.3mm;font-weight:600;color:#8C8C8C;direction:rtl;margin-top:.2mm;")
          .Append("white-space:nowrap;overflow:hidden;text-overflow:ellipsis}")
          .Append(".identity{flex:0 0 auto;display:flex;flex-direction:column;align-items:center;")
          .Append("justify-content:center;text-align:center;padding:1.4mm 0 1mm}")
          .Append(".name{font-weight:700;font-size:4.2mm;line-height:1.15;color:#0D0D0D;")
          .Append("max-width:100%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}")
          .Append(".name-ar{font-weight:700;font-size:3.2mm;color:#4A4A4A;direction:rtl;margin-top:.8mm;")
          .Append("max-width:100%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}")
          .Append(".blank-label{font-weight:700;font-size:3.4mm;color:#6B6B6B;letter-spacing:.04em}")
          .Append(".blank-sub{font-size:2.2mm;color:#8C8C8C;margin-top:.6mm}")
          .Append(".blank-sub-ar{direction:rtl;margin-top:.2mm}")
          .Append(".scan{flex:0 0 auto;display:flex;flex-direction:column;align-items:center;gap:1mm;")
          .Append("min-height:16mm}")
          .Append(".bars{width:100%;display:flex;justify-content:center;align-items:center;")
          .Append("overflow:visible;min-height:12mm}")
          .Append(".bc{max-width:74mm;transform-origin:center top}")
          .Append(".num{font-family:Consolas,'Courier New',monospace;font-weight:700;")
          .Append("font-size:3.1mm;letter-spacing:.1em;color:#0D0D0D}")
          .Append(".hint{flex:0 0 auto;margin-top:auto;padding-top:1mm;text-align:center;font-size:2mm;")
          .Append("line-height:1.35;color:#8C8C8C}")
          .Append(".hint-ar{direction:rtl;margin-top:.2mm}")
          .Append(".toolbar{position:sticky;top:0;z-index:5;display:flex;flex-wrap:wrap;align-items:center;")
          .Append("gap:12px;padding:14px 20px;background:#fff;border-bottom:1px solid #E8E8E8}")
          .Append(".toolbar-title{font-weight:700;font-size:15px;color:#0D0D0D}")
          .Append(".toolbar-sub{font-size:12px;color:#6B6B6B;flex:1}")
          .Append(".toolbar button{border:none;background:#7ACC00;color:#0D0D0D;font-weight:700;")
          .Append("padding:8px 16px;border-radius:8px;cursor:pointer;font-size:13px}")
          .Append(".preview-grid{display:flex;flex-wrap:wrap;gap:20px;padding:24px;justify-content:center}")
          .Append(".preview-item .card{box-shadow:0 8px 28px rgba(13,13,13,.12)}")
          .Append(".preview-more{align-self:center;font-size:13px;color:#6B6B6B;font-weight:600}");
        if (screenCentered && !multiPage)
        {
            sb.Append("@media screen{body{min-height:100vh;display:flex;align-items:center;justify-content:center;")
              .Append("padding:24px;background:#ECEEF2}.sheet{width:auto;height:auto;padding:0}")
              .Append(".card{box-shadow:0 8px 28px rgba(13,13,13,.12)}}");
        }
        sb.Append("</style></head><body>");
    }

    private static void AppendCardShellOpen(StringBuilder sb) =>
        sb.Append("<div class=\"card\">");

    private static void AppendCardShellClose(StringBuilder sb) =>
        sb.Append("</div>");

    private static void AppendHeader(
        StringBuilder sb, bool useLogo, string? logoEncoded,
        string gym, string gymAr, bool showGymAr)
    {
        // Member reprint: gym name is the brand (not HyMotion).
        sb.Append("<div class=\"hdr\">");
        if (useLogo)
            sb.Append("<img class=\"logo\" src=\"").Append(logoEncoded).Append("\" alt=\"\">");
        else
            sb.Append("<div class=\"mark\" aria-hidden=\"true\"></div>");
        sb.Append("<div class=\"brand-col\">")
          .Append("<div class=\"product\">").Append(gym).Append("</div>");
        if (showGymAr)
            sb.Append("<div class=\"gym-ar\">").Append(gymAr).Append("</div>");
        sb.Append("</div></div>");
    }

    private static void AppendMemberIdentity(
        StringBuilder sb, string name, string nameAr, bool showNameAr)
    {
        sb.Append("<div class=\"identity\">")
          .Append("<div class=\"name\">").Append(string.IsNullOrEmpty(name) ? "—" : name).Append("</div>");
        if (showNameAr)
            sb.Append("<div class=\"name-ar\">").Append(nameAr).Append("</div>");
        sb.Append("</div>");
    }

    private static void AppendScan(StringBuilder sb, string bars, string numberEncoded)
    {
        sb.Append("<div class=\"scan\">")
          .Append("<div class=\"bars\">").Append(bars).Append("</div>")
          .Append("<div class=\"num\">").Append(numberEncoded).Append("</div>")
          .Append("</div>");
    }

    private static void AppendHint(StringBuilder sb, bool memberHint)
    {
        if (memberHint)
        {
            sb.Append("<div class=\"hint\">Desk check-in · Scan at reception")
              .Append("<div class=\"hint-ar\">للدخول · امسح عند الاستقبال</div></div>");
        }
        else
        {
            sb.Append("<div class=\"hint\">")
              .Append("<div class=\"hint-ar\"></div></div>");
        }
    }

    public static string? NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (!v.StartsWith('#')) v = "#" + v;
        return SafeHex.IsMatch(v) ? v.ToUpperInvariant() : null;
    }

    private static bool IsSafeLogoUrl(string url)
    {
        if (url.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            return !url.Contains("..", StringComparison.Ordinal);
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                   && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
        return false;
    }
}
