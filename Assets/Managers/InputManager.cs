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

    [Tooltip("P1 uses only its ESP32 wheel, with W/S pedal by default or Constant pedal from F8. " +
        "P2 uses the keyboard. No ESP32 pedals or P2 wheel are required. Takes priority over keyboard-only mode.")]
    public bool isP1SerialP2KeyboardDebugMode = false;

    [Header("P1 Steering Test (ESP32 wheel only)")]
    [Tooltip("P1のハンドルはESP32、ペダルはW/Sまたはレース中の一定入力。F8から走行中にも変更できます。")]
    [SerializeField] private P1SteeringTestMode p1SteeringTestMode = P1SteeringTestMode.Normal;
    [SerializeField, Range(0f, 1f)] private float p1ConstantPedal = 1f;
    public P1SteeringTestMode SteeringTestMode => p1SteeringTestMode;
    public float ConstantTestPedal => p1ConstantPedal;
    public bool ConstantTestPedalPaused => steeringTestSource?.ConstantPedalPaused == true;
    private P1SteeringTestInputSource steeringTestSource;

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
    public bool IsAnyDebugMode => p1SteeringTestMode != P1SteeringTestMode.Normal ||
        (!backgroundBeforeSimulator.HasValue && (isDebugMode || isP1SerialP2KeyboardDebugMode));
    public TwoPlayerSerialInputSource SharedSerialInputSource => sharedSerialInputSource;
    public string InputModeLabel => GetInputModeLabel();
    public event Action<string, string> SerialLineProcessed;

    private readonly IDriveInputSource[] inputSources =
        new IDriveInputSource[SupportedPlayerCount];
    private TwoPlayerSerialInputSource sharedSerialInputSource;
    // Cars and tutorials keep these handles across runtime reconfiguration.
    private readonly LiveDriveInputSource[] liveInputs = {
        new LiveDriveInputSource(0), new LiveDriveInputSource(1)
    };
    private readonly DashboardInputLease[] dashboardInputs = { new DashboardInputLease(), new DashboardInputLease() };
    private readonly DashboardDriveInputSource[] dashboardSources = new DashboardDriveInputSource[SupportedPlayerCount];
    public DashboardInputLease GetDashboardInput(int player) => IsValidPlayerIndex(player) ? dashboardInputs[player] : null;
    public void ClearDashboardInputs() { foreach (DashboardInputLease lease in dashboardInputs) lease.Clear(); }
    private bool appliedDebugMode, appliedMixedMode;
    private bool initialized, hasInitialized;
    private bool? backgroundBeforeSimulator;

    public void Init()
    {
        DisposeInputSources();
        esp32Serial ??= new Esp32SerialConfiguration();

        if (RacingStandalonePerformanceProbe.AutorunEnabled)
        {
            for (int i = 0; i < SupportedPlayerCount; i++) inputSources[i] = new KeyboardDriveInputSource(i);
            initialized = true;
            return;
        }

        if (SerialSimulatorSession.TryGetConfiguration(out string simulatorPort, out float simulatorDivisor))
        {
            backgroundBeforeSimulator = Application.runInBackground;
            Application.runInBackground = true;
            sharedSerialInputSource = new TwoPlayerSerialInputSource(
                esp32Serial.WithSimulatorPort(simulatorPort, simulatorDivisor), simulatorPort, isP1SerialP2KeyboardDebugMode);
            sharedSerialInputSource.LineProcessed += OnSharedSerialLineProcessed;
            for (int playerIndex = 0; playerIndex < SupportedPlayerCount; playerIndex++)
                inputSources[playerIndex] = playerIndex == 1 && isP1SerialP2KeyboardDebugMode
                    ? (IDriveInputSource)new KeyboardDriveInputSource(1)
                    : new SharedPlayerDriveInputSource(sharedSerialInputSource, playerIndex);
            CompleteInputInitialization();
            Debug.Log($"ESP32 simulator: {simulatorPort}, steering divisor {simulatorDivisor}. " +
                (isP1SerialP2KeyboardDebugMode ? "P1 serial / P2 keyboard." : "Both players use the PTY stream."));
            return;
        }

        if (isP1SerialP2KeyboardDebugMode)
        {
            InitializeSharedSerialInput(playerOneOnly: true);
            inputSources[0] = new SharedPlayerDriveInputSource(sharedSerialInputSource, 0);
            inputSources[1] = new KeyboardDriveInputSource(1);

            CompleteInputInitialization();
            Debug.Log(
                "InputManager is in P1 serial / P2 keyboard debug mode. " +
                "Player 1 uses ESP32 USB serial and Player 2 uses arrow keys / Slash (or Right Ctrl) / Right Shift.");
            return;
        }

        if (isDebugMode)
        {
            for (int playerIndex = 0; playerIndex < SupportedPlayerCount; playerIndex++)
            {
                inputSources[playerIndex] = new KeyboardDriveInputSource(playerIndex);
            }

            CompleteInputInitialization();
            Debug.Log(
                "InputManager is in debug mode. " +
                "Player 1 uses WASD/Space/Enter and Player 2 uses arrow keys / Slash (or Right Ctrl) / Right Shift.");
            return;
        }

        // One microcontroller sends both players in a single RGP/1 TEL frame.
        InitializeSharedSerialInput(playerOneOnly: false);
        for (int playerIndex = 0; playerIndex < SupportedPlayerCount; playerIndex++)
        {
            inputSources[playerIndex] = new SharedPlayerDriveInputSource(
                sharedSerialInputSource, playerIndex);
        }

        CompleteInputInitialization();
    }

    public string InputMode => isP1SerialP2KeyboardDebugMode ? "mixed" : isDebugMode ? "keyboard" : "serial";

    public bool SetInputMode(string mode)
    {
        if (mode != "serial" && mode != "keyboard" && mode != "mixed") return false;
        isDebugMode = mode == "keyboard";
        isP1SerialP2KeyboardDebugMode = mode == "mixed";
        if (!initialized || appliedDebugMode != isDebugMode || appliedMixedMode != isP1SerialP2KeyboardDebugMode)
        {
            p1SteeringTestMode = P1SteeringTestMode.Normal;
            Init();
        }
        return true;
    }

    private void CompleteInputInitialization()
    {
        if (isP1SerialP2KeyboardDebugMode && p1SteeringTestMode == P1SteeringTestMode.Normal)
            p1SteeringTestMode = P1SteeringTestMode.KeyboardPedal;
        IDriveInputSource normal = inputSources[0];
        steeringTestSource = new P1SteeringTestInputSource(normal, normal as KeyboardDriveInputSource ?? new KeyboardDriveInputSource(0));
        inputSources[0] = steeringTestSource;
        initialized = true;
        ApplySteeringTestSettings();
        appliedDebugMode = isDebugMode;
        appliedMixedMode = isP1SerialP2KeyboardDebugMode;
        // Sample held keys before publishing a replacement: default neutral state is not a physical release.
        foreach (IDriveInputSource source in inputSources) source?.UpdateInput(0f);
        for (int i = 0; i < SupportedPlayerCount; i++)
        {
            dashboardSources[i] = new DashboardDriveInputSource(inputSources[i], dashboardInputs[i], () => Time.realtimeSinceStartupAsDouble);
            liveInputs[i].SwitchSource(dashboardSources[i], hasInitialized);
        }
        hasInitialized = true;
    }

    public void SetP1SteeringTestMode(P1SteeringTestMode mode)
    {
        if (mode < P1SteeringTestMode.Normal || mode > P1SteeringTestMode.ConstantPedal) return;
        if (isP1SerialP2KeyboardDebugMode && mode == P1SteeringTestMode.Normal) mode = P1SteeringTestMode.KeyboardPedal;
        p1SteeringTestMode = mode;
        ApplySteeringTestSettings();
        if (mode == P1SteeringTestMode.ConstantPedal) ResumeP1ConstantPedal();
    }

    public void PauseP1ConstantPedal() => steeringTestSource?.PauseConstantPedal();
    public void ResumeP1ConstantPedal()
    {
        if (p1SteeringTestMode == P1SteeringTestMode.ConstantPedal && steeringTestSource?.IsConnected == true)
            steeringTestSource.RequestConstantResume();
    }

    public void SetP1ConstantPedal(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return;
        p1ConstantPedal = Mathf.Clamp01(value);
        ApplySteeringTestSettings();
    }

    private void ApplySteeringTestSettings()
    {
        if (!initialized || steeringTestSource == null) return;
        if (p1SteeringTestMode != P1SteeringTestMode.Normal && sharedSerialInputSource == null)
            InitializeSharedSerialInput(playerOneOnly: true);
        if (sharedSerialInputSource != null && steeringTestSource.Serial == null)
            steeringTestSource.Serial = new SharedPlayerDriveInputSource(sharedSerialInputSource, 0);
        sharedSerialInputSource?.SetP1SteeringOnly(p1SteeringTestMode != P1SteeringTestMode.Normal);
        bool changed = steeringTestSource.Mode != p1SteeringTestMode;
        steeringTestSource.Mode = p1SteeringTestMode;
        steeringTestSource.ConstantPedal = p1ConstantPedal;
        Gmanager race = Gmanager.Control;
        steeringTestSource.IsRacing = race != null && race.state == Gmanager.State.Game && race.GetParticipant(0)?.IsRacing == true;
        if (changed && race != null)
        {
            // A live change keeps the existing gate; constant mode supplies an acknowledged neutral sample.
            GameObject car = race.GetParticipant(0)?.Car ?? race.GetComponent<DrivingTutorialController>()?.GetPlayerCar(0);
            if (car?.GetComponent<DebugMover>()?.InputSource is PlayerDriveInputGate gate) gate.RequirePedalRelease("input_mode_changed");
        }
    }

    public void UpdateInput(float deltaTime)
    {
        if (initialized && (appliedDebugMode != isDebugMode || appliedMixedMode != isP1SerialP2KeyboardDebugMode))
            SetInputMode(InputMode);
        UpdateSerialDebugDisplayToggle();
        if (!initialized)
        {
            return;
        }

        ApplySteeringTestSettings();
        sharedSerialInputSource?.UpdateInput(deltaTime);
        foreach (IDriveInputSource source in inputSources) source?.UpdateInput(deltaTime);
        foreach (DashboardDriveInputSource source in dashboardSources) source?.UpdateInput(deltaTime);

        DriveInputState playerOneState = GetInputState(0);
        handle = playerOneState.steering;
        peddale = playerOneState.pedal;
    }

    public IDriveInputSource GetPlayerInputSource(int playerIndex)
    {
        return IsValidPlayerIndex(playerIndex) ? liveInputs[playerIndex] : null;
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

    private void InitializeSharedSerialInput(bool playerOneOnly)
    {
        sharedSerialInputSource = new TwoPlayerSerialInputSource(
            esp32Serial, ResolveSharedSerialPort(), playerOneOnly);
        sharedSerialInputSource.LineProcessed += OnSharedSerialLineProcessed;
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
        SerialLineProcessed?.Invoke(status, line);
        string players = sharedSerialInputSource != null && backgroundBeforeSimulator.HasValue ? "P1/P2" :
            isP1SerialP2KeyboardDebugMode || (isDebugMode && p1SteeringTestMode != P1SteeringTestMode.Normal) ? "P1" : "P1/P2";
        AddSerialDebugLog($"{players} {status}", line);
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
        if (Gmanager.Control?.IsMaintenanceActive == true || !IsSerialDebugDisplayVisible)
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
        GUILayout.Label($"Input source: {GetInputModeLabel()}", serialDebugLabelStyle);
        if (sharedSerialInputSource != null)
        {
            string age = sharedSerialInputSource.LastSerialLineTime < 0f ? "-" :
                $"{Mathf.Max(0f, Time.realtimeSinceStartup - sharedSerialInputSource.LastSerialLineTime):F2} s ago";
            GUILayout.Label($"Shared controller: {(sharedSerialInputSource.IsPortOpen ? "Connected" : "Disconnected")}    Port: {sharedSerialInputSource.PortName}", serialDebugLabelStyle);
            GUILayout.Label($"Raw lines: {sharedSerialInputSource.LinesReceived}    Processed: {sharedSerialInputSource.LinesProcessed}    Parse errors: {sharedSerialInputSource.ParseErrorCount}", serialDebugLabelStyle);
            GUILayout.Label($"State: {sharedSerialInputSource.Session.State}    First usable input: {sharedSerialInputSource.FirstUsableInputMilliseconds:F0} ms (-1 = waiting)", serialDebugLabelStyle);
            GUILayout.Label($"Last input: {age}    Raw: {sharedSerialInputSource.LastSerialLine}    Parse: {sharedSerialInputSource.LastParseResult}", serialDebugLabelStyle);
        }
        for (int playerIndex = 0; playerIndex < SupportedPlayerCount; playerIndex++)
        {
            DriveInputState state = GetInputState(playerIndex);
            IDriveInputSource source = GetPlayerInputSource(playerIndex);
            string device = source?.DeviceId ?? "INACTIVE";
            GUILayout.Label($"Player {playerIndex + 1} ({device}): {(IsPlayerConnected(playerIndex) ? "Connected" : "Waiting for input")}", serialDebugLabelStyle);
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

    private string GetInputModeLabel()
    {
        if (p1SteeringTestMode != P1SteeringTestMode.Normal)
        {
            string wheel = backgroundBeforeSimulator.HasValue ? "Simulator" : "ESP32";
            string pedal = p1SteeringTestMode == P1SteeringTestMode.KeyboardPedal ? "W/S pedal" : $"constant {p1ConstantPedal:P0} (race only; S releases)";
            string p2 = backgroundBeforeSimulator.HasValue || (!isDebugMode && !isP1SerialP2KeyboardDebugMode) ? "Serial" : "Keyboard";
            return $"P1 {wheel} wheel + {pedal} / P2 {p2} (debug)";
        }
        if (backgroundBeforeSimulator.HasValue) return "Serial Simulator P1/P2 (macOS PTY)";
        if (isP1SerialP2KeyboardDebugMode) return "P1 Serial / P2 Keyboard (debug)";
        return isDebugMode ? (sharedSerialInputSource != null ? "Keyboard P1/P2 (debug; ESP32 standby)" :
            "Keyboard P1/P2 (debug; serial disabled)") : "Serial P1/P2";
    }

    private void DisposeInputSources()
    {
        ClearDashboardInputs();
        Array.Clear(dashboardSources, 0, dashboardSources.Length);
        foreach (LiveDriveInputSource handle in liveInputs) handle.SwitchSource(null, true);
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

        steeringTestSource = null;

        initialized = false;
        if (backgroundBeforeSimulator.HasValue)
        {
            Application.runInBackground = backgroundBeforeSimulator.Value;
            backgroundBeforeSimulator = null;
        }
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
