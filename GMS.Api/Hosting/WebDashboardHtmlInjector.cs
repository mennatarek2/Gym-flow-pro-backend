using System.Text.RegularExpressions;

namespace GMS.Api.Hosting;

/// <summary>Injects shared shell assets into dashboard/auth HTML (mirrors apps/web/server.js sendHtml).</summary>
internal static partial class WebDashboardHtmlInjector
{
    private static readonly string[] SharedScripts =
    [
        "/shared/api-config.js",
        "/shared/api-client.js",
        "/shared/authz.js",
        "/shared/features.js?v=5",
        "/shared/i18n.js",
        "/shared/theme.js?v=1",
        "/shared/nav.js?v=4",
        "/shared/inventory-api.js",
        "/shared/member-orders-api.js",
        "/shared/gfp-branding.js?v=5",
        "/shared/shell.js?v=theme1",
        "/shared/quick-actions.js?v=5",
        "/shared/refund-action.js?v=1"
    ];

    private static readonly string[] SharedStyles =
    [
        "/shared/rtl.css",
        "/shared/typography.css?v=1",
        "/shared/refund-action.css",
        "/shared/theme.css?v=2"
    ];

    private const string ThemeBootStyle =
        "<style data-gfp-theme-boot>html[data-theme=\"dark\"]{color-scheme:dark;background:#151716;--lbg:#151716;--ls1:#1C201D;--ls2:#222722;--ls3:#2E342F;--ltp:#E8EBE6;--lts:#B5BBB4;--ltt:#8C948A;--suc100:#16351F;--dng100:#3A1C1C;--wrn100:#3A2E12;--inf100:#1A2A44;--l100:rgba(122,204,0,.16);--sh1:0 1px 2px rgba(0,0,0,.28)}html[data-theme=\"dark\"] body{background:#151716;color:#E8EBE6}</style>";

    public static string Inject(string html)
    {
        var missingCss = SharedStyles.Where(h => !HasStylesheetHref(html, h)).ToList();
        var missingJs = SharedScripts.Where(s => !HasScriptSrc(html, s)).ToList();
        var needEarly = !html.Contains("data-gfp-early-locale", StringComparison.Ordinal);
        var needThemeBoot = !html.Contains("data-gfp-theme-boot", StringComparison.Ordinal);

        var headStart = new List<string>();
        if (needThemeBoot)
            headStart.Add(ThemeBootStyle);
        if (needEarly)
            headStart.Add(EarlyLocaleScript);

        var headEnd = new List<string>();
        headEnd.AddRange(missingCss.Select(h => $"<link rel=\"stylesheet\" href=\"{h}\">"));
        headEnd.AddRange(missingJs.Select(s => $"<script src=\"{s}\"></script>"));

        if (headStart.Count > 0 && HeadOpenRegex().IsMatch(html))
            html = HeadOpenRegex().Replace(html, m => m.Value + "\n" + string.Join('\n', headStart) + "\n");
        else if (headStart.Count > 0)
            html = string.Join('\n', headStart) + "\n" + html;

        if (headEnd.Count > 0)
        {
            var inject = string.Join('\n', headEnd) + "\n";
            if (HeadCloseRegex().IsMatch(html))
                html = HeadCloseRegex().Replace(html, inject + "</head>");
            else if (HeadOpenRegex().IsMatch(html))
                html = HeadOpenRegex().Replace(html, m => m.Value + "\n" + inject);
            else
                html = inject + html;
        }

        return html;
    }

    private static bool HasScriptSrc(string html, string src)
    {
        var esc = Regex.Escape(src.Split('?')[0]);
        return Regex.IsMatch(html, $"""<script[^>]+src=["']{esc}[^"']*["']""", RegexOptions.IgnoreCase);
    }

    private static bool HasStylesheetHref(string html, string href)
    {
        var esc = Regex.Escape(href.Split('?')[0]);
        return Regex.IsMatch(html, $"""<link[^>]+href=["']{esc}[^"']*["']""", RegexOptions.IgnoreCase);
    }

    private static readonly string EarlyLocaleScript =
        """<script data-gfp-early-locale>(function(){try{var h=document.documentElement;var l=localStorage.getItem("gfp_locale");if(l!=="ar"&&l!=="en")l="en";h.lang=l;h.dir=l==="ar"?"rtl":"ltr";var pref=localStorage.getItem("gfp_appearance");try{var u0=JSON.parse(localStorage.getItem("gfp_user")||"null");var uid=u0&&(u0.id||u0.Id||u0.userId||u0.UserId);if(uid){var p2=localStorage.getItem("gfp_appearance:"+uid);if(p2==="light"||p2==="dark"||p2==="system")pref=p2;}}catch(e0){}if(pref!=="light"&&pref!=="dark"&&pref!=="system")pref="light";var dark=pref==="dark"||(pref==="system"&&window.matchMedia&&matchMedia("(prefers-color-scheme: dark)").matches);h.setAttribute("data-theme",dark?"dark":"light");h.setAttribute("data-appearance",pref);h.style.colorScheme=dark?"dark":"light";var u=JSON.parse(localStorage.getItem("gfp_user")||"null");var tid=u&&(u.tenantId||u.TenantId);var raw=tid&&localStorage.getItem("gfp_branding:"+tid);if(!raw)raw=localStorage.getItem("gfp_branding");if(!raw)return;var b=JSON.parse(raw);var p=b.primaryColor||b.PrimaryColor||"#7ACC00";var a=b.accentColor||b.AccentColor||"#A0E040";h.style.setProperty("--gfp-brand-primary",p);h.style.setProperty("--gfp-brand-accent",a);h.style.setProperty("--l500",p);h.style.setProperty("--l400",a);h.style.setProperty("--l600",p);h.style.setProperty("--l300",a);}catch(e){}})();</script>""";

    [GeneratedRegex(@"<head[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HeadOpenRegex();

    [GeneratedRegex(@"</head>", RegexOptions.IgnoreCase)]
    private static partial Regex HeadCloseRegex();
}
