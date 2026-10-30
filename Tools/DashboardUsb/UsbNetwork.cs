using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

internal sealed record UsbLink(string Name, IPAddress Address, IPAddress Peer);

// Never infer USB from an IP range or bind a generic USB Ethernet/LAN adapter.
internal static class UsbNetwork
{
    internal static HashSet<string> AppleUsbDevices(string hardwarePorts)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (string block in hardwarePorts.Replace("\r", "").Split("\n\n"))
        {
            string[] lines = block.Split('\n', StringSplitOptions.TrimEntries);
            bool mobile = lines.Any(line => line.StartsWith("Hardware Port: ", StringComparison.Ordinal) &&
                (line[15..].Equals("iPhone USB", StringComparison.OrdinalIgnoreCase) || line[15..].Equals("iPad USB", StringComparison.OrdinalIgnoreCase)));
            string? device = lines.FirstOrDefault(line => line.StartsWith("Device: ", StringComparison.Ordinal));
            if (mobile && device != null) result.Add(device[8..]);
        }
        return result;
    }
    internal static bool IsMobileUsb(string name, string description, ISet<string> appleDevices)
        => appleDevices.Contains(name) || description.Contains("Apple Mobile Device Ethernet", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("Remote NDIS", StringComparison.OrdinalIgnoreCase) || description.Contains("RNDIS", StringComparison.OrdinalIgnoreCase);

    public static async Task<UsbLink?> Find(CancellationToken cancellation)
    {
        var appleDevices = new HashSet<string>();
        if (OperatingSystem.IsMacOS())
        {
            var start = new ProcessStartInfo("/usr/sbin/networksetup") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add("-listallhardwareports");
            using var process = Process.Start(start)!;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(2000);
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var error = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                if (process.ExitCode == 0) appleDevices = AppleUsbDevices(await output);
                await error;
            }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(); cancellation.ThrowIfCancellationRequested(); return null; }
        }
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up || !IsMobileUsb(network.Name, network.Description, appleDevices)) continue;
            var properties = network.GetIPProperties();
            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(unicast.Address)) continue;
                // Only the tethering device itself may connect, not other hotspot/LAN peers.
                var peer = properties.GatewayAddresses.Select(g => g.Address).FirstOrDefault(address =>
                    address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address) &&
                    !address.Equals(unicast.Address) && SameSubnet(address, unicast.Address, unicast.IPv4Mask));
                if (peer != null) return new UsbLink(network.Name, unicast.Address, peer);
            }
        }
        return null;
    }
    internal static bool SameSubnet(IPAddress left, IPAddress right, IPAddress mask)
    {
        byte[] a=left.GetAddressBytes(), b=right.GetAddressBytes(), m=mask.GetAddressBytes();
        return a.Length==4 && b.Length==4 && m.Length==4 && !m.All(value=>value==0) &&
            Enumerable.Range(0,4).All(i=>(a[i]&m[i])==(b[i]&m[i]));
    }
}
