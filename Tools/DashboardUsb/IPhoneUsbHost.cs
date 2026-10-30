using System.Net.Sockets;

internal sealed record IPhoneUsbStatus(bool connected,string state,string authentication="trusted-usb");
internal sealed class IPhoneUsbHost
{
    private readonly UsbMux mux=new();
    private IPhoneUsbStatus status;
    public IPhoneUsbHost()=>status=new(false,"iPhone / iPadのRacing USBアプリを開き、USBでつないでください。コード入力は不要です。");
    public IPhoneUsbStatus Status=>Volatile.Read(ref status);
    public async Task Run(CancellationToken cancellation)
    {
        if(!OperatingSystem.IsMacOS()){Set(false,"iPhone用USB中継はMacで起動してください。");return;}
        while(!cancellation.IsCancellationRequested)
        {
            try
            {
                using var discovery=CancellationTokenSource.CreateLinkedTokenSource(cancellation);discovery.CancelAfter(2000);
                int[] devices=await mux.List(discovery.Token);
                Set(false,devices.Length==0?"USB接続を待っています。端末でこのMacを信頼してください。":"専用アプリの自動接続を待っています。コード欄がある旧版アプリはXcodeで更新してください。");
                foreach(int device in devices.Take(4))
                {
                    try
                    {
                        Stream stream;
                        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation)){timeout.CancelAfter(2000);stream=await mux.Connect(device,30220,timeout.Token);}
                        using(stream)await new UsbTunnel(8766,active=>Set(active,active?"端末とUSBだけで接続中です。":"USB接続を待っています。最新版のアプリを前面にしてください。")).Run(stream,cancellation);
                    }
                    catch(Exception error)when(error is IOException or SocketException or OperationCanceledException or System.Text.Json.JsonException){}
                }
            }
            catch(Exception error)when(error is IOException or SocketException or OperationCanceledException or System.Xml.XmlException)
            {if(!cancellation.IsCancellationRequested)Set(false,"USB端末を確認できません。iPhoneの信頼設定とケーブルを確認してください。");}
            await Task.Delay(2000,cancellation);
        }
    }
    private void Set(bool active,string message)=>Volatile.Write(ref status,new(active,message));
}
