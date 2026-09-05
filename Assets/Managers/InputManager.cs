using System;
using System.Collections.Generic;
using System.IO.Ports;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    public const int SupportedPlayerCount = 2;

    [Header("Input Mode")]
    [Tooltip("Enable two keyboard layouts instead of opening the ESP32 serial controllers.")]
    public bool isDebugMode = false;

    [Header("ESP32 Controllers")]
    [SerializeField] private SerialControllerConfiguration[] serialControllers =
    {
        new SerialControllerConfiguration(0, "P1"),
        new SerialControllerConfiguration(1, "P2")
    };

    [Tooltip("Allow an older controller without DEVICE identity to be discovered for Player 1.")]
    [SerializeField] private bool allowLegacyPlayerOneDiscovery = true;
    [SerializeField] private string[] serialPortNameHints =
        { "usbserial", "usbmodem", "ttyUSB", "ttyACM", "COM" };

    [Header("Player 1 Legacy Monitor")]
    [Tooltip("Player 1 steering value. Kept for the existing title/result input flow.")]
    public float handle;
    [Tooltip("Player 1 pedal value. Kept for the existing title/result input flow.")]
    public float peddale;

    [Header("Serial Debug Monitor")]
    [Tooltip("シリアル入力の状態と受信履歴を画面に表示できるようにします。キーボード入力モードとは独立しています。")]
    [SerializeField] private bool serialDebugMode = false;
    [Tooltip("デバッグモニターを起動時から表示します。実行中は指定キーで切り替えられます。")]
    [SerializeField] private bool serialDebugDisplayVisible = true;
    [SerializeField] private Key serialDebugToggleKey = Key.F8;
    [SerializeField, Range(1, 50)] private int serialDebugLogCapacity = 12;
    [Tooltip("画面表示に加えて、受信したシリアル行をUnity Consoleにも出力します。")]
    [SerializeField] private bool mirrorSerialInputToConsole = false;

    private readonly Queue<string> serialDebugLog = new Queue<string>();
    private Vector2 serialDebugScrollPosition;
    private GUIStyle serialDebugHeaderStyle;
    private GUIStyle serialDebugLabelStyle;
    private GUIStyle serialDebugLogStyle;

    public bool SerialDebugMode
    {
        get => serialDebugMode;
        set => serialDebugMode = value;
    }

    public bool IsSerialDebugDisplayVisible => serialDebugMode && serialDebugDisplayVisible;

    private readonly IDriveInputSource[] inputSources =
        new IDriveInputSource[SupportedPlayerCount];
    private bool initialized;

    public void Init()
    {
        DisposeInputSources();
        EnsureControllerConfigurations();

        if (isDebugMode)
        {
            for (int playerIndex = 0; playerIndex < SupportedPlayerCount; playerIndex++)
            {
                inputSources[playerIndex] = new KeyboardDriveInputSource(playerIndex);
            }

            initialized = true;
            Debug.Log(
                "InputManager is in debug mode. " +
                "Player 1 uses WASD/Space/Enter and Player 2 uses arrow keys/Right Ctrl/Right Shift.");
            return;
        }

        string[] resolvedPorts = ResolveSerialPorts();
        for (int playerIndex = 0; playerIndex < SupportedPlayerCount; playerIndex++)
        {
            SerialControllerConfiguration configuration = serialControllers[playerIndex];
            inputSources[playerIndex] = new SerialDriveInputSource(
                configuration,
                resolvedPorts[playerIndex]);
            ((SerialDriveInputSource)inputSources[playerIndex]).LineProcessed += OnSerialLineProcessed;
        }

        initialized = true;
    }

    public void UpdateInput(float deltaTime)
    {
        UpdateSerialDebugDisplayToggle();
        if (!initialized)
        {
            return;
        }

        foreach (IDriveInputSource source in inputSources)
        {
            source?.UpdateInput(deltaTime);
        }

        DriveInputState playerOneState = GetInputState(0);
        handle = playerOneState.steering;
        peddale = playerOneState.pedal;
    }

    public IDriveInputSource GetPlayerInputSource(int playerIndex)
    {
        return IsValidPlayerIndex(playerIndex) ? inputSources[playerIndex] : null;
    }

    public DriveInputState GetInputState(int playerIndex)
    {
        IDriveInputSource source = GetPlayerInputSource(playerIndex);
        return source != null ? source.CurrentState : DriveInputState.Neutral;
    }

    public bool IsPlayerConnected(int playerIndex)
    {
        IDriveInputSource source = GetPlayerInputSource(playerIndex);
        return source != null && source.IsConnected;
    }

    private string[] ResolveSerialPorts()
    {
        string[] resolvedPorts = new string[SupportedPlayerCount];
        HashSet<string> claimedPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int playerIndex = 0; playerIndex < SupportedPlayerCount; playerIndex++)
        {
            string configuredPort = serialControllers[playerIndex].PortName?.Trim();
            if (string.IsNullOrEmpty(configuredPort))
            {
                continue;
            }

            if (!claimedPorts.Add(configuredPort))
            {
                Debug.LogError(
                    $"Serial port {configuredPort} is assigned to more than one player. " +
                    $"Player {playerIndex + 1} will remain disconnected.");
                continue;
            }

            resolvedPorts[playerIndex] = configuredPort;
        }

        string[] availablePorts;
        try
        {
            availablePorts = SerialPort.GetPortNames();
        }
        catch (Exception exception)
        {
            Debug.LogError($"Failed to enumerate serial ports: {exception.Message}");
            return resolvedPorts;
        }

        Array.Sort(availablePorts, StringComparer.OrdinalIgnoreCase);

        for (int playerIndex = 0; playerIndex < SupportedPlayerCount; playerIndex++)
        {
            if (!string.IsNullOrEmpty(serialControllers[playerIndex].PortName?.Trim()))
            {
                continue;
            }

            List<string> candidates = new List<string>();
            foreach (string availablePort in availablePorts)
            {
                if (!claimedPorts.Contains(availablePort))
                {
                    candidates.Add(availablePort);
                }
            }

            SerialControllerConfiguration configuration = serialControllers[playerIndex];
            string discoveredPort = SerialControllerDiscovery.FindPortForDevice(
                configuration,
                candidates.ToArray());

            if (string.IsNullOrEmpty(discoveredPort))
            {
                continue;
            }

            resolvedPorts[playerIndex] = discoveredPort;
            claimedPorts.Add(discoveredPort);
        }

        // Resolve both identities first so a legacy fallback cannot claim Player 2's port.
        if (allowLegacyPlayerOneDiscovery && string.IsNullOrEmpty(resolvedPorts[0]) &&
            string.IsNullOrWhiteSpace(serialControllers[0].PortName))
        {
            Array.Sort(availablePorts, (left, right) =>
            {
                int priority = GetPortPriority(left).CompareTo(GetPortPriority(right));
                return priority != 0 ? priority : StringComparer.OrdinalIgnoreCase.Compare(left, right);
            });
            foreach (string candidate in availablePorts)
            {
                if (!claimedPorts.Contains(candidate) &&
                    SerialControllerDiscovery.IsLegacyController(serialControllers[0], candidate))
                {
                    resolvedPorts[0] = candidate;
                    break;
                }
            }
        }

        for (int playerIndex = 0; playerIndex < SupportedPlayerCount; playerIndex++)
        {
            if (string.IsNullOrEmpty(resolvedPorts[playerIndex]))
            {
                Debug.LogError($"Could not resolve controller for Player {playerIndex + 1}. " +
                    "Configure its COM port explicitly or make the firmware answer IDENTIFY with DEVICE,<id>.");
            }
        }

        return resolvedPorts;
    }

    private int GetPortPriority(string portName)
    {
        if (serialPortNameHints == null) return 0;
        for (int index = 0; index < serialPortNameHints.Length; index++)
        {
            if (!string.IsNullOrEmpty(serialPortNameHints[index]) &&
                portName.IndexOf(serialPortNameHints[index], StringComparison.OrdinalIgnoreCase) >= 0)
                return index;
        }
        return serialPortNameHints.Length;
    }

    private void EnsureControllerConfigurations()
    {
        if (serialControllers == null || serialControllers.Length != SupportedPlayerCount)
        {
            SerialControllerConfiguration[] previous = serialControllers;
            serialControllers = new SerialControllerConfiguration[SupportedPlayerCount];

            for (int playerIndex = 0; playerIndex < SupportedPlayerCount; playerIndex++)
            {
                SerialControllerConfiguration matchingConfiguration = null;
                if (previous != null)
                {
                    foreach (SerialControllerConfiguration configuration in previous)
                    {
                        if (configuration != null && configuration.PlayerIndex == playerIndex)
                        {
                            matchingConfiguration = configuration;
                            break;
                        }
                    }
                }

                serialControllers[playerIndex] = matchingConfiguration;
            }
        }

        for (int playerIndex = 0; playerIndex < SupportedPlayerCount; playerIndex++)
        {
            serialControllers[playerIndex] ??=
                new SerialControllerConfiguration(playerIndex, $"P{playerIndex + 1}");
            serialControllers[playerIndex].AssignPlayerIndex(playerIndex);
        }
    }

    void UpdateSerialDebugDisplayToggle()
    {
        if (!serialDebugMode || serialDebugToggleKey == Key.None)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[serialDebugToggleKey].wasPressedThisFrame)
        {
            ToggleSerialDebugDisplay();
        }
    }

    private void OnSerialLineProcessed(SerialDriveInputSource source, string status, string line)
    {
        AddSerialDebugLog($"P{source.PlayerIndex + 1} {status}", line);
    }

    void AddSerialDebugLog(string status, string line)
    {
        if (!serialDebugMode)
        {
            return;
        }

        string displayLine = line.Length <= 512 ? line : line.Substring(0, 512) + "...";
        serialDebugLog.Enqueue($"[{Time.realtimeSinceStartup,9:F3}] {status,-7} {displayLine}");

        int capacity = Mathf.Max(1, serialDebugLogCapacity);
        while (serialDebugLog.Count > capacity)
        {
            serialDebugLog.Dequeue();
        }

        if (mirrorSerialInputToConsole)
        {
            Debug.Log($"[InputManager Serial] {status}: {line}", this);
        }
    }

    public void ToggleSerialDebugDisplay()
    {
        serialDebugDisplayVisible = !serialDebugDisplayVisible;
    }

    public void SetSerialDebugDisplayVisible(bool visible)
    {
        serialDebugDisplayVisible = visible;
    }

    void OnGUI()
    {
        if (!IsSerialDebugDisplayVisible)
        {
            return;
        }

        EnsureSerialDebugStyles();

        float width = Mathf.Min(620f, Mathf.Max(100f, Screen.width - 20f));
        float desiredHeight = 340f + serialDebugLogCapacity * 20f;
        float height = Mathf.Min(desiredHeight, Mathf.Max(100f, Screen.height - 20f));
        Rect panelRect = new Rect(10f, 10f, width, height);

        Color previousColor = GUI.color;
        GUI.color = new Color(0.12f, 0.12f, 0.12f, 0.94f);
        GUI.Box(panelRect, GUIContent.none);
        GUI.color = previousColor;

        GUILayout.BeginArea(new Rect(panelRect.x + 12f, panelRect.y + 10f, panelRect.width - 24f, panelRect.height - 20f));
        serialDebugScrollPosition = GUILayout.BeginScrollView(serialDebugScrollPosition);
        GUILayout.Label($"InputManager / Serial Monitor  [{serialDebugToggleKey}: hide]", serialDebugHeaderStyle);
        GUILayout.Label($"Input source: {(isDebugMode ? "Keyboard (serial disabled)" : "Serial")}", serialDebugLabelStyle);
        for (int playerIndex = 0; playerIndex < SupportedPlayerCount; playerIndex++)
        {
            if (!(inputSources[playerIndex] is SerialDriveInputSource source))
            {
                GUILayout.Label($"Player {playerIndex + 1}: serial inactive", serialDebugLabelStyle);
                continue;
            }

            string age = source.LastSerialLineTime < 0f ? "-" :
                $"{Mathf.Max(0f, Time.realtimeSinceStartup - source.LastSerialLineTime):F2} s ago";
            GUILayout.Label($"Player {playerIndex + 1} / {source.DeviceId}: " +
                $"{(source.IsConnected ? "Connected" : source.IsPortOpen ? "Waiting for input" : "Disconnected")}    Port: {source.PortName}", serialDebugLabelStyle);
            GUILayout.Label($"Baud: {serialControllers[playerIndex].BaudRate}    Read timeout: {serialControllers[playerIndex].ReadTimeoutMilliseconds} ms    Queue: {source.PendingLineCount}", serialDebugLabelStyle);
            GUILayout.Label($"Raw lines: {source.LinesReceived}    Processed: {source.LinesProcessed}    Parse errors: {source.ParseErrorCount}", serialDebugLabelStyle);
            GUILayout.Label($"Pedal: {source.CurrentState.pedal:F4}    Handle: {source.CurrentState.steering:F4}    Parse: {source.LastParseResult}", serialDebugLabelStyle);
            string raw = source.LastSerialLine;
            if (raw.Length > 512) raw = raw.Substring(0, 512) + "...";
            GUILayout.Label($"Last input: {age}    Raw: {raw}", serialDebugLabelStyle);
        }
        GUILayout.Space(6f);
        GUILayout.Label("Serial input log", serialDebugHeaderStyle);

        if (serialDebugLog.Count == 0)
        {
            GUILayout.Label("No serial input has been captured yet.", serialDebugLogStyle);
        }
        else
        {
            foreach (string entry in serialDebugLog)
            {
                GUILayout.Label(entry, serialDebugLogStyle);
            }
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void EnsureSerialDebugStyles()
    {
        if (serialDebugHeaderStyle != null)
        {
            return;
        }

        serialDebugHeaderStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        serialDebugLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            wordWrap = true,
            normal = { textColor = Color.white }
        };
        serialDebugLogStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            wordWrap = true,
            richText = false,
            normal = { textColor = new Color(0.75f, 0.95f, 1f) }
        };
    }

    private static bool IsValidPlayerIndex(int playerIndex)
    {
        return playerIndex >= 0 && playerIndex < SupportedPlayerCount;
    }

    private void DisposeInputSources()
    {
        for (int index = 0; index < inputSources.Length; index++)
        {
            if (inputSources[index] is SerialDriveInputSource serialSource)
            {
                serialSource.LineProcessed -= OnSerialLineProcessed;
            }
            inputSources[index]?.Dispose();
            inputSources[index] = null;
        }

        initialized = false;
        handle = 0f;
        peddale = 0f;
    }

    private void OnApplicationQuit()
    {
        DisposeInputSources();
    }

    private void OnDestroy()
    {
        DisposeInputSources();
    }
}
