using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.XR;

public class InputManager : MonoBehaviour
{
    // Serialized fields: Unityでインスペクターから設定可能な変数。
    // [SerializeField]がついていないがpublicなのでインスペクタに表示されるらしい

    // isDebugMode: デバッグモードのフラグ。trueの場合、キーボード入力を使用してhandleとpeddaleの値を更新する。
    [Header("Input Mode")]
    public bool isDebugMode = false;

    public float handle;
    public float peddale;

    [Header("Serial Settings")]
    [SerializeField, Min(1)] private int serialBaudRate = 115200;
    [SerializeField, Min(1)] private int serialReadTimeoutMs = 200;
    [SerializeField] private string[] serialPortNameHints = new string[]
    {
        "usbserial",
        "usbmodem",
        "ttyACM",
        "ttyUSB",
        "COM"
    };

    [Header("Serial Debug Monitor")]
    [Tooltip("シリアル入力の状態と受信履歴を画面に表示できるようにします。キーボード入力モードとは独立しています。")]
    [SerializeField] private bool serialDebugMode = false;
    [Tooltip("デバッグモニターを起動時から表示します。実行中は指定キーで切り替えられます。")]
    [SerializeField] private bool serialDebugDisplayVisible = true;
    [SerializeField] private Key serialDebugToggleKey = Key.F8;
    [SerializeField, Range(1, 50)] private int serialDebugLogCapacity = 12;
    [Tooltip("画面表示に加えて、受信したシリアル行をUnity Consoleにも出力します。")]
    [SerializeField] private bool mirrorSerialInputToConsole = false;

    SerialPort serialPort;
    Thread serialReadThread;
    readonly ConcurrentQueue<string> serialQueue = new ConcurrentQueue<string>();
    readonly Queue<string> serialDebugLog = new Queue<string>();

    string connectedSerialPortName = "-";
    string lastSerialLine = "-";
    string lastParseResult = "Waiting for input";
    float lastSerialLineTime = -1f;
    long serialLinesReceived;
    int serialLinesProcessed;
    int serialParseErrorCount;
    Vector2 serialDebugScrollPosition;
    GUIStyle serialDebugHeaderStyle;
    GUIStyle serialDebugLabelStyle;
    GUIStyle serialDebugLogStyle;

    public bool SerialDebugMode
    {
        get => serialDebugMode;
        set => serialDebugMode = value;
    }

    public bool IsSerialDebugDisplayVisible => serialDebugMode && serialDebugDisplayVisible;

    // initializer 用途？
    public void Init()
    {
        if (isDebugMode)
        {
            Debug.Log("InputManager is in debug mode. Using keyboard input.");
            return;
        }
        // ESP32からのシリアル通信の初期化
        try
        {
            string portName = FindAvailableSerialPort();
            if (string.IsNullOrEmpty(portName))
            {
                Debug.LogError("No matching serial port was found.");
                return;
            }

            serialPort = new SerialPort(portName, serialBaudRate);
            serialPort.NewLine = "\n";
            serialPort.ReadTimeout = serialReadTimeoutMs; // Updateループにブロッキングさせないため短めに設定
            serialPort.Open();
            connectedSerialPortName = portName;
            Debug.Log($"Serial port opened successfully: {portName}");

            // 背景スレッドで継続的にReadLineしてキューに積む
            serialReadThread = new Thread(SerialReadLoop) { IsBackground = true };
            serialReadThread.Start();
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to open serial port: {e.Message}");
        }
    }

    string FindAvailableSerialPort()
    {
        string[] portNames = SerialPort.GetPortNames();
        if (portNames == null || portNames.Length == 0)
        {
            return null;
        }

        Array.Sort(portNames, (left, right) =>
        {
            int leftScore = GetPortPriority(left);
            int rightScore = GetPortPriority(right);
            int compare = leftScore.CompareTo(rightScore);
            if (compare != 0)
            {
                return compare;
            }

            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        });

        foreach (string portName in portNames)
        {
            if (TryProbeSerialPort(portName))
            {
                return portName;
            }
        }

        return null;
    }

    int GetPortPriority(string portName)
    {
        if (string.IsNullOrEmpty(portName))
        {
            return int.MaxValue;
        }

        for (int index = 0; index < serialPortNameHints.Length; index++)
        {
            if (portName.IndexOf(serialPortNameHints[index], StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return index;
            }
        }

        return serialPortNameHints.Length;
    }

    bool TryProbeSerialPort(string portName)
    {
        Debug.Log($"Probing serial port: {portName}");
        SerialPort probePort = null;
        try
        {
            probePort = new SerialPort(portName, serialBaudRate);
            probePort.NewLine = "\n";
            probePort.ReadTimeout = serialReadTimeoutMs;
            probePort.Open();

            if (!probePort.IsOpen)
            {
                Debug.LogWarning($"Failed to open serial port for probing: {portName}");
                return false;
            }

            string line = probePort.ReadLine().Trim();
            // if (IsExpectedSerialLine(line))
            if (true)
            {
                Debug.Log($"Detected serial port candidate: {portName}");
                return true;
            }

            Debug.LogWarning($"Serial port {portName} did not return expected data during probing. Received: '{line}'");
            return false;
        }
        catch (TimeoutException)
        {
            Debug.LogWarning($"Timeout occurred while probing serial port: {portName}");
            return false;
        }
        catch (Exception e)
        {
            Debug.LogError($"Unexpected error occurred while probing serial port: {portName} - {e.Message}");
            return false;
        }
        finally
        {
            if (probePort != null)
            {
                try
                {
                    if (probePort.IsOpen)
                    {
                        probePort.Close();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error occurred while closing serial port: {portName} - {e.Message}");
                }
            }
        }
    }

    // Updateは毎フレーム呼び出される(dt: delta time、前のフレームからの経過時間)
    // 現在: キーボード入力を処理して、peddaleとhandleの値を更新する
    // ToDo: ESP32からのInput処理(ハンドルのIMU、ペダルのADC値)を追加する
    public void UpdateInput(float dt)
    {
        UpdateSerialDebugDisplayToggle();

        // テスト用入力処理 temporary input handling for testing
        if (isDebugMode)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                peddale = 0f;
                handle = 0f;
                return;
            }

            if (keyboard.wKey.isPressed)
            {
                peddale = 1.0f;
            }
            else if (keyboard.sKey.isPressed)
            {
                peddale = -1.0f;
            }
            else
            {
                peddale = 0.0f;
            }

            handle = 0;
            if (keyboard.dKey.isPressed) handle = 10f;
            if (keyboard.aKey.isPressed) handle = -10f;
        }
        else
        {
            // ESP32からのシリアル通信の処理
            if (serialPort != null && serialPort.IsOpen)
            {
                // キューに溜まった行を全て処理
                while (serialQueue.TryDequeue(out string line))
                {
                    ProcessSerialLine(line);
                }
            }
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

    void ProcessSerialLine(string line)
    {
        serialLinesProcessed++;
        lastSerialLine = line;
        lastSerialLineTime = Time.realtimeSinceStartup;

        bool pedalParsed = false;
        bool handleParsed = false;

        try
        {
            int metadataSeparatorIndex = line.IndexOf("||", StringComparison.Ordinal);
            string inputValues = metadataSeparatorIndex >= 0 ? line.Substring(0, metadataSeparatorIndex) : line;
            string[] parts = inputValues.Split(',');

            if (parts.Length >= 2)
            {
                pedalParsed = TryParseSerialFloat(parts[0], out float pedalValue);
                if (pedalParsed)
                {
                    peddale = pedalValue;
                }

                handleParsed = TryParseSerialFloat(parts[1], out float handleValue);
                if (handleParsed)
                {
                    handle = handleValue / 3f;
                }
            }
        }
        catch (Exception e)
        {
            serialParseErrorCount++;
            lastParseResult = $"Error: {e.Message}";
            AddSerialDebugLog("ERROR", line);
            Debug.LogError($"Failed to parse serial line '{line}': {e.Message}");
            return;
        }

        if (pedalParsed && handleParsed)
        {
            lastParseResult = "OK";
            AddSerialDebugLog("OK", line);
        }
        else
        {
            serialParseErrorCount++;
            lastParseResult = pedalParsed || handleParsed ? "Partial" : "Invalid";
            AddSerialDebugLog(lastParseResult.ToUpperInvariant(), line);
        }
    }

    static bool TryParseSerialFloat(string value, out float result)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
            || float.TryParse(value, out result);
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
        float desiredHeight = 225f + serialDebugLogCapacity * 20f;
        float height = Mathf.Min(desiredHeight, Mathf.Max(100f, Screen.height - 20f));
        Rect panelRect = new Rect(10f, 10f, width, height);

        Color previousColor = GUI.color;
        GUI.color = new Color(0.12f, 0.12f, 0.12f, 0.94f);
        GUI.Box(panelRect, GUIContent.none);
        GUI.color = previousColor;

        GUILayout.BeginArea(new Rect(panelRect.x + 12f, panelRect.y + 10f, panelRect.width - 24f, panelRect.height - 20f));
        GUILayout.Label($"InputManager / Serial Monitor  [{serialDebugToggleKey}: hide]", serialDebugHeaderStyle);
        GUILayout.Label($"Input source: {(isDebugMode ? "Keyboard (serial disabled)" : "Serial")}", serialDebugLabelStyle);
        GUILayout.Label($"Connection: {GetSerialConnectionState()}    Port: {connectedSerialPortName}", serialDebugLabelStyle);
        GUILayout.Label($"Baud: {serialBaudRate}    Read timeout: {serialReadTimeoutMs} ms    Queue: {serialQueue.Count}", serialDebugLabelStyle);
        GUILayout.Label($"Raw lines: {Interlocked.Read(ref serialLinesReceived)}    Processed: {serialLinesProcessed}    Parse errors: {serialParseErrorCount}", serialDebugLabelStyle);
        GUILayout.Label($"Pedal: {peddale:F4}    Handle: {handle:F4}    Parse: {lastParseResult}", serialDebugLabelStyle);
        GUILayout.Label($"Last input: {GetLastInputAgeText()}    Raw: {lastSerialLine}", serialDebugLabelStyle);
        GUILayout.Space(6f);
        GUILayout.Label("Serial input log", serialDebugHeaderStyle);

        serialDebugScrollPosition = GUILayout.BeginScrollView(serialDebugScrollPosition);
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

    string GetSerialConnectionState()
    {
        if (isDebugMode)
        {
            return "Disabled by keyboard debug mode";
        }

        try
        {
            return serialPort != null && serialPort.IsOpen ? "Connected" : "Disconnected";
        }
        catch
        {
            return "Disconnected";
        }
    }

    string GetLastInputAgeText()
    {
        if (lastSerialLineTime < 0f)
        {
            return "-";
        }

        return $"{Mathf.Max(0f, Time.realtimeSinceStartup - lastSerialLineTime):F2} s ago";
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

    bool IsExpectedSerialLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        string[] parts = line.Split(',');
        if (parts.Length < 2)
        {
            return false;
        }

        return float.TryParse(parts[0], out _) || float.TryParse(parts[1], out _);
    }

    void SerialReadLoop()
    {
        try
        {
            while (serialPort != null && serialPort.IsOpen)
            {
                try
                {
                    string line = serialPort.ReadLine();
                    // Debug.Log(line);
                    if (!string.IsNullOrEmpty(line))
                    {
                        serialQueue.Enqueue(line.Trim());
                        Interlocked.Increment(ref serialLinesReceived);
                    }
                }
                catch (System.TimeoutException)
                {
                    // タイムアウトは無視してループを継続（非ブロッキング）
                }
                catch (Exception e)
                {
                    Debug.LogError($"Serial read loop error: {e.Message}");
                    break;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"SerialReadLoop fatal error: {e.Message}");
        }
    }

    void OnDestroy()
    {
        try
        {
            if (serialPort != null)
            {
                try { serialPort.Close(); } catch { }
                serialPort = null;
            }
        }
        catch { }
    }
}
