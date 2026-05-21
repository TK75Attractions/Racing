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
            serialPort = new SerialPort("/dev/tty.usbserial-10", 115200); // ポート名とレートは都度変更
            serialPort.NewLine = "\n";
            serialPort.ReadTimeout = 200; // Updateループにブロッキングさせないため短めに設定
            serialPort.Open();
            Debug.Log("Serial port opened successfully.");

            // 背景スレッドで継続的にReadLineしてキューに積む
            serialReadThread = new Thread(SerialReadLoop) { IsBackground = true };
            serialReadThread.Start();
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to open serial port: {e.Message}");
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
            if (Keyboard.current.dKey.isPressed) handle = 30f;
            if (Keyboard.current.aKey.isPressed) handle = -30f;
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
                        string[] parts = line.Split(',');
                        if (parts.Length >= 2)
                        {
                            /*
                            if (float.TryParse(parts[0], out float peddalValue))
                            {
                                peddale = peddalValue;
                            }
                            */
                            peddale = Keyboard.current.wKey.isPressed ? 1.0f : 0.0f; // デバッグ用の暫定処理

                            if (float.TryParse(parts[1], out float handleValue))
                            {
                                handle = handleValue;
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