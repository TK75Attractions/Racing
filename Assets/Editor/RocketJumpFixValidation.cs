using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Records real player cameras during the deterministic input-driven race regression.</summary>
[InitializeOnLoad]
public static class RocketJumpFixValidation
{
    public const string ActiveKey = "Racing.RocketJumpFix.Active";
    private static RenderTexture target;
    private static int jumpFrames, tubeFrames, resetFrames, lastFrame = -1;
    public static string Output => Path.GetFullPath("Artifacts/RocketJumpFix");

    static RocketJumpFixValidation() { EditorApplication.update += Tick; }

    [MenuItem("Racing/Validate Rocket Jump Fix And Record")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        Directory.CreateDirectory(Output);
        RaceItemValidation.Run();
        RaceCourseRoadValidation.Run();
        TubeCourseValidation.Run();
        SessionState.SetBool(ActiveKey, true);
        foreach (string clip in new[] { "jump", "tube", "reset" })
        {
            string directory = Path.Combine(Output, clip);
            if (Directory.Exists(directory))
                foreach (string frame in Directory.GetFiles(directory, "*.png")) File.Delete(frame);
        }
        jumpFrames = tubeFrames = resetFrames = 0;
        CourseJumpPlaythroughValidation.RunBatch();
    }

    private static void Tick()
    {
        // A local request is convenient for repeatable editor verification without menu timing.
        string request = Path.Combine(Output, "run.request");
        if (!Application.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode &&
            !EditorApplication.isCompiling && !EditorApplication.isUpdating && File.Exists(request))
        {
            File.Delete(request);
            Run();
        }
        if (!Application.isPlaying || !SessionState.GetBool(ActiveKey, false)) return;
        if (lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        Gmanager manager = Gmanager.Control;
        if (manager == null || manager.state != Gmanager.State.Game) return;
        GameObject car = GameObject.Find("Player1_Car");
        if (car == null) return;
        Vector3 p = car.transform.position;
        bool jump = p.x > 505f && p.x < 705f && p.z > 565f && p.z < 625f;
        bool tube = p.y < -7f && tubeFrames < 450;
        bool reset = SessionState.GetBool("Racing.RocketJumpFix.Reset", false);
        if (!jump && !tube && !reset) return;
        Camera camera = manager.GetPlayerCaptureCamera(reset ? 1 : 0);
        if (camera == null) return;
        if (target == null)
        {
            target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGBHalf);
            target.Create();
        }
        Canvas.ForceUpdateCanvases();
        camera.GetComponentInParent<NativeResolutionUI>().RenderToTarget(target);
        string clip = reset ? "reset" : jump ? "jump" : "tube";
        int frame = reset ? resetFrames++ : jump ? jumpFrames++ : tubeFrames++;
        string directory = Path.Combine(Output, clip);
        Directory.CreateDirectory(directory);
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(Path.Combine(directory, frame.ToString("D5") + ".png"), texture.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(texture); }
    }
}
