using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
static Uri Url(WebApplication app)=>new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
Require(UsbMode.Requested([])=="wired"&&UsbMode.Requested(["--iphone","--no-browser"])=="iphone"&&UsbMode.Requested(["--tethering"])=="tethering","Each launch option must select its own connection mode.");
bool invalidModes=false;try{UsbMode.Requested(["--iphone","--tethering"]);}catch(ArgumentException){invalidModes=true;}Require(invalidModes,"Conflicting modes cannot silently select another setup page.");
foreach(string current in new[]{"wired","iphone","tethering","usb","unknown"})
{
    using var modeInfo=JsonDocument.Parse(JsonSerializer.Serialize(new{app="racing-usb-dashboard",mode=current,protocol="racing-usb-v2"}));
    foreach(string requested in new[]{"wired","iphone","tethering"})Require(UsbMode.CanReuse(modeInfo.RootElement,requested)==(current==requested),"A duplicate launch may reuse only the explicitly requested mode; legacy/unknown helpers must not hide missing iPhone codes.");
}
using(var legacyMode=JsonDocument.Parse("{\"app\":\"racing-usb-dashboard\",\"mode\":\"iphone\"}"))Require(!UsbMode.CanReuse(legacyMode.RootElement,"iphone"),"The old PIN helper must not be reused for automatic USB clients.");
Console.WriteLine("USB_MODE_TEST: same-mode reuse and conflicting-mode rejection passed");
Require(WiredNetwork.IsEthernet("en5",NetworkInterfaceType.Ethernet)&&!WiredNetwork.IsEthernet("en0",NetworkInterfaceType.Wireless80211)&&!WiredNetwork.IsEthernet("bridge0",NetworkInterfaceType.Ethernet),"Wired selection must exclude Wi-Fi and bridges.");
Require(WiredNetwork.MacEthernetDevices("Hardware Port: Ethernet Adapter\nDevice: en3\n\nHardware Port: USB 10/100/1000 LAN\nDevice: en4\n\nHardware Port: Wi-Fi\nDevice: en0\n\nHardware Port: USB TO DP HDMI\nDevice: en11\n\nHardware Port: Thunderbolt Bridge\nDevice: bridge0\n").SetEquals(new[]{"en3","en4"}),"macOS Wi-Fi, display and bridge ports cannot become wired candidates even when the driver reports Ethernet type.");
var wiredAddresses=new[]{new WiredAddress("en5","192.168.77.1","255.255.255.0")};
Require(WiredNetwork.Resolve(wiredAddresses,"en5","192.168.77.1","192.168.77.2").Peer.Equals(IPAddress.Parse("192.168.77.2")),"An explicitly configured direct Ethernet peer is accepted without a gateway.");
foreach(var selection in new[]{("en0","192.168.77.1","192.168.77.2"),("en5","192.168.1.1","192.168.77.2"),("en5","192.168.77.1","192.168.77.1"),("en5","192.168.77.1","192.168.78.2"),("en5","192.168.77.1","192.168.77.0"),("en5","192.168.77.1","192.168.77.255"),("en5","192.168.77.1","8.8.8.8"),("en5","192.168.77.1","::1")})
{bool denied=false;try{WiredNetwork.Resolve(wiredAddresses,selection.Item1,selection.Item2,selection.Item3);}catch(ArgumentException){denied=true;}Require(denied,"Unconfigured adapters, local/public/foreign/broadcast addresses must not be exposed.");}
Require(!WiredNetwork.Host(IPAddress.Parse("192.168.77.2"),IPAddress.Parse("255.0.255.0")),"Noncontiguous subnet masks are rejected.");
Console.WriteLine("WIRED_TEST: explicit Ethernet selection and peer validation passed");
var devices=UsbNetwork.AppleUsbDevices("Hardware Port: USB TO DP HDMI\nDevice: en11\n\nHardware Port: iPhone USB\nDevice: en5\n\nHardware Port: Wi-Fi\nDevice: en0\n\nHardware Port: iPad USB\nDevice: en7\n");
Require(devices.SetEquals(new[]{"en5","en7"}),"Only named mobile USB network devices may be chosen.");
Require(!UsbNetwork.IsMobileUsb("en11","USB TO DP HDMI",devices)&&!UsbNetwork.IsMobileUsb("en0","Wi-Fi",devices),"USB displays and Wi-Fi are never exposed.");
Require(UsbNetwork.IsMobileUsb("en5","en5",devices)&&UsbNetwork.IsMobileUsb("Ethernet 2","Remote NDIS based Internet Sharing Device",devices),"Apple and tethered RNDIS adapters are recognized.");
Require(UsbNetwork.SameSubnet(IPAddress.Parse("172.20.10.1"),IPAddress.Parse("172.20.10.2"),IPAddress.Parse("255.255.255.240")),"Phone and USB address must share a subnet.");
Require(!UsbNetwork.SameSubnet(IPAddress.Parse("192.168.1.1"),IPAddress.Parse("172.20.10.2"),IPAddress.Parse("255.255.255.240")),"A gateway on another interface is rejected.");
Console.WriteLine("USB_TEST: adapter selection passed; starting isolated loopback servers");
int commands=0;string? observedOrigin=null,observedIdentity=null,observedBody=null;
await using var stub=UsbGateway.Builder(IPAddress.Loopback,0);stub.UseWebSockets();
stub.Run(async context=>
{
    observedOrigin=context.Request.Headers.Origin.ToString();observedIdentity=context.Request.Headers["Tailscale-User-Login"].ToString();
    if(context.Request.Path=="/api/events")
    {
        using var socket=await context.WebSockets.AcceptWebSocketAsync();
        try
        {
            for(int i=0;i<5;i++){await socket.SendAsync(Encoding.UTF8.GetBytes("{\"sequence\":"+i+"}"),WebSocketMessageType.Text,true,context.RequestAborted);await Task.Delay(10,context.RequestAborted);}
            var buffer=new byte[256];await socket.ReceiveAsync(buffer,context.RequestAborted);
        }
        catch(Exception error)when(error is WebSocketException or OperationCanceledException){}
    }
    else if(context.Request.Path=="/api/connection")await context.Response.WriteAsJsonAsync(new{app="racing-dashboard",transport="websocket",canControl=true,remote=false});
    else if(context.Request.Path=="/api/action/test")
    {commands++;observedBody=await new StreamReader(context.Request.Body).ReadToEndAsync();await context.Response.WriteAsJsonAsync(new{ok=true,id="same-request"});}
    else if(context.Request.Path=="/api/player/1/frame")
    {context.Response.ContentType="image/jpeg";await context.Response.Body.WriteAsync(new byte[]{255,216,255,217});}
    else await context.Response.WriteAsJsonAsync(new{state="Title",sessionId="fake-only"});
});
await stub.StartAsync();Console.WriteLine("USB_TEST: isolated upstream ready");Uri target=Url(stub);
using var handler=new SocketsHttpHandler{UseCookies=false,AllowAutoRedirect=false,UseProxy=false};using var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(5)};
using var gateway=new UsbGateway(new("test-usb",IPAddress.Loopback,IPAddress.Loopback),target);
await using var server=gateway.Build(0);await server.StartAsync();Console.WriteLine("USB_TEST: gateway ready");Uri address=Url(server);string origin=address.GetLeftPart(UriPartial.Authority);
async Task<HttpResponseMessage> Request(string path,string? cookie=null,string? suppliedOrigin=null,string? host=null,string? body=null,string? header=null)
{
    using var request=new HttpRequestMessage(body==null?HttpMethod.Get:HttpMethod.Post,new Uri(address,path));
    if(cookie!=null)request.Headers.Add("Cookie",cookie);
    if(suppliedOrigin!=null)request.Headers.Add("Origin",suppliedOrigin);
    if(host!=null)request.Headers.Host=host;
    if(header!=null)request.Headers.Add(header,"spoofed");
    if(body!=null)request.Content=new StringContent(body,Encoding.UTF8,"application/json");
    return await client.SendAsync(request);
}
Require((await Request("/")).StatusCode==HttpStatusCode.Forbidden,"USB browsing requires pairing.");
Require((await Request("/pair?token=wrong")).StatusCode==HttpStatusCode.Forbidden,"Wrong pairing token must fail.");
var paired=await Request(gateway.PairPath);Require(paired.StatusCode==HttpStatusCode.Redirect&&paired.Headers.Location?.ToString()=="/","Pairing removes the secret from the URL.");
string setCookie=paired.Headers.GetValues("Set-Cookie").Single(),cookie=setCookie.Split(';')[0];
Require(setCookie.Contains("httponly",StringComparison.OrdinalIgnoreCase)&&setCookie.Contains("samesite=strict",StringComparison.OrdinalIgnoreCase),"Pairing cookie must be HTTP-only and strict same-site.");
Require(paired.Headers.GetValues("Referrer-Policy").Single()=="no-referrer","No pairing-secret referrer leakage.");
var page=await Request("/",cookie);Require(page.IsSuccessStatusCode&&(await page.Content.ReadAsStringAsync()).Contains("RACING CONTROL"),"Actual dashboard assets must be served.");
Require((await Request("/UsbGateway.cs",cookie)).StatusCode==HttpStatusCode.NotFound,"Source files cannot be served.");
Require((await Request("/api/action/test",cookie,"https://untrusted.test",body:"{}")).StatusCode==HttpStatusCode.Forbidden&&commands==0,"Cross-origin commands must not reach the relay.");
Require((await Request("/api/status",cookie,host:"untrusted.test")).StatusCode==HttpStatusCode.Forbidden,"Invalid Host must fail.");
foreach(string header in new[]{"X-Forwarded-For","Tailscale-User-Login"})Require((await Request("/api/status",cookie,header:header)).StatusCode==HttpStatusCode.Forbidden,"Proxy and spoofed user headers are rejected.");
var info=await Request("/api/connection",cookie);using(var document=JsonDocument.Parse(await info.Content.ReadAsStringAsync()))
{Require(document.RootElement.GetProperty("usb").GetBoolean()&&document.RootElement.GetProperty("canControl").GetBoolean(),"Connection identifies USB and retains control policy.");}
Require(observedOrigin==target.GetLeftPart(UriPartial.Authority)&&observedIdentity=="","Upstream receives only its local origin, with no supplied identity.");
string command="{\"id\":\"same-request\",\"targetSession\":\"fake-only\",\"expectedState\":\"Title\",\"observedAt\":42}";
var action=await Request("/api/action/test",cookie,origin,body:command);
Require(action.IsSuccessStatusCode&&commands==1&&observedBody==command,"Commands and guard fields must pass through unchanged, once.");
var oversized=await Request("/api/action/test",cookie,origin,body:new string('x',8192));Require((int)oversized.StatusCode==413&&commands==1,"Oversized requests cannot reach the relay.");
var frame=await Request("/api/player/1/frame",cookie);Require(frame.Content.Headers.ContentType?.MediaType=="image/jpeg"&&(await frame.Content.ReadAsByteArrayAsync()).Length==4,"Frames proxy as binary data.");
Console.WriteLine("USB_TEST: HTTP policy and forwarding passed; checking WebSocket");
using(var socket=new ClientWebSocket())
{
    socket.Options.SetRequestHeader("Cookie",cookie);socket.Options.SetRequestHeader("Origin",origin);
    using var timeout=new CancellationTokenSource(5000);await socket.ConnectAsync(new UriBuilder(address){Scheme="ws",Path="/api/events"}.Uri,timeout.Token);
    var buffer=new byte[1024];for(int i=0;i<5;i++)
    {var result=await socket.ReceiveAsync(buffer,timeout.Token);using var message=JsonDocument.Parse(buffer.AsMemory(0,result.Count));Require(message.RootElement.GetProperty("sequence").GetInt32()==i,"USB WebSocket forwards live frames in order.");}
    socket.Abort();
}
Console.WriteLine("USB_TEST: WebSocket passed; checking peer and reconnect policies");
using(var otherPeerGateway=new UsbGateway(new("wrong-peer",IPAddress.Loopback,IPAddress.Parse("127.0.0.2")),target))
{
    await using var other=otherPeerGateway.Build(0);await other.StartAsync();var denied=await client.GetAsync(new Uri(Url(other),otherPeerGateway.PairPath));
    Require(denied.StatusCode==HttpStatusCode.Forbidden,"Even a valid token cannot admit another network peer.");await other.StopAsync();
}
using(var reconnected=new UsbGateway(new("replugged",IPAddress.Loopback,IPAddress.Loopback),target))
{
    await using var next=reconnected.Build(0);await next.StartAsync();using var stale=new HttpRequestMessage(HttpMethod.Get,new Uri(Url(next),"/api/status"));stale.Headers.Add("Cookie",cookie);
    Require((await client.SendAsync(stale)).StatusCode==HttpStatusCode.Forbidden,"USB replug rotates pairing and rejects old cookies.");await next.StopAsync();
}
var timings=new List<double>();for(int i=0;i<30;i++){var watch=Stopwatch.StartNew();var response=await Request("/api/action/test",cookie,origin,body:command);Require(response.IsSuccessStatusCode,"Repeated commands reach the isolated stub.");timings.Add(watch.Elapsed.TotalMilliseconds);}timings.Sort();
using(var wiredGateway=new UsbGateway(new("wired-test",IPAddress.Loopback,IPAddress.Loopback),target,wired:true))
{
    await using var wiredServer=wiredGateway.Build(0);await wiredServer.StartAsync();Uri wiredUrl=Url(wiredServer);
    var pairing=await client.GetAsync(new Uri(wiredUrl,wiredGateway.PairPath));
    string wiredCookie=pairing.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    using var wiredRequest=new HttpRequestMessage(HttpMethod.Get,new Uri(wiredUrl,"/api/connection"));wiredRequest.Headers.Add("Cookie",wiredCookie);
    using var response=await client.SendAsync(wiredRequest);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    Require(json.RootElement.GetProperty("wired").GetBoolean()&&json.RootElement.GetProperty("transport").GetString()=="ethernet-websocket"&&json.RootElement.GetProperty("canControl").GetBoolean(),"Safari Ethernet transport is identified and control policy is preserved.");
    await wiredServer.StopAsync();
}
await stub.StopAsync();Require((await Request("/api/status",cookie)).StatusCode==HttpStatusCode.ServiceUnavailable,"A stopped relay returns unavailable instead of pretending to control a race.");
await server.StopAsync();
var report=new{passed=true,scope="loopback-only HTTP/WebSocket gateway, fake relay; no USB-device or real Race commands",commands,medianMs=timings[15],p95Ms=timings[28],maxMs=timings[^1],
 checks="USB adapter selection, peer/Host/Origin/pairing, token rotation, static assets, HTTP command preservation, binary frames, WebSocket order, body limits, offline relay"};
Console.WriteLine("USB_DASHBOARD_TEST_PASS "+JsonSerializer.Serialize(report));
if(args.Length==1){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);await File.WriteAllTextAsync(args[0],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));}
