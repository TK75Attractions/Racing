using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed class UsbGateway : IDisposable
{
    private const string Cookie = "racing_usb_session";
    private readonly Uri upstream;
    private readonly UsbLink link;
    private readonly string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly HttpClient client;
    internal string PairPath => "/pair?token=" + token;
    public UsbGateway(UsbLink link, Uri upstream)
    {
        if (!upstream.IsLoopback || upstream.Scheme != "http" || upstream.AbsolutePath != "/") throw new ArgumentException("USBの転送先はローカルの管理中継だけです。");
        this.link=link;this.upstream=upstream;
        client=new HttpClient(new SocketsHttpHandler { UseProxy=false, MaxConnectionsPerServer=16 }) { BaseAddress=upstream, Timeout=Timeout.InfiniteTimeSpan };
    }
    internal static WebApplication Builder(IPAddress address, int port)
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions { Args=[] });
        builder.WebHost.ConfigureKestrel(options=>{options.Listen(address,port);options.Limits.MaxRequestBodySize=4096;options.Limits.MaxConcurrentConnections=32;});
        builder.Host.ConfigureHostOptions(options=>options.ShutdownTimeout=TimeSpan.FromSeconds(3));
        builder.Logging.ClearProviders();
        return builder.Build();
    }
    internal WebApplication Build(int port=8767)
    {
        var app=Builder(link.Address,port);
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval=TimeSpan.FromSeconds(10) });
        app.Run(context=>Handle(context,app.Lifetime.ApplicationStopping));
        return app;
    }
    private static bool SecretEquals(string? provided,string expected)
        => provided != null && provided.Length==expected.Length &&
            CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(provided),Encoding.ASCII.GetBytes(expected));
    private async Task Handle(HttpContext context,CancellationToken stopping)
    {
        context.Response.Headers.CacheControl="no-store";
        context.Response.Headers["X-Frame-Options"]="DENY";
        context.Response.Headers["Content-Security-Policy"]="frame-ancestors 'none'";
        context.Response.Headers["X-Content-Type-Options"]="nosniff";
        context.Response.Headers["Referrer-Policy"]="no-referrer";
        string authority=link.Address+":"+context.Connection.LocalPort;
        if (context.Connection.RemoteIpAddress is not IPAddress remoteAddress || !remoteAddress.MapToIPv4().Equals(link.Peer.MapToIPv4()) ||
            context.Request.Host.Value!=authority || context.Request.Headers.ContainsKey("X-Forwarded-For") ||
            context.Request.Headers.ContainsKey("Tailscale-User-Login")) { context.Response.StatusCode=403;return; }
        string expectedOrigin="http://"+authority, origin=context.Request.Headers.Origin.ToString();
        bool mutation=context.Request.Method!="GET" && context.Request.Method!="HEAD";
        if ((mutation || context.WebSockets.IsWebSocketRequest || origin.Length>0) && origin!=expectedOrigin)
        {context.Response.StatusCode=403;return;}
        string path=context.Request.Path.Value??"/";
        if (path=="/pair" && context.Request.Method=="GET" && SecretEquals(context.Request.Query["token"],token))
        {
            context.Response.Cookies.Append(Cookie,token,new CookieOptions { HttpOnly=true, SameSite=SameSiteMode.Strict, Path="/", IsEssential=true });
            context.Response.Redirect("/");return;
        }
        if (!SecretEquals(context.Request.Cookies[Cookie],token))
        {
            context.Response.StatusCode=403;context.Response.ContentType="text/html; charset=utf-8";
            await context.Response.WriteAsync("<!doctype html><html lang=ja><meta name=viewport content='width=device-width,initial-scale=1'><h1>USB接続の確認が必要です</h1><p>ゲームPCのUSB管理画面に表示されたURLから開いてください。</p>");return;
        }
        if (DashboardAssets.IsAsset(path) && !mutation) {await DashboardAssets.Serve(context,path);return;}
        bool websocket=path=="/api/events" && context.WebSockets.IsWebSocketRequest;
        bool read=path=="/api/status" || path=="/api/connection" || path=="/api/push" || path=="/api/operator-update" ||
            path=="/api/player/1/frame" || path=="/api/player/2/frame" || path.StartsWith("/api/command/",StringComparison.Ordinal);
        bool write=path.StartsWith("/api/action/",StringComparison.Ordinal) || path.StartsWith("/api/push/",StringComparison.Ordinal) || path=="/api/operator-update";
        if (!websocket && !(read&&!mutation) && !(write&&context.Request.Method=="POST")) {context.Response.StatusCode=404;return;}
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted,stopping);
        try
        {
            if(websocket){await RelaySocket(context,cancellation);return;}
            cancellation.CancelAfter(TimeSpan.FromSeconds(8));
            using var request=new HttpRequestMessage(new HttpMethod(context.Request.Method),path+context.Request.QueryString);
            request.Headers.TryAddWithoutValidation("Origin",upstream.GetLeftPart(UriPartial.Authority));
            if (mutation)
            {
                using var body=new MemoryStream();await context.Request.Body.CopyToAsync(body,cancellation.Token);
                request.Content=new ByteArrayContent(body.ToArray());request.Content.Headers.ContentType=new("application/json");
            }
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation.Token);
            context.Response.StatusCode=(int)response.StatusCode;
            context.Response.ContentType=response.Content.Headers.ContentType?.ToString();
            if(path=="/api/connection" && response.IsSuccessStatusCode)
            {
                using var document=await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation.Token),cancellationToken:cancellation.Token);
                var info=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(document.RootElement.GetRawText())!;
                info["usb"]=JsonSerializer.SerializeToElement(true);info["transport"]=JsonSerializer.SerializeToElement("usb-websocket");
                await context.Response.WriteAsJsonAsync(info,cancellation.Token);
            }
            else await response.Content.CopyToAsync(context.Response.Body,cancellation.Token);
        }
        catch(BadHttpRequestException error){if(!context.Response.HasStarted)context.Response.StatusCode=error.StatusCode;}
        catch(Exception error) when(error is HttpRequestException or IOException or OperationCanceledException or WebSocketException)
        {
            if(!context.Response.HasStarted){context.Response.StatusCode=503;await context.Response.WriteAsJsonAsync(new{ok=false,reason="relay_unavailable",message="通常のDashboard中継を起動してください。"});}
        }
    }
    private async Task RelaySocket(HttpContext context,CancellationTokenSource cancellation)
    {
        using var upstreamSocket=new ClientWebSocket();upstreamSocket.Options.SetRequestHeader("Origin",upstream.GetLeftPart(UriPartial.Authority));
        var address=new UriBuilder(upstream){Scheme="ws",Path="/api/events",Query=context.Request.QueryString.Value?.TrimStart('?')};
        using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token))
        {deadline.CancelAfter(TimeSpan.FromSeconds(5));await upstreamSocket.ConnectAsync(address.Uri,deadline.Token);}
        using var downstream=await context.WebSockets.AcceptWebSocketAsync();
        var send=CopySocket(upstreamSocket,downstream,cancellation.Token);var receive=CopySocket(downstream,upstreamSocket,cancellation.Token);
        try{await Task.WhenAny(send,receive);}finally{cancellation.Cancel();upstreamSocket.Abort();downstream.Abort();}
        try{await Task.WhenAll(send,receive);}catch(Exception error) when(error is OperationCanceledException or WebSocketException){}
    }
    private static async Task CopySocket(WebSocket source,WebSocket destination,CancellationToken cancellation)
    {
        var buffer=new byte[16384];
        while(!cancellation.IsCancellationRequested)
        {
            var message=await source.ReceiveAsync(buffer,cancellation);
            if(message.MessageType==WebSocketMessageType.Close)return;
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);deadline.CancelAfter(TimeSpan.FromSeconds(3));
            await destination.SendAsync(buffer.AsMemory(0,message.Count),message.MessageType,message.EndOfMessage,deadline.Token);
        }
    }
    public void Dispose()=>client.Dispose();
}
