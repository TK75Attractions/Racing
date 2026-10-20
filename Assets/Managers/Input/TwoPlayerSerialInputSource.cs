using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

// Reads one physical serial stream and exposes the latest state for both players.
public sealed class TwoPlayerSerialInputSource : IDisposable
{
    private readonly Esp32SerialConfiguration configuration;
    private readonly string portName;
    private readonly bool playerOneOnly;
    private readonly struct ReceivedFrame
    {
        public readonly string Wire;
        public readonly double ReceivedAt;
        public ReceivedFrame(string wire, double receivedAt) { Wire = wire; ReceivedAt = receivedAt; }
    }
    private readonly ConcurrentQueue<ReceivedFrame> receivedLines = new ConcurrentQueue<ReceivedFrame>();
    private readonly ConcurrentQueue<string> diagnosticMessages = new ConcurrentQueue<string>();
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly RgpHostSession session;

    private SerialPort serialPort;
    private MacPseudoTerminalLineReader pseudoTerminal;
    private Thread readThread;
    private volatile bool stopRequested;
    private volatile bool portIsOpen;
    private long linesReceived;
    private bool inputWasFresh;
    private bool processingReceivedFrames;

    public event Action<string, string> LineProcessed;
    public RgpSerialJournal Monitor { get; } = new RgpSerialJournal();
    public RgpHostSession Session => session;
    public bool HasFreshTelemetry
    {
        get
        {
            ProcessReceivedFrames();
            return portIsOpen && session.IsFresh(clock.Elapsed.TotalMilliseconds);
        }
    }
    public bool InvalidTelemetry { get; private set; }
    public bool HasRecentDeviceData => portIsOpen && clock.Elapsed.TotalMilliseconds - lastDeviceDataAt < 3000;
    private double lastDeviceDataAt = double.NegativeInfinity;
    private double nextStatusAt = 250;
    public bool TrySendCommand(string command, string[] args, out uint sequence, out string reason)
    {
        sequence = 0; reason = "serial_disconnected";
        if (!portIsOpen || !HasRecentDeviceData) return false;
        bool sent = session.TryCommand(command, args, clock.Elapsed.TotalMilliseconds, out sequence, out reason);
        if (sent && !portIsOpen) { reason = "write_failed"; session.Disconnect(); return false; }
        return sent;
    }
    public string PortName => portName;
    public bool IsPortOpen => portIsOpen;
    public int PendingLineCount => receivedLines.Count;
    public long LinesReceived => Interlocked.Read(ref linesReceived);
    public int LinesProcessed { get; private set; }
    public int ParseErrorCount { get; private set; }
    public string LastSerialLine { get; private set; } = "-";
    public string LastParseResult { get; private set; } = "Waiting for input";
    public float LastSerialLineTime { get; private set; } = -1f;
    public double FirstUsableInputMilliseconds { get; private set; } = -1;

    public TwoPlayerSerialInputSource(
        Esp32SerialConfiguration configuration,
        string resolvedPortName,
        bool playerOneOnly = false)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        portName = resolvedPortName;
        this.playerOneOnly = playerOneOnly;
        session = new RgpHostSession(Send);
        session.CommandTimedOut += command => RecordParseResult("TIMEOUT", command);
        Open();
    }

    public DriveInputState GetInputState(int playerIndex)
    {
        if (!IsConnected(playerIndex)) return DriveInputState.Neutral;
        RgpPlayerInput input = playerIndex == 0 ? session.PlayerOne : session.PlayerTwo;
        // RGP keeps accel/brake independent. This game's signed pedal uses their difference.
        return new DriveInputState { pedal = input.Accel - input.Brake,
            steering = input.Handle / configuration.SteeringDivisor };
    }

    public bool IsConnected(int playerIndex)
    {
        // Catch-up FixedUpdate runs before Gmanager.Update after a slow frame. The
        // reader may already have fresh TEL queued: apply it before checking its age,
        // otherwise a render hitch permanently latches the car's pedal-release gate.
        ProcessReceivedFrames();
        return playerIndex >= 0 && playerIndex < (playerOneOnly ? 1 : 2) && portIsOpen &&
            session.IsFresh(clock.Elapsed.TotalMilliseconds);
    }

    public void UpdateInput(float deltaTime)
    {
        FlushDiagnostics();
        ProcessReceivedFrames();
        if (!portIsOpen) return;
        session.Tick(clock.Elapsed.TotalMilliseconds);
        if (clock.Elapsed.TotalMilliseconds >= nextStatusAt)
        {
            // Startup/recovery needs prompt status verification; steady-state diagnostics stay at 2s.
            nextStatusAt = clock.Elapsed.TotalMilliseconds + (session.State == "READY" ? 2000 : 250);
            session.TryCommand("GET_STATUS", Array.Empty<string>(), clock.Elapsed.TotalMilliseconds, out _, out _);
        }
        bool fresh = session.IsFresh(clock.Elapsed.TotalMilliseconds);
        if (inputWasFresh && !fresh && portIsOpen && session.State == "READY" && session.TelemetryEnabled)
            RecordParseResult("STALE", "TEL");
        inputWasFresh = fresh;
    }

    // Called on the main thread only; the read thread still owns byte I/O alone.
    // Keep each frame's original receive timestamp so old queued data cannot hide a real outage.
    private void ProcessReceivedFrames()
    {
        if (processingReceivedFrames) return; // LineProcessed subscribers may inspect input.
        processingReceivedFrames = true;
        try
        {
            if (!portIsOpen)
            {
                session.Disconnect();
                inputWasFresh = false;
                while (receivedLines.TryDequeue(out _)) { }
                return;
            }
            while (receivedLines.TryDequeue(out ReceivedFrame received))
            {
                LinesProcessed++;
                LastSerialLine = received.Wire.TrimEnd('\r', '\n');
                LastSerialLineTime = Time.realtimeSinceStartup;
                if (!SerialInputProtocol.TryParse(received.Wire, out RgpFrame frame))
                {
                    if (LastSerialLine.StartsWith("@TEL,", StringComparison.Ordinal)) InvalidTelemetry = true;
                    ParseErrorCount++;
                    RecordParseResult("INVALID", LastSerialLine);
                    continue;
                }
                lastDeviceDataAt = received.ReceivedAt;
                string result = session.Receive(frame, received.ReceivedAt);
                if (frame.Type == "TEL" && result == "OK") InvalidTelemetry = false;
                if (frame.Type == "TEL" && result == "OK" && FirstUsableInputMilliseconds < 0 &&
                    session.IsFresh(clock.Elapsed.TotalMilliseconds))
                {
                    FirstUsableInputMilliseconds = clock.Elapsed.TotalMilliseconds;
                    Monitor.Diagnostic("INPUT_READY", "startup_ms=" + FirstUsableInputMilliseconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
                }
                RecordParseResult(result, LastSerialLine);
            }
        }
        finally { processingReceivedFrames = false; }
    }

    private void Send(string wire)
    {
        if (!portIsOpen) return;
        try { if (pseudoTerminal != null) pseudoTerminal.Write(wire); else serialPort.Write(wire); Monitor.RecordWire("TX", wire); }
        catch (Exception exception)
        {
            portIsOpen = false;
            QueueDiagnostic($"RGP command write failed: {exception.Message}");
        }
    }

    public void Dispose()
    {
        stopRequested = true;
        portIsOpen = false;
        session.Disconnect();
        try
        {
            if (serialPort != null && serialPort.IsOpen) serialPort.Close();
        }
        catch { }

        if (readThread != null && readThread.IsAlive)
            readThread.Join(Math.Max(250, configuration.ReadTimeoutMilliseconds * 2));
        pseudoTerminal?.Dispose();
        pseudoTerminal = null;
        serialPort?.Dispose();
        serialPort = null;
        readThread = null;
    }

    private void RecordParseResult(string status, string line)
    {
        LastParseResult = status;
        if (status == "STALE" || status == "TIMEOUT" || status == "INCOMPATIBLE")
            Monitor.Diagnostic(status, line);
        LineProcessed?.Invoke(status, line);
    }

    private void Open()
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            QueueDiagnostic("Shared serial port is not configured.");
            return;
        }

        try
        {
            if (configuration.UsePseudoTerminal)
            {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                pseudoTerminal = new MacPseudoTerminalLineReader(portName, configuration.ReadTimeoutMilliseconds);
#else
                throw new PlatformNotSupportedException("The serial simulator requires macOS.");
#endif
            }
            else
            {
                serialPort = new SerialPort(portName, configuration.BaudRate, Parity.None, 8, StopBits.One)
                { Handshake = Handshake.None, ReadTimeout = configuration.ReadTimeoutMilliseconds,
                    WriteTimeout = configuration.ReadTimeoutMilliseconds };
                serialPort.Open();
            }
            portIsOpen = true;
            session.Start(clock.Elapsed.TotalMilliseconds);
            readThread = new Thread(ReadLoop) { IsBackground = true, Name = $"ESP32-shared-{portName}" };
            readThread.Start();
        }
        catch (Exception exception)
        {
            portIsOpen = false;
            session.Disconnect();
            pseudoTerminal?.Dispose();
            pseudoTerminal = null;
            serialPort?.Dispose();
            serialPort = null;
            QueueDiagnostic($"Failed to open shared serial controller on {portName}: {exception.Message}");
        }
    }

    private void ReadLoop()
    {
        var decoder = new RgpStreamDecoder();
        var bytes = new byte[256];
        while (!stopRequested && portIsOpen)
        {
            try
            {
                int count = pseudoTerminal != null ? pseudoTerminal.Read(bytes) : serialPort.Read(bytes, 0, bytes.Length);
                double now = clock.Elapsed.TotalMilliseconds;
                for (int i = 0; i < count; i++)
                {
                    string wire = decoder.Push(bytes[i], now);
                    if (wire == null) continue;
                    Monitor.RecordWire("RX", wire);
                    receivedLines.Enqueue(new ReceivedFrame(wire, now));
                    Interlocked.Increment(ref linesReceived);
                }
            }
            catch (TimeoutException) { decoder.Expire(clock.Elapsed.TotalMilliseconds); }
            catch (Exception exception)
            {
                if (!stopRequested) QueueDiagnostic($"Shared serial read failed: {exception.Message}");
                break;
            }
        }
        portIsOpen = false;
    }

    private void QueueDiagnostic(string text)
    {
        Monitor.Diagnostic("ERROR", text);
        diagnosticMessages.Enqueue(text);
    }

    private void FlushDiagnostics()
    {
        while (diagnosticMessages.TryDequeue(out string message))
        {
            RecordParseResult("ERROR", message);
            Debug.LogError(message);
        }
    }
}

// The cars use one logical input source each, while both adapters read the same port.
public sealed class SharedPlayerDriveInputSource : IDriveInputSource
{
    private readonly TwoPlayerSerialInputSource sharedSource;

    public int PlayerIndex { get; }
    public string DeviceId => $"P{PlayerIndex + 1}";
    public bool IsConnected => sharedSource.IsConnected(PlayerIndex);
    public DriveInputState CurrentState
    {
        get
        {
            DriveInputState state = sharedSource.GetInputState(PlayerIndex);
            state.resetPressed |= KeyboardDriveInputSource.IsResetPressed(PlayerIndex);
            return state;
        }
    }

    public SharedPlayerDriveInputSource(TwoPlayerSerialInputSource sharedSource, int playerIndex)
    {
        this.sharedSource = sharedSource ?? throw new ArgumentNullException(nameof(sharedSource));
        PlayerIndex = playerIndex;
    }

    public void UpdateInput(float deltaTime) { }
    public void Dispose() { }
}
