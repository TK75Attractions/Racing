using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

/// <summary>
/// A macOS PTY has no modem control lines. Mono's SerialPort.Open always sets
/// DTR/RTS and fails with ENOTTY, so simulator sessions read the PTY directly.
/// Physical USB controllers continue to use SerialPort.
/// </summary>
internal sealed class MacPseudoTerminalLineReader : IDisposable
{
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";
    private const int OpenReadWriteNonBlockingNoControllingTerminal = 0x0002 | 0x0004 | 0x20000;
    private const short PollInput = 0x0001;
    private const short PollError = 0x0008 | 0x0010 | 0x0020;
    private const int Interrupted = 4;
    private const int WouldBlock = 35;
    private int descriptor = -1;
    private readonly int timeoutMilliseconds;

    [StructLayout(LayoutKind.Sequential)]
    private struct PollDescriptor
    {
        public int descriptor;
        public short events;
        public short returnedEvents;
    }

    [DllImport(LibSystem, EntryPoint = "open", SetLastError = true)]
    private static extern int Open(string path, int flags);
    [DllImport(LibSystem, EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int descriptor);
    [DllImport(LibSystem, EntryPoint = "poll", SetLastError = true)]
    private static extern int Poll(ref PollDescriptor descriptor, uint count, int timeout);
    [DllImport(LibSystem, EntryPoint = "read", SetLastError = true)]
    private static extern IntPtr Read(int descriptor, byte[] bytes, UIntPtr length);

    [DllImport(LibSystem, EntryPoint = "write", SetLastError = true)]
    private static extern IntPtr WriteBytes(int descriptor, byte[] bytes, UIntPtr length);

    public bool IsOpen => Volatile.Read(ref descriptor) >= 0;

    public static bool IsSupportedPath(string path)
    {
        const string prefix = "/dev/ttys";
        if (string.IsNullOrEmpty(path) || !path.StartsWith(prefix, StringComparison.Ordinal) || path.Length == prefix.Length)
            return false;
        for (int i = prefix.Length; i < path.Length; i++)
            if (path[i] < '0' || path[i] > '9') return false;
        return true;
    }

    public MacPseudoTerminalLineReader(string path, int timeoutMilliseconds)
    {
        if (!IsSupportedPath(path)) throw new ArgumentException("Expected a macOS PTY path such as /dev/ttys000.", nameof(path));
        this.timeoutMilliseconds = Math.Max(1, timeoutMilliseconds);
        descriptor = Open(path, OpenReadWriteNonBlockingNoControllingTerminal);
        if (descriptor < 0) throw NativeError("open");
    }

    // Raw byte I/O shares the exact RGP framing, resync and 1-second expiry used by USB.
    public int Read(byte[] bytes)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (IsOpen)
        {
            int remaining = timeoutMilliseconds - (int)timer.ElapsedMilliseconds;
            if (remaining <= 0) throw new TimeoutException();
            var poll = new PollDescriptor { descriptor = Volatile.Read(ref descriptor), events = PollInput };
            int polled = Poll(ref poll, 1, remaining);
            if (polled < 0) { if (Marshal.GetLastWin32Error() == Interrupted) continue; throw NativeError("poll"); }
            if (polled == 0) throw new TimeoutException();
            if ((poll.returnedEvents & PollInput) == 0)
            { if ((poll.returnedEvents & PollError) != 0) throw new IOException("Simulator PTY was closed."); continue; }
            long count = Read(poll.descriptor, bytes, (UIntPtr)bytes.Length).ToInt64();
            if (count < 0)
            { int error = Marshal.GetLastWin32Error(); if (error == Interrupted || error == WouldBlock) continue; throw NativeError("read"); }
            if (count == 0) throw new IOException("Simulator PTY reached end of stream.");
            return (int)count;
        }
        throw new ObjectDisposedException(nameof(MacPseudoTerminalLineReader));
    }

    public void Write(string wire)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(wire);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        int offset = 0;
        while (offset < bytes.Length && IsOpen)
        {
            if (timer.ElapsedMilliseconds >= timeoutMilliseconds) throw new TimeoutException("Simulator command write timed out.");
            byte[] remaining = new byte[bytes.Length - offset];
            Array.Copy(bytes, offset, remaining, 0, remaining.Length);
            long count = WriteBytes(Volatile.Read(ref descriptor), remaining, (UIntPtr)remaining.Length).ToInt64();
            if (count < 0)
            { int error = Marshal.GetLastWin32Error(); if (error == Interrupted || error == WouldBlock) { Thread.Sleep(1); continue; } throw NativeError("write"); }
            offset += (int)count;
        }
        if (offset != bytes.Length) throw new ObjectDisposedException(nameof(MacPseudoTerminalLineReader));
    }

    // The owner stops and joins the reader thread before disposing this FD,
    // so an in-flight poll cannot accidentally read a reused descriptor.
    public void Dispose()
    {
        int previous = Interlocked.Exchange(ref descriptor, -1);
        if (previous >= 0) Close(previous);
    }

    private static IOException NativeError(string operation) =>
        new IOException($"Simulator PTY {operation} failed (errno {Marshal.GetLastWin32Error()}).");
}
