using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;

// Exposes only an explicitly selected Ethernet address and one configured phone IP.
// Never changes macOS networking, Internet Sharing, the normal relay or Race.
internal sealed class WiredHost: IAsyncDisposable
{
    private readonly SemaphoreSlim gate=new(1);
    private UsbGateway? gateway;
    private WebApplication? server;
    private UsbLink? link;
    private string? url;
    private string message="有線LANを接続し、MacとiPhoneのIPv4を設定してください。";
    public async Task Handle(HttpContext context)
    {
        await gate.WaitAsync(context.RequestAborted);
        try
        {
            if(context.Request.Method=="POST")
            {
                if(context.Request.Path=="/api/wired/stop")await Stop("有線の待受を停止しました。");
                else if(context.Request.Path=="/api/wired")
                {
                    var input=await context.Request.ReadFromJsonAsync<Selection>(context.RequestAborted)??throw new ArgumentException("接続設定を入力してください。");
                    UsbLink selected=WiredNetwork.Resolve(input.name??"",input.address??"",input.peer??"");
                    await Stop("接続を準備しています。");
                    gateway=new UsbGateway(selected,new Uri("http://127.0.0.1:8766/"),wired:true);
                    server=gateway.Build();
                    try{await server.StartAsync(context.RequestAborted);link=selected;url="http://"+selected.Address+":8767"+gateway.PairPath;message="下のURLをiPhoneのSafariで開いてください。";}
                    catch{await Stop("有線の待受を開始できません。IPとポートを確認してください。");throw;}
                }
                else{context.Response.StatusCode=404;return;}
            }
            else if(context.Request.Method!="GET"||(context.Request.Path!="/api/wired"&&context.Request.Path!="/api/usb")){context.Response.StatusCode=404;return;}
            await Status(context);
        }
        catch(Exception error)when(error is ArgumentException or JsonException or IOException or InvalidOperationException or NetworkInformationException or System.Net.Sockets.SocketException)
        {context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(new{message=error is ArgumentException?error.Message:"有線接続を開始できません。接続設定を確認してください。"});}
        finally{gate.Release();}
    }
    private Task Status(HttpContext context)=>context.Response.WriteAsJsonAsync(new{app="racing-usb-dashboard",mode="wired",listening=link!=null,url,message,interfaces=WiredNetwork.List(),selected=link==null?null:new{name=link.Name,address=link.Address.ToString(),peer=link.Peer.ToString()}});
    public async Task Monitor(CancellationToken cancellation)
    {
        while(!cancellation.IsCancellationRequested)
        {
            await Task.Delay(2000,cancellation);await gate.WaitAsync(cancellation);
            try
            {
                if(link!=null&&!WiredNetwork.List().Any(a=>a.name==link.Name&&a.address==link.Address.ToString()))
                    await Stop("有線接続またはIPが変わりました。接続を確認し、もう一度開始してください。");
            }
            catch(NetworkInformationException){await Stop("有線ポートを確認できません。接続を確認してください。");}
            finally{gate.Release();}
        }
    }
    private async Task Stop(string status)
    {
        url=null;link=null;message=status;
        if(server!=null){await server.StopAsync();await server.DisposeAsync();server=null;}
        gateway?.Dispose();gateway=null;
    }
    public async ValueTask DisposeAsync(){await gate.WaitAsync();try{await Stop("停止しました。");}finally{gate.Release();}}
    private sealed record Selection(string? name,string? address,string? peer);
}
