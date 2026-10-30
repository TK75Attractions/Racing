using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
static Uri Url(WebApplication app)=>new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
static async Task ExpectIo(Func<Task> action){try{await action();throw new Exception("Invalid frame accepted.");}catch(IOException){}}
var oversize=new byte[9];BinaryPrimitives.WriteInt32BigEndian(oversize,65537);oversize[4]=(byte)UsbKind.Data;
using(var badWire=new UsbWire(new MemoryStream(oversize)))await ExpectIo(async()=>await badWire.Read(CancellationToken.None));
using(var badWire=new UsbWire(new MemoryStream(new byte[4])))await ExpectIo(async()=>await badWire.Read(CancellationToken.None));
var parsed=XElement.Parse("<dict><key>DeviceList</key><array><dict><key>DeviceID</key><integer>7</integer><key>Properties</key><dict><key>ConnectionType</key><string>USB</string></dict></dict><dict><key>DeviceID</key><integer>9</integer><key>Properties</key><dict><key>ConnectionType</key><string>Network</string></dict></dict></array></dict>");
Require(UsbMux.UsbDevices(parsed).SequenceEqual(new[]{7}),"Wireless paired devices must be excluded.");
Console.WriteLine("IPHONE_USB: framing limits and USB-only device selection passed");
int commands=0;string? lastBody=null;
byte[] image=Enumerable.Range(0,300_000).Select(i=>(byte)(i%251)).ToArray();
await using var upstream=UsbGateway.Builder(IPAddress.Loopback,0);upstream.UseWebSockets();
upstream.Run(async context=>
{
    Require(context.Request.Host.Value=="127.0.0.1:8766"&&context.Request.Headers.Origin=="http://127.0.0.1:8766","The phone must rewrite the exact local relay origin.");
    Require(context.Request.Headers.Cookie.Count==0,"Private phone pairing cookie must stay on the phone.");
    if(context.Request.Path=="/api/events")
    {
        using var socket=await context.WebSockets.AcceptWebSocketAsync();
        try{for(int i=0;i<4;i++){await socket.SendAsync(Encoding.UTF8.GetBytes("{\"sequence\":"+i+"}"),WebSocketMessageType.Text,true,context.RequestAborted);await Task.Delay(10,context.RequestAborted);}await socket.ReceiveAsync(new byte[256],context.RequestAborted);}
        catch(Exception error)when(error is WebSocketException or OperationCanceledException){}
    }
    else if(context.Request.Path=="/api/action/test")
    {commands++;lastBody=await new StreamReader(context.Request.Body).ReadToEndAsync();await context.Response.WriteAsJsonAsync(new{ok=true});}
    else if(context.Request.Path=="/api/player/1/frame")
    {context.Response.ContentType="image/jpeg";await context.Response.Body.WriteAsync(image);}
    else{context.Response.ContentType="text/html";await context.Response.WriteAsync("RACING CONTROL USB TEST");}
});
await upstream.StartAsync();int targetPort=Url(upstream).Port;
var process=new Process{StartInfo=new ProcessStartInfo(Path.GetFullPath("../../Temp/iphone-usb-harness")){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true}};
var portReady=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
var urlReady=new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
var childErrors=new StringBuilder();process.OutputDataReceived+=(_,e)=>{if(e.Data?.StartsWith("USB_TEST_PORT ")==true)portReady.TrySetResult(int.Parse(e.Data[14..]));if(e.Data?.StartsWith("USB_TEST_URL ")==true)urlReady.TrySetResult(new Uri(e.Data[13..]));};
process.ErrorDataReceived+=(_,e)=>{if(e.Data!=null)childErrors.AppendLine(e.Data);};
using var stop=new CancellationTokenSource();Task tunnel=Task.CompletedTask;FakeMux? fake=null;
try
{
    process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();int port=await portReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
    // Real Swift listener with a wrong code, before any forwarding is allowed.
    using(var wrong=new TcpClient())
    {
        await wrong.ConnectAsync(IPAddress.Loopback,port);
        bool active=false;await new UsbTunnel(targetPort,"00000000",value=>active|=value).Run(wrong.GetStream(),stop.Token);
        Require(!active,"Wrong pairing code cannot open any relay connection.");
    }
    await Task.Delay(50);
    fake=new FakeMux(port);var mux=new UsbMux(fake.Path);
    Require((await mux.List(stop.Token)).SequenceEqual(new[]{7}),"Actual plist request/reply must reject Wi-Fi devices.");
    Stream link=await mux.Connect(7,30220,stop.Token);
    bool connected=false;tunnel=new UsbTunnel(targetPort,"87654321",value=>connected=value).Run(link,stop.Token);
    Uri pair=await urlReady.Task.WaitAsync(TimeSpan.FromSeconds(10));Require(connected,"Native handshake must accept the correct code.");
    using var client=new HttpClient(new SocketsHttpHandler{UseProxy=false,UseCookies=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(6)};
    Uri baseUrl=new(pair.GetLeftPart(UriPartial.Authority));string origin=baseUrl.GetLeftPart(UriPartial.Authority);
    async Task<HttpResponseMessage> Get(string path,string? cookie=null,string? suppliedOrigin=null,string? body=null,string? host=null)
    {
        using var request=new HttpRequestMessage(body==null?HttpMethod.Get:HttpMethod.Post,new Uri(baseUrl,path));
        if(cookie!=null)request.Headers.Add("Cookie",cookie);if(suppliedOrigin!=null)request.Headers.Add("Origin",suppliedOrigin);if(host!=null)request.Headers.Host=host;
        if(body!=null)request.Content=new StringContent(body,Encoding.UTF8,"application/json");return await client.SendAsync(request);
    }
    Require((await Get("/")).StatusCode==HttpStatusCode.Forbidden,"Other phone apps cannot use the private HTTP proxy without a cookie.");
    Require((await Get("/pair?token=wrong")).StatusCode==HttpStatusCode.Forbidden,"Phone requires its private pairing URL.");
    var paired=await Get(pair.PathAndQuery);Require(paired.StatusCode==HttpStatusCode.Found,"Native pairing issues a redirect.");
    string cookie=paired.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    Require((await Get("/",cookie)).IsSuccessStatusCode,"HTML loads over Swift local HTTP → USB protocol → C# relay forwarding.");
    Require((await Get("/api/action/test",cookie,"https://untrusted.test",body:"{}")).StatusCode==HttpStatusCode.Forbidden&&commands==0,"Cross-site controls cannot enter the tunnel.");
    Require((await Get("/api/status",cookie,host:"bad.test")).StatusCode==HttpStatusCode.Forbidden,"Phone Host guard is enforced.");
    Require((await Get("/api/action/test",cookie,origin,body:new string('x',8192))).StatusCode==HttpStatusCode.RequestEntityTooLarge&&commands==0,"Oversized phone requests are rejected before opening a relay stream.");
    const string command="{\"id\":\"native-usb\",\"targetSession\":\"fake\",\"expectedState\":\"Title\",\"stateRevision\":7,\"observedAt\":42}";
    Require((await Get("/api/action/test",cookie,origin,body:command)).IsSuccessStatusCode&&commands==1&&lastBody==command,"Guarded command body reaches the fake relay unchanged, once.");
    var picture=await Get("/api/player/1/frame",cookie);Require((await picture.Content.ReadAsByteArrayAsync()).SequenceEqual(image),"Large binary frames must survive USB fragmentation and channel close without truncation.");
    using(var socket=new ClientWebSocket())
    {
        socket.Options.SetRequestHeader("Origin",origin);socket.Options.SetRequestHeader("Cookie",cookie);
        using var deadline=new CancellationTokenSource(6000);await socket.ConnectAsync(new UriBuilder(baseUrl){Scheme="ws",Path="/api/events"}.Uri,deadline.Token);
        byte[] bytes=new byte[2048];for(int i=0;i<4;i++){var packet=await socket.ReceiveAsync(bytes,deadline.Token);using var json=JsonDocument.Parse(bytes.AsMemory(0,packet.Count));Require(json.RootElement.GetProperty("sequence").GetInt32()==i,"Native WebSocket messages stay ordered.");}
        Require((await Get("/api/action/test",cookie,origin,body:command)).IsSuccessStatusCode,"HTTP commands remain usable while WebSocket is open.");socket.Abort();
    }
    var parallel=await Task.WhenAll(Enumerable.Range(0,8).Select(async _=>await Get("/api/action/test",cookie,origin,body:command)));
    Require(parallel.All(response=>response.IsSuccessStatusCode),"Independent HTTP channels can operate concurrently.");
    stop.Cancel();try{await tunnel;}catch(Exception error)when(error is IOException or OperationCanceledException or SocketException){}
    await Task.Delay(150);try{await Get("/api/status",cookie);throw new Exception("Disconnected USB still accepted traffic.");}catch(HttpRequestException){}
    var report=new{passed=true,scope="production Swift proxy and C# tunnel over fake USB daemon; isolated fake relay, no real Race operations",commands,
        checks="USB-only plist discovery/connect and byte order; wrong code; native cookie/Host/Origin/body guards; HTML, guarded HTTP controls, 300KB binary image, live WebSocket + concurrent controls, USB disconnect"};
    Directory.CreateDirectory("../../Artifacts/DashboardIPhone");await File.WriteAllTextAsync("../../Artifacts/DashboardIPhone/verification.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine("IPHONE_USB_NATIVE_TEST_PASS "+JsonSerializer.Serialize(report));
}
finally
{
    stop.Cancel();if(!process.HasExited){process.Kill();await process.WaitForExitAsync();}fake?.Dispose();
    try{await tunnel;}catch(Exception error)when(error is IOException or OperationCanceledException or SocketException){}
    await upstream.StopAsync();if(childErrors.Length>0)Console.WriteLine(childErrors.ToString());
}
