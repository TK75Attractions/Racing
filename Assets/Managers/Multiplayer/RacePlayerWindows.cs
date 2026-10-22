using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Standalone windows; two external Mac screens use independent borderless full-screen outputs.</summary>
public static class RacePlayerWindows
{
    public const int InitialWidth = 960;
    public const int InitialHeight = 540;
    public static bool UsesNativeWindow => !Application.isEditor &&
        Application.platform == RuntimePlatform.OSXPlayer &&
        SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (Application.isEditor) return;
        Application.runInBackground = true;
        // Avoid macOS fullscreen Spaces. The native plugin fills two external screens after both windows exist.
        if (Screen.fullScreenMode != FullScreenMode.Windowed)
            Screen.SetResolution(InitialWidth, InitialHeight, FullScreenMode.Windowed);
    }

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
    private const string Library = "RaceWindows";
    [DllImport(Library)] private static extern int RaceWindows_Open(int screen, int width, int height);
    [DllImport(Library)] private static extern void RaceWindows_UpdateLayout();
    [DllImport(Library)] private static extern int RaceWindows_ExternalFullscreen();
    [DllImport(Library)] private static extern void RaceWindows_Size(out int width, out int height);
    [DllImport(Library)] private static extern int RaceWindows_CloseRequested();
    [DllImport(Library)] private static extern void RaceWindows_Close();
    [DllImport(Library)] private static extern IntPtr RaceWindows_Frame(IntPtr texture);
    [DllImport(Library)] private static extern IntPtr RaceWindows_GetRenderEvent();
#endif

    public static bool Open(int preferredScreen)
    {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        if (UsesNativeWindow)
        {
            try { return RaceWindows_Open(preferredScreen, InitialWidth, InitialHeight) != 0; }
            catch (Exception error) when (error is DllNotFoundException || error is EntryPointNotFoundException)
            { Debug.LogError("P2 window plugin could not be loaded: " + error.Message); }
        }
#endif
        return false;
    }

    public static Vector2Int Size
    {
        get
        {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            RaceWindows_Size(out int width, out int height);
            return new Vector2Int(width, height);
#else
            return Vector2Int.zero;
#endif
        }
    }

    public static void UpdateLayout()
    {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        if (UsesNativeWindow) RaceWindows_UpdateLayout();
#endif
    }
    public static bool ExternalFullscreenActive
    {
        get
        {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            return UsesNativeWindow && RaceWindows_ExternalFullscreen()!=0;
#else
            return false;
#endif
        }
    }

    public static bool CloseRequested
    {
        get
        {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            return RaceWindows_CloseRequested() != 0;
#else
            return false;
#endif
        }
    }

    public static void Present(IntPtr texture)
    {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        // The native packet retains this frame's texture until its render-thread
        // callback runs, including across resizes or window teardown.
        var command = CommandBufferPool.Get("Racing P2 window");
        try
        {
            command.IssuePluginEventAndData(RaceWindows_GetRenderEvent(), 0, RaceWindows_Frame(texture));
            Graphics.ExecuteCommandBuffer(command);
        }
        finally { CommandBufferPool.Release(command); }
#endif
    }

    public static void Close()
    {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        RaceWindows_Close();
#endif
    }
}
