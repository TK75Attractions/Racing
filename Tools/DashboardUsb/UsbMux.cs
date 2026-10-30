using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

// usbmuxd v1 plist protocol; only USB devices, never Wi-Fi paired devices.
// Reference: libimobiledevice/libusbmuxd (ListDevices / Connect, port in network byte order).
internal sealed class UsbMux(string socketPath="/var/run/usbmuxd")
{
    internal static XElement? Value(XElement dictionary,string key)
    {var entries=dictionary.Elements().ToArray();for(int i=0;i+1<entries.Length;i+=2)if(entries[i].Value==key)return entries[i+1];return null;}
    internal static int[] UsbDevices(XElement root)
        => (Value(root,"DeviceList")?.Elements("dict")??[]).Where(device=>Value(Value(device,"Properties")??new XElement("dict"),"ConnectionType")?.Value=="USB")
            .Select(device=>int.Parse(Value(device,"DeviceID")!.Value)).ToArray();
    public async Task<int[]> List(CancellationToken cancellation)
    {
        using var stream=await Open(cancellation);
        var result=await Exchange(stream,"ListDevices",[],cancellation);
        return UsbDevices(result);
    }
    public async Task<Stream> Connect(int device,int port,CancellationToken cancellation)
    {
        var stream=await Open(cancellation);
        try
        {
            ushort networkPort=unchecked((ushort)IPAddress.HostToNetworkOrder((short)port));
            var response=await Exchange(stream,"Connect",[new XElement("key","DeviceID"),new XElement("integer",device),new XElement("key","PortNumber"),new XElement("integer",networkPort)],cancellation);
            if(Value(response,"Number")?.Value!="0")throw new IOException("iPhoneのUSB管理アプリを起動してください。");
            return stream;
        }
        catch{stream.Dispose();throw;}
    }
    private async Task<NetworkStream> Open(CancellationToken cancellation)
    {
        var socket=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
        try{await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath),cancellation);return new NetworkStream(socket,true);}
        catch{socket.Dispose();throw;}
    }
    internal static async Task<XElement> Exchange(Stream stream,string message,IEnumerable<XElement> arguments,CancellationToken cancellation)
    {
        var plist=new XElement("plist",new XAttribute("version","1.0"),new XElement("dict",
            new XElement("key","MessageType"),new XElement("string",message),new XElement("key","ClientVersionString"),new XElement("string","RacingUsb/1"),
            new XElement("key","ProgName"),new XElement("string","RacingUsb"),arguments));
        byte[] body=Encoding.UTF8.GetBytes(plist.ToString(SaveOptions.DisableFormatting)),header=new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(header,(uint)(body.Length+16));BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4),1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8),8);BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12),1);
        await stream.WriteAsync(header,cancellation);await stream.WriteAsync(body,cancellation);
        await stream.ReadExactlyAsync(header,cancellation);uint size=BinaryPrimitives.ReadUInt32LittleEndian(header);
        if(size<16||size>1_048_576||BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4))!=1||BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8))!=8||BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12))!=1)throw new IOException("Invalid usbmuxd reply.");
        var payload=new byte[size-16];await stream.ReadExactlyAsync(payload,cancellation);
        return XDocument.Parse(Encoding.UTF8.GetString(payload)).Root?.Element("dict")??throw new IOException("Invalid USB plist.");
    }
}
