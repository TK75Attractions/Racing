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
    private const int OpenReadOnlyNonBlockingNoControllingTerminal = 0x0004 | 0x20000;
    private const short PollInput = 0x0001;
    private const short PollError = 0x0008 | 0x0010 | 0x0020;
    private const int Interrupted = 4;
    private const int WouldBlock = 35;
    private const int MaxLineLength = 4096;

    private int descriptor = -1;
    private readonly int timeoutMilliseconds;
    private readonly byte[] buffer = new byte[512];
    private int bufferPosition, bufferLength;
    private readonly StringBuilder line = new StringBuilder();
    private bool discardOversizedLine;

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
        descriptor = Open(path, OpenReadOnlyNonBlockingNoControllingTerminal);
        if (descriptor < 0) throw NativeError("open");
    }

    public string ReadLine()
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (IsOpen)
        {
            while (bufferPosition < bufferLength)
            {
                char value = (char)buffer[bufferPosition++];
                if (value == '\n')
                {
                    string result = discardOversizedLine ? "<oversized simulator line>" : line.ToString().TrimEnd('\r');
                    line.Clear();
                    discardOversizedLine = false;
                    return result;
                }
                if (discardOversizedLine) continue;
                if (line.Length >= MaxLineLength) { line.Clear(); discardOversizedLine = true; }
                else line.Append(value);
            }

            int remaining = timeoutMilliseconds - (int)timer.ElapsedMilliseconds;
            if (remaining <= 0) throw new TimeoutException();
            int current = Volatile.Read(ref descriptor);
            if (current < 0) break;
            var poll = new PollDescriptor { descriptor = current, events = PollInput };
            int polled = Poll(ref poll, 1, remaining);
            if (polled < 0)
            {
                if (Marshal.GetLastWin32Error() == Interrupted) continue;
                throw NativeError("poll");
            }
            if (polled == 0) throw new TimeoutException();
            if ((poll.returnedEvents & PollInput) == 0)
            {
                if ((poll.returnedEvents & PollError) != 0) throw new IOException("Simulator PTY was closed.");
                continue;
            }
            long count = Read(current, buffer, (UIntPtr)buffer.Length).ToInt64();
            if (count < 0)
            {
                int error = Marshal.GetLastWin32Error();
                if (error == Interrupted || error == WouldBlock) continue;
                throw NativeError("read");
            }
            if (count == 0) throw new IOException("Simulator PTY reached end of stream.");
            bufferPosition = 0;
            bufferLength = (int)count;
        }
        throw new ObjectDisposedException(nameof(MacPseudoTerminalLineReader));
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
