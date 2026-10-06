#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Measures identical stationary races with mirrors and HUDs enabled/disabled.</summary>
public static class RacingUIPerformanceValidation
{
    private const string Key = "Racing.UI.Performance.Active";
    private static Gmanager manager;
    private static double started, phaseStarted;
    private static int phase = -1, lastFrame = -1;
    private static bool raceRequested;
    private static int warmFrames;
    private static double sampleStarted;
    private static readonly string[] phases = { "hud-on", "hud-off", "hud-on-repeat" };
    private static readonly List<double> frames = new List<double>();
    private static readonly List<string> report = new List<string>();
    private static readonly Dictionary<string, ProfilerRecorder> recorders = new Dictionary<string, ProfilerRecorder>();
    private static readonly Dictionary<string, double> totals = new Dictionary<string, double>();
    private static int oldVsync, oldTarget;
    private static bool outputsBound;
    private static readonly List<RenderTexture> outputs = new List<RenderTexture>();
    private static readonly List<Camera> outputCameras = new List<Camera>();

    public static void RunBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        UnityEngine.Object.FindFirstObjectByType<InputManager>().isDebugMode = true;
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void Resume()
    {
        if (SessionState.GetBool(Key, false)) EditorApplication.delayCall += Attach;
    }

    private static void Attach()
    {
        if (!EditorApplication.isPlaying) return;
        oldVsync = QualitySettings.vSyncCount; oldTarget = Application.targetFrameRate;
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
        started = EditorApplication.timeSinceStartup;
        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);
        var wanted = new HashSet<string> { "Main Thread", "Render Thread", "GPU Frame Time", "Draw Calls Count", "Triangles Count", "GC Allocated In Frame", "Canvas.BuildBatch", "Canvas.SendWillRenderCanvases" };
        foreach (var handle in handles)
        {
            var description = ProfilerRecorderHandle.GetDescription(handle);
            if (!wanted.Contains(description.Name) || recorders.ContainsKey(description.Name)) continue;
            var recorder = ProfilerRecorder.StartNew(description.Category, description.Name, 1);
            if (recorder.Valid) { recorders.Add(description.Name, recorder); totals.Add(description.Name, 0); }
        }
        report.Add("Unity " + Application.unityVersion + " / " + SystemInfo.graphicsDeviceName + " / " + Screen.width + "x" + Screen.height);
        report.Add("Editor Play Mode; both display cameras forced to 1280x720 render textures; VSync off; stationary cars; 3s warmup + 5s sample per phase. Not a standalone-player FPS estimate.");
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup - started > 90) throw new InvalidOperationException("Performance validation timed out.");
            if (manager == null) manager = UnityEngine.Object.FindFirstObjectByType<Gmanager>();
            if (manager == null || Time.frameCount < 5) return;
            if (!raceRequested) { raceRequested = true; manager.StartGame(); return; }
            if (manager.state != Gmanager.State.Game) return;
            if (!outputsBound)
            {
                for (int player=0;player<2;player++)
                {
                    Camera camera=manager.GetPlayerCaptureCamera(player);
                    if(camera==null)throw new InvalidOperationException("Missing display camera.");
                    RenderTexture target=new RenderTexture(1280,720,24);target.Create();
                    camera.targetTexture=target;outputs.Add(target);outputCameras.Add(camera);
                }
                outputsBound=true;
            }
            if (phase < 0) BeginPhase(0);
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            double elapsed = EditorApplication.timeSinceStartup - phaseStarted;
            if (elapsed < 3 || warmFrames++ < 30) return;
            if (sampleStarted < 0) sampleStarted = EditorApplication.timeSinceStartup;
            {
                frames.Add(Time.unscaledDeltaTime * 1000d);
                foreach (string name in recorders.Keys) totals[name] += recorders[name].LastValue;
            }
            if (EditorApplication.timeSinceStartup - sampleStarted < 5) return;
            frames.Sort();
            string line = phases[phase] + ": frames=" + frames.Count + "; median frame ms=" + frames[frames.Count / 2].ToString("F2");
            foreach (string name in recorders.Keys)
            {
                bool time = name.Contains("Thread") || name.Contains("Time") || name.StartsWith("Canvas.");
                line += "; " + name + "=" + (totals[name] / frames.Count / (time ? 1000000d : 1d)).ToString("F2") + (time ? "ms" : "");
            }
            report.Add(line); Debug.Log("UI_PERFORMANCE: " + line);
            if (phase + 1 < phases.Length) BeginPhase(phase + 1);
            else Finish(0);
        }
        catch (Exception exception) { Debug.LogError(exception); Finish(1); }
    }

    private static void BeginPhase(int next)
    {
        phase = next; frames.Clear(); warmFrames = 0; sampleStarted = -1;
        foreach (string name in recorders.Keys) totals[name] = 0;
        bool hud = phase != 1;
        foreach (var transition in UnityEngine.Object.FindObjectsByType<ScreenTransitionController>(FindObjectsSortMode.None))
        {
            Transform root = transition.transform.Find("OnPlay/ModernHUD");
            if (root == null) continue;
            root.gameObject.SetActive(hud);
        }
        phaseStarted = EditorApplication.timeSinceStartup;
    }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        foreach (var recorder in recorders.Values) recorder.Dispose();
        foreach(Camera camera in outputCameras) if(camera!=null) camera.targetTexture=null;
        foreach(RenderTexture texture in outputs){texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
        SessionState.SetBool(Key, false);
        QualitySettings.vSyncCount = oldVsync; Application.targetFrameRate = oldTarget;
        string output = Environment.GetEnvironmentVariable("RACING_UI_PERF_REPORT") ?? "/tmp/racing-ui-performance.txt";
        File.WriteAllLines(output, report);
        if (code == 0) Debug.Log("UI_PERFORMANCE_VALIDATION_PASS: " + output);
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(code);
    }
}
#endif
