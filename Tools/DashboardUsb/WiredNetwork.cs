using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Diagnostics;

internal sealed record WiredAddress(string name,string address,string mask);
internal static class WiredNetwork
{
    internal static HashSet<string> MacEthernetDevices(string ports)
    {
        var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(string block in ports.Replace("\r","").Split("\n\n"))
        {
            var lines=block.Split('\n',StringSplitOptions.TrimEntries);
            string hardware=lines.FirstOrDefault(l=>l.StartsWith("Hardware Port: "))??"";
            string? device=lines.FirstOrDefault(l=>l.StartsWith("Device: "));
            if(device!=null&&(hardware.Contains("Ethernet",StringComparison.OrdinalIgnoreCase)||hardware.Contains("LAN",StringComparison.OrdinalIgnoreCase))&&
                !hardware.Contains("WLAN",StringComparison.OrdinalIgnoreCase)&&!hardware.Contains("Wireless",StringComparison.OrdinalIgnoreCase))names.Add(device[8..]);
        }
        return names;
    }
    private static HashSet<string>? AllowedMacDevices()
    {
        if(!OperatingSystem.IsMacOS())return null;
        var start=new ProcessStartInfo("/usr/sbin/networksetup"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};start.ArgumentList.Add("-listallhardwareports");
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        if(!process.WaitForExit(1500)){process.Kill();return [];}
        return process.ExitCode==0?MacEthernetDevices(output.GetAwaiter().GetResult()):[];
    }
    internal static bool IsEthernet(string name,NetworkInterfaceType type)
        => type==NetworkInterfaceType.Ethernet && !name.StartsWith("bridge",StringComparison.OrdinalIgnoreCase);
    internal static bool Private(IPAddress address)
    {
        byte[] b=address.GetAddressBytes();
        return b.Length==4&&(b[0]==10||b[0]==172&&b[1]>=16&&b[1]<=31||b[0]==192&&b[1]==168);
    }
    internal static bool Host(IPAddress address,IPAddress mask)
    {
        byte[] bytes=address.GetAddressBytes(), masks=mask.GetAddressBytes();
        if(bytes.Length!=4||masks.Length!=4)return false;
        uint m=0,h=0;for(int i=0;i<4;i++){m=(m<<8)|masks[i];h=(h<<8)|(uint)(bytes[i]&~masks[i]&255);}
        uint inverse=~m;
        return inverse>=3&&inverse<0x80000000&&(inverse&(inverse+1))==0&&h!=0&&h!=inverse;
    }
    public static WiredAddress[] List()
    {
        var allowed=AllowedMacDevices();
        return NetworkInterface.GetAllNetworkInterfaces()
        .Where(n=>n.OperationalStatus==OperationalStatus.Up&&IsEthernet(n.Name,n.NetworkInterfaceType)&&(allowed==null||allowed.Contains(n.Name)))
        .SelectMany(n=>n.GetIPProperties().UnicastAddresses.Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork&&Private(a.Address)&&Host(a.Address,a.IPv4Mask))
            .Select(a=>new WiredAddress(n.Name,a.Address.ToString(),a.IPv4Mask.ToString()))).ToArray();
    }
    internal static UsbLink Resolve(IEnumerable<WiredAddress> available,string name,string address,string peer)
    {
        WiredAddress configured=available.SingleOrDefault(a=>a.name==name&&a.address==address)??throw new ArgumentException("選んだ有線ポートにIPv4アドレスがありません。MacのEthernet設定を確認してください。");
        if(!IPAddress.TryParse(address,out var local)||!IPAddress.TryParse(peer,out var remote)||!IPAddress.TryParse(configured.mask,out var mask)||
            !Private(local)||!Private(remote)||local.Equals(remote)||!Host(local,mask)||!Host(remote,mask)||!UsbNetwork.SameSubnet(local,remote,mask))
            throw new ArgumentException("iPhoneには、Macと同じサブネット内の別のプライベートIPv4アドレスを指定してください。");
        return new UsbLink(name,local,remote);
    }
    public static UsbLink Resolve(string name,string address,string peer)=>Resolve(List(),name,address,peer);
}
