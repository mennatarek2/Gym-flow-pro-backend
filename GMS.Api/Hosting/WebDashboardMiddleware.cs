namespace GMS.Api.Hosting;

/// <summary>Serves staff dashboard + auth static HTML from wwwroot (production / MonsterASP single-site deploy).</summary>
public class WebDashboardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _dashboardRoot;
    private readonly string _memberRoot;
    private readonly string _authRoot;
    private readonly string _sharedRoot;

    public WebDashboardMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        _next = next;
        var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        _dashboardRoot = Path.Combine(webRoot, "dashboard");
        _memberRoot = Path.Combine(webRoot, "member");
        _authRoot = Path.Combine(webRoot, "auth");
        _sharedRoot = Path.Combine(webRoot, "shared");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "/";
        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/hubs", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/hangfire", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (path.StartsWith("/shared/", StringComparison.OrdinalIgnoreCase))
        {
            var file = Path.GetFileName(path);
            var sharedFile = Path.Combine(_sharedRoot, file);
            if (File.Exists(sharedFile))
            {
                await SendFileAsync(context, sharedFile, injectHtml: false);
                return;
            }
        }

        if (path == "/")
        {
            context.Response.Redirect("/dashboard/", permanent: false);
            return;
        }

        if (path.StartsWith("/auth", StringComparison.OrdinalIgnoreCase))
        {
            if (await TryServeAuthAsync(context, path))
                return;
        }

        if (path.StartsWith("/dashboard", StringComparison.OrdinalIgnoreCase))
        {
            if (await TryServeRootedAsync(context, path, "/dashboard", _dashboardRoot, memberScope: false))
                return;
        }

        if (path.StartsWith("/member", StringComparison.OrdinalIgnoreCase))
        {
            if (await TryServeRootedAsync(context, path, "/member", _memberRoot, memberScope: true))
                return;
        }

        await _next(context);
    }

    private async Task<bool> TryServeAuthAsync(HttpContext context, string url)
    {
        if (url is "/auth/login" or "/auth/login/")
            return await TrySendHtmlAsync(context, Path.Combine(_authRoot, "login", "index.html"), memberScope: false);

        if (!url.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase))
            return false;

        var rel = url[6..].TrimEnd('/');
        var exact = Path.Combine(_authRoot, rel.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(exact))
        {
            await SendFileAsync(context, exact, injectHtml: exact.EndsWith(".html", StringComparison.OrdinalIgnoreCase));
            return true;
        }

        var idx = Path.Combine(_authRoot, rel.Replace('/', Path.DirectorySeparatorChar), "index.html");
        if (!File.Exists(idx))
            return false;

        if (!url.EndsWith('/'))
        {
            var q = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : "";
            context.Response.Redirect($"/auth/{rel}/{q}", permanent: true);
            return true;
        }

        return await TrySendHtmlAsync(context, idx, memberScope: false);
    }

    /// <summary>Shared static/HTML resolution for both the staff dashboard root and the member root.</summary>
    private async Task<bool> TryServeRootedAsync(HttpContext context, string url, string prefix, string root, bool memberScope)
    {
        var sub = url.Replace(prefix, "", StringComparison.OrdinalIgnoreCase).Trim('/');

        if (string.IsNullOrEmpty(sub))
        {
            if (url == prefix)
            {
                context.Response.Redirect($"{prefix}/", permanent: true);
                return true;
            }

            return await TrySendHtmlAsync(context, Path.Combine(root, "index.html"), memberScope);
        }

        var exact = Path.Combine(root, sub.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(exact))
        {
            await SendFileAsync(context, exact, injectHtml: exact.EndsWith(".html", StringComparison.OrdinalIgnoreCase), memberScope);
            return true;
        }

        var idx = Path.Combine(root, sub.Replace('/', Path.DirectorySeparatorChar), "index.html");
        if (File.Exists(idx))
        {
            if (!url.EndsWith('/'))
            {
                var q = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : "";
                context.Response.Redirect($"{prefix}/{sub}/{q}", permanent: true);
                return true;
            }

            return await TrySendHtmlAsync(context, idx, memberScope);
        }

        var parts = sub.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            var parent = string.Join(Path.DirectorySeparatorChar, parts[..^1]);
            var dynIdx = Path.Combine(root, parent, "[id]", "index.html");
            if (File.Exists(dynIdx))
                return await TrySendHtmlAsync(context, dynIdx, memberScope);

            var file = parts[^1];
            var dynAsset = Path.Combine(root, parent, "[id]", file);
            if (File.Exists(dynAsset))
            {
                await SendFileAsync(context, dynAsset, injectHtml: false, memberScope);
                return true;
            }
        }

        if (parts.Length >= 3)
        {
            var gp = string.Join(Path.DirectorySeparatorChar, parts[..^2]);
            var file = parts[^1];
            var dynAsset = Path.Combine(root, gp, "[id]", file);
            if (File.Exists(dynAsset))
            {
                await SendFileAsync(context, dynAsset, injectHtml: false, memberScope);
                return true;
            }
        }

        return false;
    }

    private async Task<bool> TrySendHtmlAsync(HttpContext context, string filePath, bool memberScope)
    {
        if (!File.Exists(filePath))
            return false;

        await SendFileAsync(context, filePath, injectHtml: true, memberScope);
        return true;
    }

    private async Task SendFileAsync(HttpContext context, string filePath, bool injectHtml, bool memberScope = false)
    {
        if (injectHtml)
        {
            var html = await File.ReadAllTextAsync(filePath);
            html = WebDashboardHtmlInjector.Inject(html, memberScope);
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(html);
            return;
        }

        var contentType = GetContentType(filePath);
        context.Response.ContentType = contentType;
        await context.Response.SendFileAsync(filePath);
    }

    private static string GetContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".svg" => "image/svg+xml",
            ".woff2" => "font/woff2",
            _ => "application/octet-stream"
        };
    }
}
