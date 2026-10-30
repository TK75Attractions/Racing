using System.Net;
using System.Text.Json;

// This helper owns only port 8767. It never starts/stops Unity, Race, Tailscale or the normal relay.
string requestedMode;
try{requestedMode=UsbMode.Requested(args);}
catch(ArgumentException error){Console.Error.WriteLine(error.Message);Environment.ExitCode=2;return;}
using(var probe=new HttpClient(new SocketsHttpHandler{UseProxy=false}){Timeout=TimeSpan.FromMilliseconds(750)})
{
    try
    {
        using var info=JsonDocument.Parse(await probe.GetStringAsync("http://127.0.0.1:8767/api/usb"));
        if(info.RootElement.TryGetProperty("app",out var name)&&name.ValueKind==JsonValueKind.String&&name.GetString()=="racing-usb-dashboard")
        {
            if(!UsbMode.CanReuse(info.RootElement,requestedMode)){Console.Error.WriteLine(UsbMode.Conflict(info.RootElement,requestedMode));Environment.ExitCode=2;return;}
            Console.WriteLine("同じ接続方式の補助アプリは起動済みです: http://127.0.0.1:8767/");OpenSetup();return;
        }
    }
    catch(Exception error)when(error is HttpRequestException or TaskCanceledException or JsonException){}
}
var builder=WebApplication.CreateBuilder(new WebApplicationOptions { Args=[] });
builder.WebHost.ConfigureKestrel(options=>{options.Listen(IPAddress.Loopback,8767);options.Limits.MaxRequestBodySize=4096;});
builder.Logging.ClearProviders();
var setup=builder.Build();
var iphone=new IPhoneUsbHost();
setup.Run(async context=>
{
    context.Response.Headers.CacheControl="no-store";
    context.Response.Headers["X-Frame-Options"]="DENY";
    context.Response.Headers["Referrer-Policy"]="no-referrer";
    context.Response.Headers["X-Content-Type-Options"]="nosniff";
    string host=context.Request.Host.Value??"";
    if(context.Connection.RemoteIpAddress is not IPAddress peer || !IPAddress.IsLoopback(peer) ||
        (host!="127.0.0.1:8767"&&host!="localhost:8767") ||
        context.Request.Headers.ContainsKey("X-Forwarded-For") || context.Request.Headers.ContainsKey("Tailscale-User-Login") ||
        context.Request.Method!="GET" ||
        (context.Request.Headers.Origin.Count>0&&context.Request.Headers.Origin!="http://"+host))
    {context.Response.StatusCode=403;return;}
    if(context.Request.Path=="/api/usb")
    {await context.Response.WriteAsJsonAsync(new{app="racing-usb-dashboard",mode=requestedMode,protocol=UsbTunnel.Protocol,connected=iphone.Status.connected,iphone=iphone.Status});return;}
    if(context.Request.Path!="/"){context.Response.StatusCode=404;return;}
    context.Response.ContentType="text/html; charset=utf-8";
    await context.Response.WriteAsync(UsbSetup.Page);
});
await setup.StartAsync();
Console.WriteLine("USB管理画面の準備: http://127.0.0.1:8767/");
Console.WriteLine("iPhone / iPad専用アプリからUSBで自動接続します。");
var monitor=iphone.Run(setup.Lifetime.ApplicationStopping);
OpenSetup();
await setup.WaitForShutdownAsync();
try{await monitor;}catch(OperationCanceledException){}

void OpenSetup()
{
    if(args.Contains("--no-browser"))return;
    var start=new System.Diagnostics.ProcessStartInfo(OperatingSystem.IsMacOS()?"/usr/bin/open":OperatingSystem.IsWindows()?"cmd":"xdg-open"){UseShellExecute=false};
    if(OperatingSystem.IsWindows()){start.ArgumentList.Add("/c");start.ArgumentList.Add("start");start.ArgumentList.Add("");}
    start.ArgumentList.Add("http://127.0.0.1:8767/");System.Diagnostics.Process.Start(start)?.Dispose();
}
