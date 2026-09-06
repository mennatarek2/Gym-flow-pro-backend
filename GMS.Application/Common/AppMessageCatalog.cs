namespace GMS.Application.Common;

using System.Collections.Concurrent;
using System.Text.Json;

/// <summary>
/// Loads EN/AR error messages from Localization/AppMessages.*.json (code → text).
/// </summary>
public static class AppMessageCatalog
{
    private static readonly ConcurrentDictionary<string, string> En = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> Ar = new(StringComparer.OrdinalIgnoreCase);
    private static int _loaded;

    public static void EnsureLoaded()
    {
        if (Interlocked.CompareExchange(ref _loaded, 1, 0) != 0) return;
        TryLoad("en", En);
        TryLoad("ar", Ar);
    }

    private static void TryLoad(string locale, ConcurrentDictionary<string, string> target)
    {
        try
        {
            var asm = typeof(AppMessageCatalog).Assembly;
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith($"AppMessages.{locale}.json", StringComparison.OrdinalIgnoreCase));
            Stream? stream = null;
            if (name != null)
                stream = asm.GetManifestResourceStream(name);
            if (stream == null)
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Localization", $"AppMessages.{locale}.json");
                if (File.Exists(path))
                    stream = File.OpenRead(path);
            }
            if (stream == null)
            {
                // Fallback: project content path relative to assembly
                var dir = Path.GetDirectoryName(asm.Location);
                if (dir != null)
                {
                    var alt = Path.Combine(dir, "Localization", $"AppMessages.{locale}.json");
                    if (File.Exists(alt)) stream = File.OpenRead(alt);
                }
            }
            if (stream == null) return;
            using (stream)
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
                if (dict == null) return;
                foreach (var kv in dict)
                    target[kv.Key] = kv.Value;
            }
        }
        catch
        {
            // Catalog is best-effort; callers still pass explicit strings.
        }
    }

    public static AppError Get(string code, string? fallbackEn = null, string? fallbackAr = null)
    {
        EnsureLoaded();
        En.TryGetValue(code, out var en);
        Ar.TryGetValue(code, out var ar);
        en ??= fallbackEn ?? code;
        ar ??= fallbackAr ?? en;
        return new AppError(code, en, ar);
    }

    public static void RegisterForTests(string code, string en, string ar)
    {
        EnsureLoaded();
        En[code] = en;
        Ar[code] = ar;
    }
}
