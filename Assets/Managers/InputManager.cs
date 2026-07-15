using System;
using System.Collections.Concurrent;
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
    public bool isDebugMode = false;

    public float handle;
    public float peddale;
    SerialPort serialPort;
    Thread serialReadThread;
    ConcurrentQueue<string> serialQueue = new ConcurrentQueue<string>();
    readonly int serialBaudRate = 115200;
    readonly int serialReadTimeoutMs = 200;
    readonly string[] serialPortNameHints = new string[]
    {
        "usbserial",
        "usbmodem",
        "ttyACM",
        "ttyUSB",
        "COM"
    };

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
        // テスト用入力処理 temporary input handling for testing
        if (isDebugMode)
        {
            if (Keyboard.current.wKey.isPressed)
            {
                peddale = 1.0f;
            }
            else
            {
                peddale = 0.0f;
            }
            //if (Keyboard.current.sKey.isPressed) p--;
            //peddale += p * 0.3f * dt;
            //if (peddale < 0) peddale = 0;

            handle = 0;
            if (Keyboard.current.dKey.isPressed) handle = 10f;
            if (Keyboard.current.aKey.isPressed) handle = -10f;
        }
        else
        {
            // ESP32からのシリアル通信の処理
            if (serialPort != null && serialPort.IsOpen)
            {
                // キューに溜まった行を全て処理
                while (serialQueue.TryDequeue(out string line))
                {
                    try
                    {
                        string[] parts1 = line.Split("||");
                        string[] parts = parts1[0].Split(',');
                        if (parts.Length >= 2)
                        {
                            if (float.TryParse(parts[0], out float peddalValue))
                            {
                                peddale = peddalValue / 2;
                            }

                            if (float.TryParse(parts[1], out float handleValue))
                            {
                                handle = handleValue / 3;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Failed to parse serial line '{line}': {e.Message}");
                    }
                }
            }
        }
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
                    Debug.Log(line);
                    if (!string.IsNullOrEmpty(line)) serialQueue.Enqueue(line.Trim());
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