#if UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>Give Unity's Windows secondary display a normal movable frame.</summary>
public sealed class WindowsPlayerWindow : MonoBehaviour
{
    private delegate bool EnumWindow(IntPtr window, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int left, top, right, bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(IntPtr window, string title);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong32(IntPtr window, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool AdjustWindowRect(ref Rect rect, uint style, bool menu);
    private HashSet<IntPtr> previous;
    private IntPtr secondary;
    private int displayIndex, width, height;
    private float deadline;

    public void Activate(int index)
    {
        displayIndex = index;
        previous = UnityWindows();
        foreach (IntPtr window in previous) SetWindowText(window, "RACING — P1");
        Display.displays[index].Activate(RacePlayerWindows.InitialWidth, RacePlayerWindows.InitialHeight, new RefreshRate { numerator = 60, denominator = 1 });
        deadline = Time.realtimeSinceStartup + 5f;
    }

    private void Update()
    {
        if (secondary == IntPtr.Zero)
        {
            foreach (IntPtr window in UnityWindows())
            {
                if (previous.Contains(window)) continue;
                secondary = window;
                const uint style = 0x10CF0000; // WS_VISIBLE | WS_OVERLAPPEDWINDOW
                GetWindowRect(window, out Rect position);
                Rect size = new Rect { right = RacePlayerWindows.InitialWidth, bottom = RacePlayerWindows.InitialHeight };
                AdjustWindowRect(ref size, style, false);
                if (IntPtr.Size == 8) SetWindowLongPtr64(window, -16, new IntPtr(style));
                else SetWindowLong32(window, -16, (int)style);
                SetWindowText(window, "RACING — P2");
                SetWindowPos(window, IntPtr.Zero, position.left + 24, position.top + 48,
                    size.right - size.left, size.bottom - size.top, 0x0034); // FRAMECHANGED | NOZORDER | NOACTIVATE
                break;
            }
            if (secondary == IntPtr.Zero && Time.realtimeSinceStartup > deadline)
            {
                Debug.LogError("P2 window could not be located after activating the selected display.");
                enabled = false;
            }
        }
        if (secondary == IntPtr.Zero || !GetClientRect(secondary, out Rect client)) return;
        int w = client.right - client.left, h = client.bottom - client.top;
        if (w <= 0 || h <= 0 || (w == width && h == height)) return;
        width = w; height = h;
        Display.displays[displayIndex].SetRenderingResolution(w, h);
    }

    private static HashSet<IntPtr> UnityWindows()
    {
        uint current = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        var result = new HashSet<IntPtr>();
        EnumWindows((window, parameter) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner != current) return true;
            var name = new StringBuilder(256);
            GetClassName(window, name, name.Capacity);
            if (name.ToString().StartsWith("Unity", StringComparison.Ordinal)) result.Add(window);
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
#endif
