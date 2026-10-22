// The page must remain available when Unity is stopped. Its API connection recovers separately.
internal static class DashboardAssets
{
    private static readonly Dictionary<string, (string File, string Type)> Routes = new(StringComparer.Ordinal)
    {
        ["/"] = ("index.html", "text/html; charset=utf-8"),
        ["/index.html"] = ("index.html", "text/html; charset=utf-8"),
        ["/notifications.js"] = ("notifications.js", "application/javascript; charset=utf-8"),
        ["/sw.js"] = ("sw.js", "application/javascript; charset=utf-8"),
        ["/manifest.webmanifest"] = ("manifest.webmanifest", "application/manifest+json"),
        ["/app-icon.png"] = ("app-icon.png", "image/png"),
        ["/diagnostics.js"] = ("diagnostics.js", "application/javascript; charset=utf-8"),
        ["/app.js"] = ("app.js", "application/javascript; charset=utf-8"),
        ["/live.js"] = ("live.js", "application/javascript; charset=utf-8"),
        ["/remote.js"] = ("remote.js", "application/javascript; charset=utf-8"),
        ["/style.css"] = ("style.css", "text/css; charset=utf-8"),
        ["/favicon.svg"] = ("favicon.svg", "image/svg+xml"),
        ["/firebase-config.json"] = ("firebase-config.json", "application/json; charset=utf-8")
    };
    public static string? FindSourceDirectory()
    {
        for (DirectoryInfo? directory = new(Environment.CurrentDirectory); directory != null; directory = directory.Parent)
        {
            string source = Path.Combine(directory.FullName, "Assets", "StreamingAssets", "Dashboard");
            if (File.Exists(Path.Combine(source, "index.html"))) return source;
        }
        return null;
    }
    public static bool IsAsset(string path) => Routes.ContainsKey(path);
    public static async Task Serve(HttpContext context, string path)
    {
        var asset = Routes[path];
        string? configured = Environment.GetEnvironmentVariable("RACING_DASHBOARD_ASSETS");
        string directory = !string.IsNullOrEmpty(configured) && Path.IsPathFullyQualified(configured) && Directory.Exists(configured)
            ? configured : Path.Combine(AppContext.BaseDirectory, "Dashboard");
        string file = Path.Combine(directory, asset.File);
        context.Response.Headers.CacheControl = "no-store";
        if (!File.Exists(file))
        {
            context.Response.StatusCode = 503;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync("<!doctype html><html lang=ja><meta charset=utf-8>" +
                "<meta name=viewport content='width=device-width,initial-scale=1'><title>Dashboardの準備が必要です</title>" +
                "<h1>Dashboardの準備が必要です</h1><p>ゲームPCでDashboardの起動または常駐設定をやり直してください。</p></html>");
            return;
        }
        context.Response.ContentType = asset.Type;
        context.Response.ContentLength = new FileInfo(file).Length;
        if (!HttpMethods.IsHead(context.Request.Method)) await context.Response.SendFileAsync(file);
    }
}
