using System.Net.Sockets;
using System.Security.Cryptography;

internal sealed record IPhoneUsbStatus(bool connected,string state,string code);
internal sealed class IPhoneUsbHost
{
    private readonly UsbMux mux=new();
    private readonly string code=RandomNumberGenerator.GetInt32(100_000_000).ToString("D8");
    private IPhoneUsbStatus status;
    public IPhoneUsbHost()=>status=new(false,"USBケーブルでiPhoneをつなぎ、専用アプリを開いてください。",code);
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
                Set(false,devices.Length==0?"USB接続を待っています。iPhoneでこのPCを信頼してください。":"iPhoneの専用アプリを開き、8けたの接続コードを入力してください。");
                foreach(int device in devices.Take(4))
                {
                    try
                    {
                        Stream stream;
                        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation)){timeout.CancelAfter(2000);stream=await mux.Connect(device,30220,timeout.Token);}
                        using(stream)await new UsbTunnel(8766,code,active=>Set(active,active?"iPhoneとUSBだけで接続中です。":"USB接続を待っています。アプリと接続コードを確認してください。")).Run(stream,cancellation);
                    }
                    catch(Exception error)when(error is IOException or SocketException or OperationCanceledException or System.Text.Json.JsonException){}
                }
            }
            catch(Exception error)when(error is IOException or SocketException or OperationCanceledException or System.Xml.XmlException)
            {if(!cancellation.IsCancellationRequested)Set(false,"USB端末を確認できません。iPhoneの信頼設定とケーブルを確認してください。");}
            await Task.Delay(2000,cancellation);
        }
    }
    private void Set(bool active,string message)=>Volatile.Write(ref status,new(active,message,code));
}
