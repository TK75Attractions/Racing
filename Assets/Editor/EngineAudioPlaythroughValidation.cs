using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Captures real OnAudioFilterRead PCM before per-player routing while the existing driver runs.</summary>
public static class EngineAudioPlaythroughValidation
{
    private const string Key = "Racing.EngineAudioPlaythrough";
    private static string output;
    private static StreamWriter trace;
    private static CarSoundController engine;
    private static EngineAudioCore core;
    private static double started;
    private static float segmentStarted, lastTime = -1;
    private static int stage, maxGear;
    private static bool tunnelSeen, drySeen;
    private static float maxRoom;

    [MenuItem("Racing/Validation/Engine Audio Playthrough")]
    public static void RunBatch()
    {
        SessionState.SetBool(Key, true);
        Subscribe();
        CourseJumpPlaythroughValidation.RunBatch();
    }
    [InitializeOnLoadMethod]
    private static void Resume() { if (SessionState.GetBool(Key, false)) Subscribe(); }
    private static void Subscribe()
    {
        started = EditorApplication.timeSinceStartup;
        output = Path.Combine(Directory.GetCurrentDirectory(), "Artifacts/EngineAudio");
        Directory.CreateDirectory(output);
        stage = 0; engine = null; core = null; trace = null;
        maxGear = 1; maxRoom = 0; tunnelSeen = drySeen = false; lastTime = -1;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false)) { EditorApplication.update -= Tick; return; }
        try
        {
            if (EditorApplication.timeSinceStartup - started > 230) throw new Exception("Engine audio playthrough timed out.");
            if (!Application.isPlaying || Gmanager.Control == null || Gmanager.Control.state != Gmanager.State.Game) return;
            Transform car = Gmanager.Control.GetPlayerCarTransform(0);
            if (car == null || Time.time == lastTime) return;
            lastTime = Time.time;
            if (engine == null)
            {
                engine = car.GetComponentInChildren<CarSoundController>(); core = engine.audioCore;
                trace = new StreamWriter(Path.Combine(output, "gameplay.csv"));
                trace.WriteLine("time,x,y,z,speed_mps,gear,rpm,tunnel");
                core.BeginPreviewRecording(14); segmentStarted = Time.time; stage = 1;
            }
            Vector3 p = car.position;
            maxGear = Math.Max(maxGear, engine.CurrentGear); maxRoom = Math.Max(maxRoom, engine.TunnelAmount);
            if (engine.TunnelAmount > .5f) tunnelSeen = true;
            else if (!tunnelSeen) drySeen = true;
            trace.WriteLine(FormattableString.Invariant($"{Time.time:F3},{p.x:F3},{p.y:F3},{p.z:F3},{car.GetComponent<Rigidbody>().linearVelocity.magnitude:F2},{engine.CurrentGear},{engine.CurrentRpm:F1},{engine.TunnelAmount:F2}"));
            // Read the actual HUD, not a separate test-only display.
            if (Time.time - segmentStarted > 3f)
            {
                RacingGearDisplay display = UnityEngine.Object.FindObjectsByType<RacingGearDisplay>(FindObjectsSortMode.None)
                    .FirstOrDefault(d => d.PlayerIndex == 0);
                if (display == null) throw new Exception("Missing P1 gear HUD.");
                {
                    string expected = engine.IsReverse ? "R" : engine.CurrentGear.ToString();
                    if (display.GetComponentInChildren<TMP_Text>().text != expected) throw new Exception("Actual P1 gear HUD disagrees with its engine.");
                }
            }
            if (stage == 1 && Time.time - segmentStarted >= 14) { Save("05-gameplay-launch.wav"); stage = 2; }
            if (stage == 2 && engine.TunnelAmount > .5f)
            { core.BeginPreviewRecording(12); segmentStarted = Time.time; stage = 3; }
            if (stage == 3 && Time.time - segmentStarted >= 12) { Save("06-gameplay-tunnel.wav"); SavePhoto(); stage = 4; }
            if (stage == 4 && engine.TunnelAmount < .01f && p.x < 310 && p.z > 510)
            { core.BeginPreviewRecording(4); segmentStarted = Time.time; stage = 5; }
            if (stage == 5 && Time.time - segmentStarted >= 4)
            {
                Save("07-gameplay-exit.wav");
                // This course peaks below the longer second-gear upshift speed; first/second is the intended coverage.
                if (!tunnelSeen || !drySeen || maxGear < 2) throw new Exception("Missing real drive/shift/tunnel coverage.");
                trace.Dispose(); trace = null;
                File.WriteAllText(Path.Combine(output, "gameplay-result.txt"),
                    $"PASS: actual car driven by pedal/steering input, highest gear {maxGear}, peak tunnel enclosure {maxRoom:F2}, dry road, tube entry/interior/exit, real DSP callback PCM before per-player capture.\n");
                Finish(true, "ENGINE_AUDIO_PLAYTHROUGH_PASS: actual shifts and real PCM captured on open road, inside tube and at exit.");
            }
        }
        catch (Exception ex) { trace?.Dispose(); trace = null; Finish(false, ex.ToString()); }
    }
    private static void Save(string name)
    {
        float[] pcm = core.EndPreviewRecording(out int rate);
        if (pcm.Length < rate || !pcm.Any(x => Mathf.Abs(x) > .001f))
            throw new Exception("Actual audio callback produced no audible recording: " + name);
        EngineAudioValidation.WriteWave(Path.Combine(output, name), pcm, rate);
        Debug.Log($"ENGINE_GAMEPLAY_PCM: {name}, {pcm.Length / (rate * 2f):F2}s, {rate}Hz, peak={pcm.Max(x => Mathf.Abs(x)):F4}");
    }
    private static void SavePhoto()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        Camera camera = Gmanager.Control.GetPlayerCaptureCamera(0);
        var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGBHalf);
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            target.Create(); Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(output, "gameplay-tunnel.png"), image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; target.Release();
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
        }
    }
    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
        CourseJumpPlaythroughValidation.Finish(success, message);
    }
}
