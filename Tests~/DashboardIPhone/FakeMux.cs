using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

internal sealed class FakeMux:IDisposable
{
    public string Path {get;}="/private/tmp/racing-mux-"+Guid.NewGuid().ToString("N")+".sock";
    private readonly Socket listener=new(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
    private readonly CancellationTokenSource stop=new();
    private readonly Task serve;
    public FakeMux(int targetPort)
    {
        listener.Bind(new UnixDomainSocketEndPoint(Path));listener.Listen(4);serve=Serve(targetPort);
    }
    private async Task Serve(int targetPort)
    {
        try
        {
            while(!stop.IsCancellationRequested)
            {
                var socket=await listener.AcceptAsync(stop.Token);_ = Handle(socket,targetPort);
            }
        }
        catch(Exception error)when(error is OperationCanceledException or SocketException or ObjectDisposedException){}
    }
    private async Task Handle(Socket socket,int targetPort)
    {
        using var stream=new NetworkStream(socket,true);
        try
        {
            var header=new byte[16];await stream.ReadExactlyAsync(header,stop.Token);
            uint size=BinaryPrimitives.ReadUInt32LittleEndian(header);if(size>65536||size<16||BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4))!=1||BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8))!=8)throw new IOException("Bad plist framing.");
            var payload=new byte[size-16];await stream.ReadExactlyAsync(payload,stop.Token);var root=XDocument.Parse(Encoding.UTF8.GetString(payload)).Root!.Element("dict")!;
            bool connect=UsbMux.Value(root,"MessageType")?.Value=="Connect";
            if(connect&&(UsbMux.Value(root,"DeviceID")?.Value!="7"||UsbMux.Value(root,"PortNumber")?.Value!=unchecked((ushort)IPAddress.HostToNetworkOrder((short)30220)).ToString()))throw new IOException("Wrong USB device/port byte order.");
            string reply=connect?"<dict><key>MessageType</key><string>Result</string><key>Number</key><integer>0</integer></dict>":"<dict><key>DeviceList</key><array><dict><key>DeviceID</key><integer>7</integer><key>Properties</key><dict><key>ConnectionType</key><string>USB</string></dict></dict><dict><key>DeviceID</key><integer>99</integer><key>Properties</key><dict><key>ConnectionType</key><string>Network</string></dict></dict></array></dict>";
            byte[] response=Encoding.UTF8.GetBytes("<plist version=\"1.0\">"+reply+"</plist>");BinaryPrimitives.WriteUInt32LittleEndian(header,(uint)(response.Length+16));
            // Exercise short reads in the production plist parser.
            await stream.WriteAsync(header.AsMemory(0,7),stop.Token);await stream.WriteAsync(header.AsMemory(7),stop.Token);await stream.WriteAsync(response,stop.Token);
            if(connect)
            {
                using var tcp=new TcpClient();await tcp.ConnectAsync(IPAddress.Loopback,targetPort,stop.Token);var upstream=tcp.GetStream();
                using var linkStop=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                var a=stream.CopyToAsync(upstream,linkStop.Token);var b=upstream.CopyToAsync(stream,linkStop.Token);
                await Task.WhenAny(a,b);linkStop.Cancel();try{await Task.WhenAll(a,b);}catch(Exception error)when(error is OperationCanceledException or IOException){}
            }
        }
        catch(Exception error)when(error is OperationCanceledException or IOException or SocketException){}
    }
    public void Dispose(){stop.Cancel();listener.Dispose();if(File.Exists(Path))File.Delete(Path);}
}
