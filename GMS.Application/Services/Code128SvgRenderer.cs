namespace GMS.Application.Services;

using System.Net;
using System.Text;

/// <summary>
/// CODE128-B encoder for member access cards (MAC-P0 / C2).
/// Emits print-safe HTML bar modules (Chromium print is unreliable with SVG rect barcodes).
/// </summary>
public static class Code128SvgRenderer
{
    // CODE128 patterns index 0–106 (B start=104, stop=106)
    private static readonly string[] Patterns =
    {
        "212222","222122","222221","121223","121322","131222","122213","122312","132212","221213",
        "221312","231212","112232","122132","122231","113222","123122","123221","223211","221132",
        "221231","213212","223112","312131","311222","321122","321221","312212","322112","322211",
        "212123","212321","232121","111323","131123","131321","112313","132113","132311","211313",
        "231113","231311","112133","112331","132131","113123","113321","133121","313121","211331",
        "231131","213113","213311","213131","311123","311321","331121","312113","312311","332111",
        "314111","221411","431111","111224","111422","121124","121421","141122","141221","112214",
        "112412","122114","122411","142112","142211","241211","221114","413111","241112","134111",
        "111242","121142","121241","114212","124112","124211","411212","421112","421211","212141",
        "214121","412121","111143","111341","131141","114113","114311","411113","411311","113141",
        "114131","311141","411131","211412","211214","211232","2331112"
    };

    private const int StartB = 104;
    private const int Stop = 106;

    /// <summary>Legacy SVG (screen). Prefer <see cref="ToHtmlBars"/> for print cards.</summary>
    public static string ToSvg(string data, int barHeight = 56, int module = 2)
    {
        var codes = Encode(data);
        var modules = 0;
        foreach (var c in codes)
            modules += PatternWidth(Patterns[c]);

        var width = modules * module;
        var sb = new StringBuilder(256 + codes.Count * 48);
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"")
          .Append(width)
          .Append("\" height=\"")
          .Append(barHeight)
          .Append("\" viewBox=\"0 0 ")
          .Append(width)
          .Append(' ')
          .Append(barHeight)
          .Append("\" role=\"img\" aria-label=\"")
          .Append(WebUtility.HtmlEncode(data))
          .Append("\">");

        EmitBars(codes, module, (x, w) =>
        {
            sb.Append("<rect x=\"").Append(x)
              .Append("\" y=\"0\" width=\"").Append(w)
              .Append("\" height=\"").Append(barHeight)
              .Append("\" fill=\"#000000\"/>");
        });

        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>
    /// Print-safe inline HTML barcode.
    /// Uses border-left bars (Chrome often drops empty-span backgrounds in Print/PDF).
    /// </summary>
    public static string ToHtmlBars(string data, int modulePx = 2, int heightPx = 44)
    {
        var codes = Encode(data);
        var sb = new StringBuilder(512 + codes.Count * 64);
        sb.Append("<div class=\"bc\" role=\"img\" aria-label=\"")
          .Append(WebUtility.HtmlEncode(data))
          .Append("\" style=\"display:inline-block;font-size:0;line-height:0;white-space:nowrap;")
          .Append("height:")
          .Append(heightPx)
          .Append("px;-webkit-print-color-adjust:exact!important;print-color-adjust:exact!important\">");

        EmitBars(codes, modulePx, (x, w) =>
        {
            // Border bars survive Chromium print; background-only spans often vanish.
            sb.Append("<span style=\"display:inline-block;width:0;height:")
              .Append(heightPx)
              .Append("px;border-left:")
              .Append(w)
              .Append("px solid #000;vertical-align:top\"></span>");
        }, emitWhite: true, whiteEmitter: (w) =>
        {
            sb.Append("<span style=\"display:inline-block;width:")
              .Append(w)
              .Append("px;height:")
              .Append(heightPx)
              .Append("px;vertical-align:top\"></span>");
        });

        sb.Append("</div>");
        return sb.ToString();
    }

    private static List<int> Encode(string data)
    {
        if (string.IsNullOrEmpty(data))
            throw new ArgumentException("Barcode data required.", nameof(data));

        foreach (var ch in data)
        {
            if (ch < 32 || ch > 126)
                throw new ArgumentException("CODE128-B supports printable ASCII only.", nameof(data));
        }

        var codes = new List<int> { StartB };
        var checksum = StartB;
        for (var i = 0; i < data.Length; i++)
        {
            var value = data[i] - 32;
            codes.Add(value);
            checksum += value * (i + 1);
        }
        codes.Add(checksum % 103);
        codes.Add(Stop);
        return codes;
    }

    private static void EmitBars(
        List<int> codes,
        int module,
        Action<int, int> blackEmitter,
        bool emitWhite = false,
        Action<int>? whiteEmitter = null)
    {
        var x = 0;
        foreach (var c in codes)
        {
            var pattern = Patterns[c];
            var black = true;
            foreach (var digit in pattern)
            {
                var w = (digit - '0') * module;
                if (black)
                    blackEmitter(x, w);
                else if (emitWhite && whiteEmitter != null)
                    whiteEmitter(w);
                x += w;
                black = !black;
            }
        }
    }

    private static int PatternWidth(string pattern)
    {
        var sum = 0;
        foreach (var c in pattern)
            sum += c - '0';
        return sum;
    }
}
