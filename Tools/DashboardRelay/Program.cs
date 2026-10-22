using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

if (args.Contains("--update-service")) { await MacDashboardService.UpdateInstalled(); return; }

if (args.Contains("--install-service") || args.Contains("--uninstall-service"))
{
    await MacDashboardService.Configure(args.Contains("--install-service"));
    return;
}

bool serviceMode = args.Contains("--service");
bool automatic = args.Contains("--auto") || serviceMode;
bool openBrowser = automatic && !args.Contains("--no-browser");
bool managed = args.Contains("--managed");
using var managedStop = new CancellationTokenSource();
if (managed) _ = Task.Run(async () => { await Console.In.ReadLineAsync(); managedStop.Cancel(); });
string? startNotice = null;
if (automatic)
{
    try
    {
        using var probe = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(1) };
        using var existing = JsonDocument.Parse(await probe.GetStringAsync("http://127.0.0.1:8766/api/connection"));
        if (existing.RootElement.TryGetProperty("app", out var name) && name.GetString() == "racing-dashboard")
        {
            string? remote = existing.RootElement.TryGetProperty("remoteUrl", out var value) ? value.GetString() : null;
            Console.WriteLine("RACING_DASHBOARD_READY " + JsonSerializer.Serialize(new { localUrl = "http://127.0.0.1:8766/", remoteUrl = remote, reused = true }));
            if (openBrowser) QuickStart.Open("http://127.0.0.1:8766/");
            return;
        }
    }
    catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException) { }
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RACING_DASHBOARD_ORIGIN")))
    {
        try
        {
            var settings = await QuickStart.Configure(managedStop.Token);
            Environment.SetEnvironmentVariable("RACING_DASHBOARD_ORIGIN", settings.Origin);
            if (settings.Origin != null) Environment.SetEnvironmentVariable("RACING_DASHBOARD_OPERATORS", string.Join(",", settings.Operators));
            startNotice = settings.Notice;
        }
        catch (OperationCanceledException) when (managedStop.IsCancellationRequested) { return; }
        catch (Exception error) { startNotice = "Tailscaleの設定を確認できませんでした。ローカル接続で起動します。（" + error.GetType().Name + "）"; }
    }
}
if (managedStop.IsCancellationRequested) return;
// At login Tailscale may start after the agent. launchd retries until the private route is ready.
if (serviceMode && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RACING_DASHBOARD_ORIGIN")))
{
    Console.WriteLine(startNotice ?? "Tailscaleの接続を待っています。");
    await Task.Delay(5000);
    return;
}
args = args.Where(arg => arg != "--auto" && arg != "--no-browser" && arg != "--managed" && arg != "--service").ToArray();

// Only Serve may expose this loopback service. Do not bind to 0.0.0.0.
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:8766");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 4096);
builder.Logging.ClearProviders();
var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(10) });
using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, MaxConnectionsPerServer = 32 })
{ BaseAddress = new Uri("http://127.0.0.1:8765"), Timeout = Timeout.InfiniteTimeSpan };
string? allowedOrigin = Environment.GetEnvironmentVariable("RACING_DASHBOARD_ORIGIN")?.TrimEnd('/');
var operators = (Environment.GetEnvironmentVariable("RACING_DASHBOARD_OPERATORS") ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
int activeSockets = 0;
using var push = new DashboardPush(Environment.GetEnvironmentVariable("RACING_PUSH_DATA") ??
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RacingDashboard", "Push"), allowedOrigin);
Task? pushMonitor = null;
var pushTests = new System.Collections.Concurrent.ConcurrentDictionary<string, long>();
Console.WriteLine("Dashboard relay: http://127.0.0.1:8766 (Tailscale Serve target)");
if (allowedOrigin == null) Console.WriteLine("Local-only mode. Set RACING_DASHBOARD_ORIGIN to the exact Serve HTTPS origin to allow remote access.");
app.Run(async context =>
{
    context.Response.ContentType = "text/plain; charset=utf-8";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    bool loopbackHost = context.Request.Host.Value == "127.0.0.1:8766" || context.Request.Host.Value == "localhost:8766";
    string identity = context.Request.Headers["Tailscale-User-Login"].ToString();
    bool proxyTraffic = identity.Length > 0 || context.Request.Headers.ContainsKey("X-Forwarded-For");
    bool local = loopbackHost && !proxyTraffic;
    // Accept Serve's preserved public Host or a rewritten loopback Host, while always
    // treating an identity-bearing request as remote (including for authorization).
    bool remote = allowedOrigin != null && Uri.TryCreate(allowedOrigin, UriKind.Absolute, out var publicUri) &&
        publicUri.Scheme == "https" && (loopbackHost || string.Equals(context.Request.Host.Value, publicUri.Authority, StringComparison.OrdinalIgnoreCase));
    if ((!local && !remote) || (!local && string.IsNullOrEmpty(identity))) { context.Response.StatusCode = 403; return; }
    remote = !local;
    string expectedOrigin = remote ? allowedOrigin! : "http://" + context.Request.Host.Value;
    string origin = context.Request.Headers.Origin.ToString();
    bool mutation = context.Request.Method != "GET" && context.Request.Method != "HEAD";
    if ((mutation || context.WebSockets.IsWebSocketRequest || origin.Length > 0) && origin != expectedOrigin)
    { context.Response.StatusCode = 403; return; }
    // Wildcard includes shared users admitted by Tailscale. Remote identity and
    // Host/Origin checks above still apply before granting any control access.
    bool canControl = local || operators.Contains("*") || operators.Contains(identity);
    string path = context.Request.Path.Value ?? "/";
    if (path == "/api/push" || path.StartsWith("/api/push/", StringComparison.Ordinal))
    {
        context.Response.Headers.CacheControl = "no-store";
        string owner = local ? "local" : identity;
        if (path == "/api/push" && !mutation) { await context.Response.WriteAsJsonAsync(push.Status(owner,canControl)); return; }
        if (!canControl || context.Request.Method != "POST") { context.Response.StatusCode = 403; return; }
        if (!push.Ready) { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { ok=false,reason="https_required" }); return; }
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body,cancellationToken:context.RequestAborted);
            string endpoint = HardwareAlertMonitor.Text(document.RootElement,"endpoint");
            bool ok = false;
            if (path == "/api/push/subscribe")
            {
                var keys = HardwareAlertMonitor.Object(document.RootElement,"keys");
                ok = push.Subscribe(owner,endpoint,new Dictionary<string,string> { ["p256dh"] = HardwareAlertMonitor.Text(keys,"p256dh"), ["auth"] = HardwareAlertMonitor.Text(keys,"auth") });
            }
            else if (path == "/api/push/unsubscribe") ok = push.Remove(owner,endpoint);
            else if (path == "/api/push/test")
            {
                long now = Environment.TickCount64;
                if (pushTests.TryGetValue(owner,out long previous) && now-previous<60_000) { context.Response.StatusCode=429; return; }
                pushTests[owner]=now;
                await push.Send("通知テストです。この端末でレース機器の異常通知を受信できます。",context.RequestAborted,owner);ok=true;
            }
            context.Response.StatusCode=ok?200:400;
            await context.Response.WriteAsJsonAsync(new{ok});
        }
        catch (Exception error) when (error is JsonException or BadHttpRequestException or IOException or UnauthorizedAccessException)
        { if (!context.Response.HasStarted) context.Response.StatusCode=400; }
        return;
    }
    if (path == "/api/connection")
    {
        await context.Response.WriteAsJsonAsync(new { app = "racing-dashboard", transport = "websocket", canControl, user = remote ? identity : "local", remote,
            remoteUrl = allowedOrigin == null ? null : allowedOrigin + "/" }); return;
    }
    if (path == "/api/events" && context.WebSockets.IsWebSocketRequest)
    {
        if (Interlocked.Increment(ref activeSockets) > 8) { Interlocked.Decrement(ref activeSockets); context.Response.StatusCode = 429; return; }
        try
        {
            using var upstreamCancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, app.Lifetime.ApplicationStopping);
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var receive = ReceiveUntilClose(socket, upstreamCancellation);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/api/events" + context.Request.QueryString);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, upstreamCancellation.Token);
                response.EnsureSuccessStatusCode();
                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(upstreamCancellation.Token), Encoding.UTF8);
                while (!upstreamCancellation.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(upstreamCancellation.Token);
                    if (line == null) break;
                    if (line.Length == 0) continue;
                    using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(upstreamCancellation.Token);
                    sendTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                    await socket.SendAsync(Encoding.UTF8.GetBytes(line).AsMemory(), WebSocketMessageType.Text, true, sendTimeout.Token);
                }
            }
            catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or WebSocketException) { }
            finally { upstreamCancellation.Cancel(); socket.Abort(); try { await receive; } catch (WebSocketException) { } }
        }
        finally { Interlocked.Decrement(ref activeSockets); }
        return;
    }
    bool command = path.StartsWith("/api/action/", StringComparison.Ordinal);
    if (mutation && (!command || context.Request.Method != "POST" || !canControl)) { context.Response.StatusCode = 403; return; }
    // Explicit routes prevent using the relay as a general HTTP proxy.
    bool asset = DashboardAssets.IsAsset(path);
    bool read = path == "/api/status" || path == "/api/player/1/frame" || path == "/api/player/2/frame" || path.StartsWith("/api/command/", StringComparison.Ordinal);
    if (!command && !asset && !read) { context.Response.StatusCode = 404; return; }
    if (command && !mutation) { context.Response.StatusCode = 405; return; }
    if (asset) { await DashboardAssets.Serve(context, path); return; }
    try
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, app.Lifetime.ApplicationStopping);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), path);
        // Authorization happened above. Unity only accepts its own loopback origin.
        request.Headers.TryAddWithoutValidation("Origin", "http://127.0.0.1:8765");
        if (mutation)
        {
            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body, timeout.Token);
            request.Content = new ByteArrayContent(body.ToArray());
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        }
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        context.Response.StatusCode = (int)response.StatusCode;
        context.Response.ContentType = response.Content.Headers.ContentType?.ToString();
        context.Response.Headers.CacheControl = "no-store";
        await response.Content.CopyToAsync(context.Response.Body, timeout.Token);
    }
    catch (BadHttpRequestException error) { if (!context.Response.HasStarted) context.Response.StatusCode = error.StatusCode; }
    catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException)
    {
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = 503;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new { ok = false, reason = "unity_unavailable",
                message = "Unityに接続できません。ゲームPCでUnityをPlayしてください。" });
        }
    }
});
using var managedRegistration = managedStop.Token.Register(app.Lifetime.StopApplication);
await app.StartAsync();
pushMonitor = push.Monitor(client,app.Lifetime.ApplicationStopping,owner=>owner=="local"||operators.Contains("*")||operators.Contains(owner));
Console.WriteLine("RACING_DASHBOARD_READY " + JsonSerializer.Serialize(new {
    localUrl = "http://127.0.0.1:8766/", remoteUrl = allowedOrigin == null ? null : allowedOrigin + "/", notice = startNotice, reused = false }));
Console.WriteLine(startNotice ?? "WebSocketの準備ができました。UnityをPlayしてダッシュボードを開いてください。");
if (openBrowser) QuickStart.Open("http://127.0.0.1:8766/");
await app.WaitForShutdownAsync();
try { await pushMonitor; } catch (OperationCanceledException) { }

static async Task ReceiveUntilClose(WebSocket socket, CancellationTokenSource cancellation)
{
    try
    {
        var buffer = new byte[256];
        while (!cancellation.IsCancellationRequested)
        {
            var message = await socket.ReceiveAsync(buffer.AsMemory(), cancellation.Token);
            // This channel is server-to-browser only. HTTP carries authorized commands.
            if (message.MessageType == WebSocketMessageType.Close || message.Count > 0) break;
        }
    }
    catch (Exception error) when (error is OperationCanceledException or WebSocketException) { }
    finally { cancellation.Cancel(); }
}
