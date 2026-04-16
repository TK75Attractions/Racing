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
    public bool isDebugMode = false;

    public float handle;
    public float peddale;

    public void Init()
    {
        if (isDebugMode)
        {
            Debug.Log("InputManager is in debug mode. Using keyboard input.");
        }
    }

    public void UpdateInput(float dt)
    {
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