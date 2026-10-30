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
var setup=UsbGateway.Builder(IPAddress.Loopback,8767);
string? usbUrl=null, lastError=null;
var iphone=new IPhoneUsbHost();
bool wiredMode=requestedMode=="wired";
await using var wired=new WiredHost();
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
        (context.Request.Method!="GET"&&!(wiredMode&&context.Request.Method=="POST"&&context.Request.Path.StartsWithSegments("/api/wired"))) ||
        (context.Request.Method=="POST"&&context.Request.Headers.Origin!="http://"+host) ||
        (context.Request.Headers.Origin.Count>0&&context.Request.Headers.Origin!="http://"+host))
    {context.Response.StatusCode=403;return;}
    if(wiredMode&&(context.Request.Path.StartsWithSegments("/api/wired")||context.Request.Path=="/api/usb")){await wired.Handle(context);return;}
    if(context.Request.Path=="/api/usb")
    {await context.Response.WriteAsJsonAsync(new{app="racing-usb-dashboard",mode=requestedMode,protocol=UsbTunnel.Protocol,connected=iphone.Status.connected||usbUrl!=null,url=usbUrl,error=lastError,iphone=iphone.Status});return;}
    if(context.Request.Path!="/"){context.Response.StatusCode=404;return;}
    context.Response.ContentType="text/html; charset=utf-8";
    await context.Response.WriteAsync(wiredMode?WiredSetup.Page:UsbSetup.Page);
});
await setup.StartAsync();
Console.WriteLine("USB管理画面の準備: http://127.0.0.1:8767/");
Console.WriteLine(wiredMode?"Safari用の有線LAN接続を準備します。アプリのインストール・インターネット共有は不要です。":"選択したUSB接続方式で起動しました。RACEの再起動は不要です。");
var monitor=wiredMode?wired.Monitor(setup.Lifetime.ApplicationStopping):args.Contains("--iphone")?iphone.Run(setup.Lifetime.ApplicationStopping):Task.CompletedTask;
var tethering=args.Contains("--tethering")?Watch(setup.Lifetime.ApplicationStopping):Task.CompletedTask;
OpenSetup();
await setup.WaitForShutdownAsync();
try{await Task.WhenAll(monitor,tethering);}catch(OperationCanceledException){}

void OpenSetup()
{
    if(args.Contains("--no-browser"))return;
    var start=new System.Diagnostics.ProcessStartInfo(OperatingSystem.IsMacOS()?"/usr/bin/open":OperatingSystem.IsWindows()?"cmd":"xdg-open"){UseShellExecute=false};
    if(OperatingSystem.IsWindows()){start.ArgumentList.Add("/c");start.ArgumentList.Add("start");start.ArgumentList.Add("");}
    start.ArgumentList.Add("http://127.0.0.1:8767/");System.Diagnostics.Process.Start(start)?.Dispose();
}

async Task Watch(CancellationToken cancellation)
{
    UsbLink? current=null;UsbGateway? gateway=null;WebApplication? server=null;
    try
    {
        while(!cancellation.IsCancellationRequested)
        {
            try
            {
                UsbLink? found=await UsbNetwork.Find(cancellation);
                if(found!=current)
                {
                    usbUrl=null;
                    if(server!=null){await server.StopAsync(cancellation);await server.DisposeAsync();server=null;}
                    gateway?.Dispose();gateway=null;current=null;
                    if(found!=null)
                    {
                        gateway=new UsbGateway(found,new Uri("http://127.0.0.1:8766/"));server=gateway.Build();await server.StartAsync(cancellation);
                        current=found;usbUrl="http://"+found.Address+":8767"+gateway.PairPath;
                    }
                }
                lastError=null;
            }
            catch(Exception error) when(error is System.Net.NetworkInformation.NetworkInformationException or IOException or System.Net.Sockets.SocketException or InvalidOperationException)
            {
                usbUrl=null;current=null;
                if(server!=null){await server.StopAsync(cancellation);await server.DisposeAsync();server=null;}
                gateway?.Dispose();gateway=null;
                lastError="USBネットワークを確認できません。ケーブルとインターネット共有を確認してください。（"+error.GetType().Name+"）";
            }
            await Task.Delay(2000,cancellation);
        }
    }
    finally
    {
        usbUrl=null;
        if(server!=null){await server.StopAsync();await server.DisposeAsync();}
        gateway?.Dispose();
    }
}
