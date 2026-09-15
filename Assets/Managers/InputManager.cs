using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    public const int SupportedPlayerCount = 2;

    [Header("Input Mode")]
    [Tooltip("Use two keyboard layouts instead of the ESP32 USB serial input.")]
    public bool isDebugMode = false;

    [Header("ESP32 USB Serial (P1 + P2)")]
    [SerializeField] private Esp32SerialConfiguration esp32Serial = new Esp32SerialConfiguration();

    // Kept for the title/result flow, but not user-editable input settings.
    [HideInInspector]
    public float handle;
    [HideInInspector]
    public float peddale;

    [Header("Serial Monitor")]
    [Tooltip("受信したUSBシリアル行とP1/P2の状態を表示します。F8で表示を切り替えます。")]
    [SerializeField] private bool serialDebugMode = false;
    private bool serialDebugDisplayVisible = true;
    private const Key SerialDebugToggleKey = Key.F8;
    private const int SerialDebugLogCapacity = 12;

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
    private TwoPlayerSerialInputSource sharedSerialInputSource;
    private bool initialized;

    public void Init()
    {
        DisposeInputSources();
        esp32Serial ??= new Esp32SerialConfiguration();

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

        // One microcontroller sends both players in a single four-column frame.
        sharedSerialInputSource = new TwoPlayerSerialInputSource(
            esp32Serial, ResolveSharedSerialPort());
        sharedSerialInputSource.LineProcessed += OnSharedSerialLineProcessed;
        for (int playerIndex = 0; playerIndex < SupportedPlayerCount; playerIndex++)
        {
            inputSources[playerIndex] = new SharedPlayerDriveInputSource(
                sharedSerialInputSource, playerIndex);
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

        if (isDebugMode)
        {
            foreach (IDriveInputSource source in inputSources) source?.UpdateInput(deltaTime);
        }
        else
        {
            sharedSerialInputSource?.UpdateInput(deltaTime);
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
        if (!isDebugMode && sharedSerialInputSource != null)
        {
            return sharedSerialInputSource.GetInputState(playerIndex);
        }

        IDriveInputSource source = GetPlayerInputSource(playerIndex);
        return source != null ? source.CurrentState : DriveInputState.Neutral;
    }

    public bool IsPlayerConnected(int playerIndex)
    {
        if (!isDebugMode && sharedSerialInputSource != null)
        {
            return sharedSerialInputSource.IsConnected(playerIndex);
        }

        IDriveInputSource source = GetPlayerInputSource(playerIndex);
        return source != null && source.IsConnected;
    }

    private string ResolveSharedSerialPort()
    {
        string configuredPort = esp32Serial.PortName?.Trim();
        if (!string.IsNullOrEmpty(configuredPort)) return configuredPort;

        string[] availablePorts;
        try
        {
            availablePorts = SerialPort.GetPortNames();
        }
        catch (Exception exception)
        {
            Debug.LogError($"Failed to enumerate serial ports: {exception.Message}");
            return null;
        }

        List<string> usbPorts = new List<string>();
        foreach (string port in availablePorts)
        {
            // Mono on macOS also returns hundreds of pseudo-terminals. Use USB ports only.
            if (!IsUsbSerialPort(port)) continue;

            if (port.StartsWith("/dev/tty.", StringComparison.Ordinal))
            {
                string calloutPort = "/dev/cu." + port.Substring("/dev/tty.".Length);
                if (File.Exists(calloutPort)) usbPorts.Add(calloutPort);
            }
            usbPorts.Add(port);
        }

        usbPorts.Sort((left, right) =>
        {
            // The installed ESP32 uses a Silicon Labs CP2102 USB bridge.
            int priority = GetPortPriority(left).CompareTo(GetPortPriority(right));
            return priority != 0 ? priority : StringComparer.OrdinalIgnoreCase.Compare(left, right);
        });

        if (usbPorts.Count > 0)
        {
            Debug.Log($"Selected ESP32 USB serial port: {usbPorts[0]}");
            return usbPorts[0];
        }

        Debug.LogError("No ESP32 USB serial port found. Connect the ESP32 or enter its Port Name in ESP32 USB Serial.");
        return null;
    }

    private static bool IsUsbSerialPort(string portName)
    {
        return portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
            portName.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0 ||
            portName.IndexOf("SLAB_USBtoUART", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static int GetPortPriority(string portName)
    {
        if (portName.IndexOf("SLAB_USBtoUART", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
        if (portName.StartsWith("/dev/cu.", StringComparison.OrdinalIgnoreCase)) return 1;
        return 2;
    }

    void UpdateSerialDebugDisplayToggle()
    {
        if (!serialDebugMode)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[SerialDebugToggleKey].wasPressedThisFrame)
        {
            ToggleSerialDebugDisplay();
        }
    }

    private void OnSharedSerialLineProcessed(string status, string line)
    {
        AddSerialDebugLog($"P1/P2 {status}", line);
    }

    void AddSerialDebugLog(string status, string line)
    {
        if (!serialDebugMode)
        {
            return;
        }

        string displayLine = line.Length <= 512 ? line : line.Substring(0, 512) + "...";
        serialDebugLog.Enqueue($"[{Time.realtimeSinceStartup,9:F3}] {status,-7} {displayLine}");

        int capacity = SerialDebugLogCapacity;
        while (serialDebugLog.Count > capacity)
        {
            serialDebugLog.Dequeue();
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
        float desiredHeight = 340f + SerialDebugLogCapacity * 20f;
        float height = Mathf.Min(desiredHeight, Mathf.Max(100f, Screen.height - 20f));
        Rect panelRect = new Rect(10f, 10f, width, height);

        Color previousColor = GUI.color;
        GUI.color = new Color(0.12f, 0.12f, 0.12f, 0.94f);
        GUI.Box(panelRect, GUIContent.none);
        GUI.color = previousColor;

        GUILayout.BeginArea(new Rect(panelRect.x + 12f, panelRect.y + 10f, panelRect.width - 24f, panelRect.height - 20f));
        serialDebugScrollPosition = GUILayout.BeginScrollView(serialDebugScrollPosition);
        GUILayout.Label($"InputManager / Serial Monitor  [{SerialDebugToggleKey}: hide]", serialDebugHeaderStyle);
        GUILayout.Label($"Input source: {(isDebugMode ? "Keyboard (serial disabled)" : "Serial")}", serialDebugLabelStyle);
        if (!isDebugMode && sharedSerialInputSource != null)
        {
            string age = sharedSerialInputSource.LastSerialLineTime < 0f ? "-" :
                $"{Mathf.Max(0f, Time.realtimeSinceStartup - sharedSerialInputSource.LastSerialLineTime):F2} s ago";
            GUILayout.Label($"Shared controller: {(sharedSerialInputSource.IsPortOpen ? "Connected" : "Disconnected")}    Port: {sharedSerialInputSource.PortName}", serialDebugLabelStyle);
            GUILayout.Label($"Raw lines: {sharedSerialInputSource.LinesReceived}    Processed: {sharedSerialInputSource.LinesProcessed}    Parse errors: {sharedSerialInputSource.ParseErrorCount}", serialDebugLabelStyle);
            GUILayout.Label($"Last input: {age}    Raw: {sharedSerialInputSource.LastSerialLine}    Parse: {sharedSerialInputSource.LastParseResult}", serialDebugLabelStyle);
        }
        for (int playerIndex = 0; playerIndex < SupportedPlayerCount; playerIndex++)
        {
            if (sharedSerialInputSource == null)
            {
                GUILayout.Label($"Player {playerIndex + 1}: serial inactive", serialDebugLabelStyle);
                continue;
            }

            DriveInputState state = GetInputState(playerIndex);
            GUILayout.Label($"Player {playerIndex + 1}: {(sharedSerialInputSource.IsConnected(playerIndex) ? "Connected" : "Waiting for input")}", serialDebugLabelStyle);
            GUILayout.Label($"Pedal: {state.pedal:F4}    Handle: {state.steering:F4}", serialDebugLabelStyle);
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
        if (sharedSerialInputSource != null)
        {
            sharedSerialInputSource.LineProcessed -= OnSharedSerialLineProcessed;
            sharedSerialInputSource.Dispose();
            sharedSerialInputSource = null;
        }

        for (int index = 0; index < inputSources.Length; index++)
        {
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
