using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

public sealed class SerialDriveInputSource : IDriveInputSource
{
    private readonly SerialControllerConfiguration configuration;
    private readonly string portName;
    private readonly ConcurrentQueue<string> receivedLines = new ConcurrentQueue<string>();
    private readonly ConcurrentQueue<string> diagnosticMessages = new ConcurrentQueue<string>();

    private SerialPort serialPort;
    private Thread readThread;
    private volatile bool stopRequested;
    private volatile bool portIsOpen;
    private bool hasReceivedInput;
    private bool resetHeld;
    private bool readyHeld;
    private float lastInputTime;
    private long linesReceived;

    // Invoked while draining the queue on the Unity main thread.
    public event Action<SerialDriveInputSource, string, string> LineProcessed;
    public string PortName => portName;
    public bool IsPortOpen => portIsOpen;
    public int PendingLineCount => receivedLines.Count;
    public long LinesReceived => Interlocked.Read(ref linesReceived);
    public int LinesProcessed { get; private set; }
    public int ParseErrorCount { get; private set; }
    public string LastSerialLine { get; private set; } = "-";
    public string LastParseResult { get; private set; } = "Waiting for input";
    public float LastSerialLineTime { get; private set; } = -1f;

    public int PlayerIndex => configuration.PlayerIndex;
    public string DeviceId => configuration.DeviceId;
    public bool IsConnected => portIsOpen &&
        hasReceivedInput &&
        Time.realtimeSinceStartup - lastInputTime <= configuration.InputTimeoutSeconds;
    public DriveInputState CurrentState { get; private set; }

    public SerialDriveInputSource(
        SerialControllerConfiguration configuration,
        string resolvedPortName)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        portName = resolvedPortName;
        Open();
    }

    public void UpdateInput(float deltaTime)
    {
        FlushDiagnostics();

        bool resetPressed = false;
        bool readyPressed = false;
        DriveInputState latestState = CurrentState;
        latestState.resetPressed = false;
        latestState.readyPressed = false;

        while (receivedLines.TryDequeue(out string line))
        {
            LinesProcessed++;
            LastSerialLine = line;
            LastSerialLineTime = Time.realtimeSinceStartup;
            if (SerialInputProtocol.TryReadDeviceId(line, out _))
            {
                RecordParseResult("IDENTITY", line);
                continue;
            }

            if (!SerialInputProtocol.TryParsePartialInput(
                    line,
                    configuration.SteeringDivisor,
                    out SerialInputFrame frame,
                    out bool pedalParsed,
                    out bool steeringParsed))
            {
                ParseErrorCount++;
                RecordParseResult("INVALID", line);
                continue;
            }

            bool complete = pedalParsed && steeringParsed;
            if (!complete) ParseErrorCount++;
            RecordParseResult(complete ? "OK" : "PARTIAL", line);

            hasReceivedInput = true;
            lastInputTime = Time.realtimeSinceStartup;
            resetPressed |= frame.ResetHeld && !resetHeld;
            readyPressed |= frame.ReadyHeld && !readyHeld;
            resetHeld = frame.ResetHeld;
            readyHeld = frame.ReadyHeld;

            if (pedalParsed) latestState.pedal = frame.Pedal;
            if (steeringParsed) latestState.steering = frame.Steering;
        }

        if (!IsConnected)
        {
            latestState.pedal = 0f;
            latestState.steering = 0f;
            resetHeld = false;
            readyHeld = false;
        }

        latestState.resetPressed = resetPressed;
        latestState.readyPressed = readyPressed;
        CurrentState = latestState;
    }

    public void Dispose()
    {
        stopRequested = true;
        portIsOpen = false;

        if (serialPort != null)
        {
            try
            {
                if (serialPort.IsOpen)
                {
                    serialPort.Close();
                }
            }
            catch
            {
                // Shutdown must continue even when the USB device has already disappeared.
            }
        }

        if (readThread != null && readThread.IsAlive)
        {
            readThread.Join(Math.Max(250, configuration.ReadTimeoutMilliseconds * 2));
        }

        serialPort?.Dispose();
        serialPort = null;
        readThread = null;
        CurrentState = DriveInputState.Neutral;
    }

    private void RecordParseResult(string status, string line)
    {
        LastParseResult = status;
        LineProcessed?.Invoke(this, status, line);
    }

    private void Open()
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            Debug.LogError($"Serial port for Player {PlayerIndex + 1} ({DeviceId}) is not configured.");
            return;
        }

        try
        {
            serialPort = new SerialPort(portName, configuration.BaudRate)
            {
                NewLine = "\n",
                ReadTimeout = configuration.ReadTimeoutMilliseconds
            };
            serialPort.Open();
            portIsOpen = true;
            readThread = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = $"ESP32-{DeviceId}-{portName}"
            };
            readThread.Start();
            Debug.Log($"Player {PlayerIndex + 1} serial controller opened: {DeviceId} on {portName}");
        }
        catch (Exception exception)
        {
            portIsOpen = false;
            Debug.LogError(
                $"Failed to open serial controller for Player {PlayerIndex + 1} " +
                $"({DeviceId}) on {portName}: {exception.Message}");
        }
    }

    private void ReadLoop()
    {
        while (!stopRequested && serialPort != null && serialPort.IsOpen)
        {
            try
            {
                string line = serialPort.ReadLine();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    receivedLines.Enqueue(line.Trim());
                    Interlocked.Increment(ref linesReceived);
                }
            }
            catch (TimeoutException)
            {
                // A timeout lets this thread regularly observe stopRequested.
            }
            catch (Exception exception)
            {
                if (!stopRequested)
                {
                    diagnosticMessages.Enqueue(
                        $"Serial read failed for Player {PlayerIndex + 1} " +
                        $"({DeviceId}) on {portName}: {exception.Message}");
                }

                break;
            }
        }

        portIsOpen = false;
    }

    private void FlushDiagnostics()
    {
        while (diagnosticMessages.TryDequeue(out string message))
        {
            Debug.LogError(message);
        }
    }
}

public static class SerialControllerDiscovery
{
    public static bool IsLegacyController(SerialControllerConfiguration configuration, string portName)
    {
        try
        {
            using SerialPort probePort = new SerialPort(portName, configuration.BaudRate)
            {
                NewLine = "\n",
                ReadTimeout = Math.Min(250, configuration.DiscoveryTimeoutMilliseconds)
            };
            probePort.Open();
            bool receivedInput = false;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < configuration.DiscoveryTimeoutMilliseconds)
            {
                try
                {
                    probePort.WriteLine("IDENTIFY");
                    string line = probePort.ReadLine();
                    // Never treat an identified device (including a different player) as legacy.
                    if (SerialInputProtocol.TryReadDeviceId(line, out _)) return false;
                    receivedInput |= SerialInputProtocol.TryParsePartialInput(line,
                        configuration.SteeringDivisor, out _, out _, out _);
                }
                catch (TimeoutException) { }
            }
            return receivedInput;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Could not probe legacy serial port {portName}: {exception.Message}");
            return false;
        }
    }

    public static string FindPortForDevice(
        SerialControllerConfiguration configuration,
        string[] candidatePorts)
    {
        if (configuration == null || candidatePorts == null)
        {
            return null;
        }

        foreach (string portName in candidatePorts)
        {
            if (TryReadDeviceId(configuration, portName, out string deviceId) &&
                string.Equals(
                    deviceId,
                    configuration.DeviceId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return portName;
            }
        }

        return null;
    }

    private static bool TryReadDeviceId(
        SerialControllerConfiguration configuration,
        string portName,
        out string deviceId)
    {
        deviceId = null;

        try
        {
            using SerialPort probePort = new SerialPort(portName, configuration.BaudRate)
            {
                NewLine = "\n",
                ReadTimeout = Math.Min(250, configuration.DiscoveryTimeoutMilliseconds)
            };
            probePort.Open();

            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < configuration.DiscoveryTimeoutMilliseconds)
            {
                try
                {
                    // Opening a serial port can reset an ESP32, so repeat the request while it boots.
                    probePort.WriteLine("IDENTIFY");
                    string line = probePort.ReadLine();
                    if (SerialInputProtocol.TryReadDeviceId(line, out deviceId))
                    {
                        return true;
                    }
                }
                catch (TimeoutException)
                {
                    // Continue until the complete discovery window has elapsed.
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Could not probe serial port {portName}: {exception.Message}");
        }

        return false;
    }
}
