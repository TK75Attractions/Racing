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
    private readonly ConcurrentQueue<string> receivedLines = new ConcurrentQueue<string>();
    private readonly ConcurrentQueue<string> diagnosticMessages = new ConcurrentQueue<string>();
    private readonly DriveInputState[] currentStates = { DriveInputState.Neutral, DriveInputState.Neutral };
    private readonly bool[] hasReceivedInput = new bool[2];
    private readonly float[] lastInputTimes = { -1f, -1f };

    private SerialPort serialPort;
    private Thread readThread;
    private volatile bool stopRequested;
    private volatile bool portIsOpen;
    private long linesReceived;

    public event Action<string, string> LineProcessed;
    public string PortName => portName;
    public bool IsPortOpen => portIsOpen;
    public int PendingLineCount => receivedLines.Count;
    public long LinesReceived => Interlocked.Read(ref linesReceived);
    public int LinesProcessed { get; private set; }
    public int ParseErrorCount { get; private set; }
    public string LastSerialLine { get; private set; } = "-";
    public string LastParseResult { get; private set; } = "Waiting for input";
    public float LastSerialLineTime { get; private set; } = -1f;

    public TwoPlayerSerialInputSource(
        Esp32SerialConfiguration configuration,
        string resolvedPortName,
        bool playerOneOnly = false)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        portName = resolvedPortName;
        this.playerOneOnly = playerOneOnly;
        Open();
    }

    public DriveInputState GetInputState(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= currentStates.Length) return DriveInputState.Neutral;
        return IsConnected(playerIndex) ? currentStates[playerIndex] : DriveInputState.Neutral;
    }

    public bool IsConnected(int playerIndex)
    {
        return playerIndex >= 0 && playerIndex < currentStates.Length && portIsOpen &&
            hasReceivedInput[playerIndex] &&
            Time.realtimeSinceStartup - lastInputTimes[playerIndex] <= configuration.InputTimeoutSeconds;
    }

    public void UpdateInput(float deltaTime)
    {
        FlushDiagnostics();
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

            if (playerOneOnly)
            {
                if (!SerialInputProtocol.TryParseInput(
                        line, configuration.SteeringDivisor, out SerialInputFrame playerOneFrame))
                {
                    ParseErrorCount++;
                    RecordParseResult("INVALID", line);
                    continue;
                }

                RecordParseResult("OK", line);
                ApplyPlayerFrame(0, playerOneFrame, valid: true);
                continue;
            }

            if (!SerialInputProtocol.TryParseTwoPlayerInput(
                    line, configuration.SteeringDivisor, out TwoPlayerSerialInputFrame frame))
            {
                ParseErrorCount++;
                RecordParseResult("INVALID", line);
                continue;
            }

            bool anyValid = frame.PlayerOneValid || frame.PlayerTwoValid;
            if (!anyValid) ParseErrorCount++;
            RecordParseResult(anyValid ? "OK" : "IGNORED", line);
            ApplyPlayerFrame(0, frame.PlayerOne, frame.PlayerOneValid);
            ApplyPlayerFrame(1, frame.PlayerTwo, frame.PlayerTwoValid);
        }
    }

    public void Dispose()
    {
        stopRequested = true;
        portIsOpen = false;
        try
        {
            if (serialPort != null && serialPort.IsOpen) serialPort.Close();
        }
        catch { }

        if (readThread != null && readThread.IsAlive)
            readThread.Join(Math.Max(250, configuration.ReadTimeoutMilliseconds * 2));
        serialPort?.Dispose();
        serialPort = null;
        readThread = null;
    }

    private void ApplyPlayerFrame(int playerIndex, SerialInputFrame frame, bool valid)
    {
        if (!valid) return; // Keep the previous state when either axis is nan.
        DriveInputState state = currentStates[playerIndex];
        state.pedal = frame.Pedal;
        state.steering = frame.Steering;
        currentStates[playerIndex] = state;
        hasReceivedInput[playerIndex] = true;
        lastInputTimes[playerIndex] = Time.realtimeSinceStartup;
    }

    private void RecordParseResult(string status, string line)
    {
        LastParseResult = status;
        LineProcessed?.Invoke(status, line);
    }

    private void Open()
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            Debug.LogError("Shared serial port is not configured.");
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
            readThread = new Thread(ReadLoop) { IsBackground = true, Name = $"ESP32-shared-{portName}" };
            readThread.Start();
        }
        catch (Exception exception)
        {
            portIsOpen = false;
            Debug.LogError($"Failed to open shared serial controller on {portName}: {exception.Message}");
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
            catch (TimeoutException) { }
            catch (Exception exception)
            {
                if (!stopRequested) diagnosticMessages.Enqueue($"Shared serial read failed: {exception.Message}");
                break;
            }
        }
        portIsOpen = false;
    }

    private void FlushDiagnostics()
    {
        while (diagnosticMessages.TryDequeue(out string message)) Debug.LogError(message);
    }
}

// The cars use one logical input source each, while both adapters read the same port.
public sealed class SharedPlayerDriveInputSource : IDriveInputSource
{
    private readonly TwoPlayerSerialInputSource sharedSource;

    public int PlayerIndex { get; }
    public string DeviceId => $"P{PlayerIndex + 1}";
    public bool IsConnected => sharedSource.IsConnected(PlayerIndex);
    public DriveInputState CurrentState => sharedSource.GetInputState(PlayerIndex);

    public SharedPlayerDriveInputSource(TwoPlayerSerialInputSource sharedSource, int playerIndex)
    {
        this.sharedSource = sharedSource ?? throw new ArgumentNullException(nameof(sharedSource));
        PlayerIndex = playerIndex;
    }

    public void UpdateInput(float deltaTime) { }
    public void Dispose() { }
}
