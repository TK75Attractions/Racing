using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

// One-time Tailscale login/HTTPS consent stays with the user. No credentials are stored.
internal static class QuickStart
{
    internal sealed record Settings(string? Origin, string[] Operators, string? Notice);
    internal static Settings ParseStatus(string json, string? configuredOperators)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("BackendState", out var state) || state.GetString() != "Running")
            return new(null, [], "Tailscaleにログインして接続してください。ローカルのWebSocketは利用できます。");
        if (!root.TryGetProperty("Self", out var self) || !self.TryGetProperty("DNSName", out var dns))
            return new(null, [], "TailscaleのPC名を取得できませんでした。");
        string name = (dns.GetString() ?? "").TrimEnd('.');
        if (!Regex.IsMatch(name, @"\A[a-zA-Z0-9-]+\.[a-zA-Z0-9.-]+\.ts\.net\z"))
            return new(null, [], "TailscaleのHTTPS用DNS名を確認できませんでした。");
        var operators = (configuredOperators ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (configuredOperators == null && self.TryGetProperty("UserID", out var id) &&
            root.TryGetProperty("User", out var users) && users.TryGetProperty(id.ToString(), out var user) &&
            user.TryGetProperty("LoginName", out var login) && !string.IsNullOrEmpty(login.GetString()) &&
            (!self.TryGetProperty("Tags", out var tags) || tags.ValueKind != JsonValueKind.Array || tags.GetArrayLength() == 0))
            operators = [login.GetString()!];
        return new("https://" + name, operators, operators.Length == 0 ? "操作担当者は未設定です。遠隔端末は閲覧専用になります。" : null);
    }

    internal static bool CanConfigureServe(string json, string origin, out string? reason)
    {
        reason = null;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Null) return true;
        string endpoint = new Uri(origin).Host + ":443";
        if (root.TryGetProperty("AllowFunnel", out var funnel) && funnel.ValueKind == JsonValueKind.Object &&
            funnel.EnumerateObject().Any(property => property.Value.ValueKind == JsonValueKind.True))
        { reason = "Funnelの公開設定があります。既存の設定を確認するまで遠隔接続の自動設定を停止します。"; return false; }
        if (root.TryGetProperty("TCP", out var tcp) && tcp.TryGetProperty("443", out var port) &&
            (!port.TryGetProperty("HTTPS", out var https) || https.ValueKind != JsonValueKind.True))
        { reason = "443番ポートを別のTailscaleサービスが使用しています。既存設定は変更しません。"; return false; }
        if (root.TryGetProperty("Web", out var web) && web.ValueKind == JsonValueKind.Object)
            foreach (var site in web.EnumerateObject())
                if (site.Name.EndsWith(":443", StringComparison.Ordinal) && site.Value.TryGetProperty("Handlers", out var handlers) &&
                    handlers.TryGetProperty("/", out var handler))
                {
                    if (site.Name != endpoint || !handler.TryGetProperty("Proxy", out var proxy) ||
                        proxy.GetString()?.TrimEnd('/') != "http://127.0.0.1:8766")
                    { reason = "TailscaleのHTTPS入口を別のサービスが使用しています。既存設定は変更しません。"; return false; }
                }
        return true;
    }

    internal static string? FindTailscale()
    {
        string? configured = Environment.GetEnvironmentVariable("RACING_TAILSCALE_CLI");
        if (!string.IsNullOrEmpty(configured)) return File.Exists(configured) ? configured : null;
        string[] candidates = OperatingSystem.IsWindows() ?
            [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe")] :
            ["/Applications/Tailscale.app/Contents/MacOS/Tailscale", "/opt/homebrew/bin/tailscale", "/usr/local/bin/tailscale", "/usr/bin/tailscale"];
        return candidates.Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => Path.Combine(path, OperatingSystem.IsWindows() ? "tailscale.exe" : "tailscale")))
            .FirstOrDefault(File.Exists);
    }

    internal static async Task<(int Exit, string Output, string Error)> Run(string executable, CancellationToken cancellation, params string[] arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } await process.WaitForExitAsync(); }
        cancellation.ThrowIfCancellationRequested();
        return (process.ExitCode, await output, await error);
    }

    internal static async Task<Settings> Configure(CancellationToken cancellation)
    {
        string? executable = FindTailscale();
        if (executable == null) return new(null, [], "Tailscaleが見つかりません。導入・ログイン後に再起動すると遠隔接続できます。現在はローカルWebSocketで起動します。");
        var status = await Run(executable, cancellation, "status", "--json");
        if (status.Exit != 0) return new(null, [], "Tailscaleを起動してログインしてください。現在はローカルWebSocketで起動します。");
        var settings = ParseStatus(status.Output, Environment.GetEnvironmentVariable("RACING_DASHBOARD_OPERATORS"));
        if (settings.Origin == null) return settings;
        var existing = await Run(executable, cancellation, "serve", "status", "--json");
        if (existing.Exit != 0 || !CanConfigureServe(existing.Output, settings.Origin, out _))
        {
            string? reason = null;
            if (existing.Exit == 0) CanConfigureServe(existing.Output, settings.Origin, out reason);
            return new(null, [], reason ?? "Tailscaleの既存公開設定を取得できないため、自動設定を停止しました。ローカル接続を利用してください。");
        }
        if (HasRoute(existing.Output, settings.Origin)) return settings;
        Console.WriteLine("Tailscaleの遠隔接続を準備しています…（初回はHTTPSの有効化が必要です）");
        var serve = await Run(executable, cancellation, "serve", "--bg", "--yes", "--https=443", "http://127.0.0.1:8766");
        if (serve.Exit != 0)
        {
            // Only show a genuine Tailscale account-consent URL; never launch arbitrary output.
            var consent = Regex.Match(serve.Output + serve.Error, @"https://(?:login|control)\.tailscale\.com/[a-zA-Z0-9/_?=&%.\-]+");
            string note = "TailscaleのHTTPS設定を完了してから、もう一度開始してください。";
            if (consent.Success) note += "\n" + consent.Value;
            return new(null, [], note + "\n現在はローカルWebSocketで起動します。");
        }
        // Verify the resulting route before advertising remote readiness.
        var verified = await Run(executable, cancellation, "serve", "status", "--json");
        if (verified.Exit != 0 || !HasRoute(verified.Output, settings.Origin))
            return new(null, [], "Tailscaleの接続先を確認できませんでした。ローカルWebSocketで起動します。");
        return settings;
    }

    internal static bool HasRoute(string json, string origin)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("TCP", out var tcp) && tcp.TryGetProperty("443", out var port) &&
            port.TryGetProperty("HTTPS", out var https) && https.ValueKind == JsonValueKind.True &&
            document.RootElement.TryGetProperty("Web", out var web) &&
            web.TryGetProperty(new Uri(origin).Host + ":443", out var site) &&
            site.TryGetProperty("Handlers", out var handlers) && handlers.TryGetProperty("/", out var handler) &&
            handler.TryGetProperty("Proxy", out var proxy) && proxy.GetString()?.TrimEnd('/') == "http://127.0.0.1:8766" &&
            CanConfigureServe(json, origin, out _);
    }

    internal static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception error) { Console.WriteLine("ブラウザーで開いてください: " + url + " (" + error.GetType().Name + ")"); }
    }
}
