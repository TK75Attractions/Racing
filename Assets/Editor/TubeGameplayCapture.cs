using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Photographs the actual P1 camera while the car validation drives, without staging poses or lighting.</summary>
public static class TubeGameplayCapture
{
    private const string ActiveKey = "Racing.TubeGameplayCapture.Active";
    private const string StageKey = "Racing.TubeGameplayCapture.Stage";
    private const string OutputKey = "Racing.TubeGameplayCapture.Output";
    [Serializable] private sealed class Frame { public string file; public Vector3 carPosition; public float speed; public float gameTime; public string camera; }
    [Serializable] private sealed class Manifest { public List<Frame> frames = new List<Frame>(); }
    private static readonly Manifest manifest = new Manifest();
    private static readonly string[] Names = { "01-entry-descent", "02-deep-straight", "03-underground-curve", "04-west-exit" };
    private static RenderTexture target;
    private static int renderedFrame = -1;

    [MenuItem("Racing/Capture Tube Gameplay (Play Mode)")]
    public static void RunBatch()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException("Gameplay photos require graphics; omit -nographics.");
        string output = Environment.GetEnvironmentVariable("RACING_TUBE_CAPTURE_OUTPUT") ?? Path.Combine(Path.GetTempPath(), "racing-tube-gameplay");
        Directory.CreateDirectory(output);
        manifest.frames.Clear();
        renderedFrame = -1;
        SessionState.SetInt(StageKey, 0);
        SessionState.SetString(OutputKey, output);
        SessionState.SetBool(ActiveKey, true);
        Subscribe();
        CourseJumpPlaythroughValidation.RunBatch();
    }

    [InitializeOnLoadMethod]
    private static void Resume() { if (SessionState.GetBool(ActiveKey, false)) Subscribe(); }
    private static void Subscribe()
    {
        EditorApplication.update -= Photograph;
        EditorApplication.update += Photograph;
        EditorApplication.playModeStateChanged -= StateChanged;
        if (target != null)
        {
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            target = null;
        }
        EditorApplication.playModeStateChanged += StateChanged;
    }
    private static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingPlayMode && state != PlayModeStateChange.EnteredEditMode) return;
        SessionState.SetBool(ActiveKey, false);
        EditorApplication.update -= Photograph;
        EditorApplication.playModeStateChanged -= StateChanged;
        if (target != null)
        {
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            target = null;
        }
    }

    private static void Photograph()
    {
        if (!Application.isPlaying || !SessionState.GetBool(ActiveKey, false)) return;
        int stage = SessionState.GetInt(StageKey, 0);
        if (stage >= Names.Length) return;
        Gmanager manager = UnityEngine.Object.FindFirstObjectByType<Gmanager>();
        if (manager == null || manager.state != Gmanager.State.Game) return;
        GameObject car = GameObject.Find("Player1_Car");
        if (car == null) return;
        Camera camera = manager.GetPlayerCaptureCamera(0);
        if (camera == null || renderedFrame == Time.frameCount) return;
        // Batch Mode does not draw a Game View automatically. Keep the actual camera's
        // frame history current so motion blur represents adjacent gameplay frames.
        RenderPlayerCamera(camera);
        renderedFrame = Time.frameCount;
        Vector3 p = car.transform.position;
        bool ready = stage == 0 ? p.x > 650f && p.z < 515f && p.z > 460f && p.y < 3f :
            stage == 1 ? p.x > 650f && p.z < 350f && p.z > 290f && p.y < -20f :
            stage == 2 ? p.x < 595f && p.x > 510f && p.z < 220f && p.y < -15f :
            p.x < 295f && p.z > 525f && p.z < 565f && p.y > -8f;
        if (!ready) return;
        string directory = SessionState.GetString(OutputKey, "");
        string file = Names[stage] + ".png";
        SavePhoto(Path.Combine(directory, file));
        manifest.frames.Add(new Frame { file = file, carPosition = p, speed = car.GetComponent<Rigidbody>().linearVelocity.magnitude,
            gameTime = Time.time, camera = camera.name });
        File.WriteAllText(Path.Combine(directory, "frames.json"), JsonUtility.ToJson(manifest, true));
        SessionState.SetInt(StageKey, stage + 1);
        Debug.Log($"TUBE_GAMEPLAY_PHOTO: {file} car={p} speed={manifest.frames[manifest.frames.Count - 1].speed:F1}m/s");
    }

    private static void RenderPlayerCamera(Camera camera)
    {
        if (target == null)
        {
            target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGBHalf) { hideFlags = HideFlags.DontSave };
            target.Create();
        }
        var request = new RenderPipeline.StandardRequest { destination = target };
        if (!RenderPipeline.SupportsRenderRequest(camera, request))
            throw new InvalidOperationException("The player's camera stack does not support capture.");
        Canvas.ForceUpdateCanvases();
        RenderPipeline.SubmitRenderRequest(camera, request);
    }

    private static void SavePhoto(string path)
    {
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        RenderTexture previousActive = RenderTexture.active;
        try
        {
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
