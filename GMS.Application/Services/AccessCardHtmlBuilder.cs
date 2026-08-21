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
        bool showGymLogo = true)
    {
        var name = WebUtility.HtmlEncode((member.FullName ?? string.Empty).Trim());
        var nameAr = WebUtility.HtmlEncode((member.FullNameAr ?? string.Empty).Trim());
        var numberRaw = (member.MemberNumber ?? string.Empty).Trim();
        var number = WebUtility.HtmlEncode(numberRaw);
        var gymEn = (gymName ?? string.Empty).Trim();
        var gymArRaw = (gymNameAr ?? string.Empty).Trim();
        var showGymAr = !string.IsNullOrWhiteSpace(gymArRaw)
                        && !string.Equals(gymEn, gymArRaw, StringComparison.OrdinalIgnoreCase);
        var gym = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(gymEn) ? "GymFlowPro Gym" : gymEn);
        var gymAr = WebUtility.HtmlEncode(gymArRaw);
        var showNameAr = !string.IsNullOrWhiteSpace(member.FullNameAr)
                         && !string.Equals(
                             (member.FullName ?? string.Empty).Trim(),
                             (member.FullNameAr ?? string.Empty).Trim(),
                             StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(numberRaw))
            throw new InvalidOperationException("MemberNumber is required to render an access card.");

        var color = NormalizeHex(primaryColor) ?? BrandingDefaults.PrimaryColor;
        var logo = (logoUrl ?? string.Empty).Trim();
        var useLogo = showGymLogo && !string.IsNullOrWhiteSpace(logo) && IsSafeLogoUrl(logo);
        var logoEncoded = useLogo ? WebUtility.HtmlEncode(logo) : null;

        var bars = Code128SvgRenderer.ToHtmlBars(numberRaw, modulePx: 2, heightPx: 46);

        var sb = new StringBuilder(4096);
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
          .Append("<title>").Append(number).Append(" — Member card</title>")
          .Append("<style>")
          .Append("@page{size:86mm 54mm;margin:0}")
          .Append("@media print{")
          .Append("html,body{width:86mm!important;height:54mm!important;margin:0!important;padding:0!important;")
          .Append("background:#fff!important;-webkit-print-color-adjust:exact!important;print-color-adjust:exact!important}")
          .Append(".sheet,.card,.scan,.bars,.bc,.mark,.logo,span,img{")
          .Append("-webkit-print-color-adjust:exact!important;print-color-adjust:exact!important}")
          .Append("}")
          .Append("*{box-sizing:border-box;margin:0;padding:0}")
          .Append("html,body{width:86mm;height:54mm;margin:0;background:#F6F7F9;color:#1A1A1A;")
          .Append("font-family:'Segoe UI','IBM Plex Sans',Tahoma,sans-serif}")
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
          .Append("@media screen{body{min-height:100vh;display:flex;align-items:center;justify-content:center;")
          .Append("padding:24px;background:#ECEEF2}.sheet{width:auto;height:auto;padding:0}")
          .Append(".card{box-shadow:0 8px 28px rgba(13,13,13,.12)}}")
          .Append("</style></head><body><div class=\"sheet\"><div class=\"card\">");

        sb.Append("<div class=\"hdr\">");
        if (useLogo)
            sb.Append("<img class=\"logo\" src=\"").Append(logoEncoded).Append("\" alt=\"\">");
        else
            sb.Append("<div class=\"mark\" aria-hidden=\"true\"></div>");
        sb.Append("<div class=\"brand-col\">")
          .Append("<div class=\"product\">GymFlowPro</div>")
          .Append("<div class=\"gym\">").Append(gym).Append("</div>");
        if (showGymAr)
            sb.Append("<div class=\"gym-ar\">").Append(gymAr).Append("</div>");
        sb.Append("</div></div>");

        sb.Append("<div class=\"identity\">")
          .Append("<div class=\"name\">").Append(string.IsNullOrEmpty(name) ? "—" : name).Append("</div>");
        if (showNameAr)
            sb.Append("<div class=\"name-ar\">").Append(nameAr).Append("</div>");
        sb.Append("</div>");

        sb.Append("<div class=\"scan\">")
          .Append("<div class=\"bars\">").Append(bars).Append("</div>")
          .Append("<div class=\"num\">").Append(number).Append("</div>")
          .Append("</div>");

        sb.Append("<div class=\"hint\">Desk check-in · Scan at reception")
          .Append("<div class=\"hint-ar\">للدخول · امسح عند الاستقبال</div></div>");

        sb.Append("</div></div></body></html>");
        return sb.ToString();
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
