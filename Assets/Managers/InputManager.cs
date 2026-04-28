using System;
using System.Collections.Concurrent;
using System.IO;
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

    // initializer 用途？
    public void Init()
    {
        if (isDebugMode)
        {
            Debug.Log("InputManager is in debug mode. Using keyboard input.");
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
            int p = 0;
            if (Keyboard.current.wKey.isPressed) p++;
            if (Keyboard.current.sKey.isPressed) p--;
            peddale += p * 0.3f * dt;
            if (peddale < 0) peddale = 0;

            handle = 0;
            if (Keyboard.current.dKey.isPressed) handle = 30;
            if (Keyboard.current.aKey.isPressed) handle = -30;
        }
    }
}