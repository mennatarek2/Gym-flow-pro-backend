namespace HyMotion.Desktop;

internal static class AppFiles
{
    public static string? FindApiExe(params string?[] roots)
    {
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var direct = Path.Combine(root, "GMS.Api.exe");
            if (File.Exists(direct)) return direct;
            var nested = Path.Combine(root, "03-fallback-app", "GMS.Api.exe");
            if (File.Exists(nested)) return nested;
            var app = Path.Combine(root, "app", "GMS.Api.exe");
            if (File.Exists(app)) return app;
        }
        return null;
    }

    public static string? FindScript(string fileName, params string?[] roots)
    {
        var extra = new List<string?>();
        foreach (var root in roots)
        {
            extra.Add(root);
            if (string.IsNullOrWhiteSpace(root)) continue;
            extra.Add(Path.Combine(root, "install-scripts"));
            extra.Add(Path.Combine(root, "install-scripts", "backup"));
            extra.Add(Path.Combine(root, "04-install-scripts"));
            extra.Add(Path.Combine(root, "04-install-scripts", "backup"));
            extra.Add(Path.Combine(root, "backup"));
        }
        foreach (var dir in extra)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var path = Path.Combine(dir, fileName);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public static void CopyApp(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, dir);
            if (ShouldSkip(rel)) continue;
            Directory.CreateDirectory(Path.Combine(destDir, rel));
        }
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            if (ShouldSkip(rel)) continue;
            var dest = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    static bool ShouldSkip(string rel)
    {
        var n = rel.Replace('/', '\\');
        return n.StartsWith("logs\\", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("uploads\\", StringComparison.OrdinalIgnoreCase)
            || n.Equals("logs", StringComparison.OrdinalIgnoreCase)
            || n.Equals("hymotion-data-dir.txt", StringComparison.OrdinalIgnoreCase);
    }
}
