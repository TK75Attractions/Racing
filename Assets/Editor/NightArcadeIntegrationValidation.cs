#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Checks the seams between the arcade UI and the night presentation branch.</summary>
public static class NightArcadeIntegrationValidation
{
    [InitializeOnLoadMethod]
    private static void ProtectBatchRuns()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (!Application.isBatchMode || state != PlayModeStateChange.EnteredPlayMode) return;
            foreach (var publisher in UnityEngine.Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None))
                publisher.enabled = false;
        };
    }

    [MenuItem("Racing/UI/Capture Night Arcade Course")]
    public static void CaptureCourse()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var input = UnityEngine.Object.FindFirstObjectByType<InputManager>();
        if (input == null) throw new InvalidOperationException("InputManager is required for course captures.");
        // Photographs should exercise connected production input gates without cabinet hardware.
        input.isDebugMode = true;
        input.isP1SerialP2KeyboardDebugMode = false;
        CourseVisualTour.RunBatch();
    }

    [MenuItem("Racing/UI/Validate Night Arcade Integration")]
    public static void Run()
    {
        RacingHUDFontBuilder.Build();
        ArcadeUIValidation.Run();
        SoundEffectsValidation.Run();
        ValidateVehicleMixer();
        ValidateAuthoredScenery();
        var root = new GameObject("Night arcade notice validation", typeof(RectTransform));
        try
        {
            ArcadeUI.Bind(root.transform, 0);
            var rail = ArcadeNoticeRail.Get(root.transform);
            rail.Set(0, "着地成功！");
            rail.Set(3, "P2 オイルでスリップ");
            rail.Set(4, "急カーブ：手前で減速");
            Require(rail.Text == "急カーブ：手前で減速", "Road safety must outrank item/proximity notices.");
            rail.Set(4, "");
            Require(rail.Text == "P2 オイルでスリップ", "Confirmed item feedback must survive a road warning.");
            rail.Clear();
            Require(rail.Text.Length == 0, "Race transitions must clear all notice priorities.");
            var queue = new DriverNoticeQueue();
            queue.Submit("landing", "着地成功！", DriverNoticePriority.Success, 0);
            Require(queue.Tick(0)?.Key == "landing", "Success feedback must enter the existing queue.");
            queue.Submit("curve", "急カーブ：手前で減速", DriverNoticePriority.Safety, .1f, condition: true);
            Require(queue.Tick(.1f)?.Key == "curve", "Road warnings must interrupt a success event.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        const string report = "NIGHT_ARCADE_INTEGRATION_PASS: arcade bindings, five notice priorities, event scheduling, authored stereo PCM, positional vehicle mixer, per-player BGM fades and result/title routing.";
        string output = Environment.GetEnvironmentVariable("RACING_NIGHT_UI_OUTPUT") ?? "Artifacts/NightArcadeIntegration";
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "integration-checks.txt"), report + "\n");
        Debug.Log(report);
    }
    private static void ValidateAuthoredScenery()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var manager = UnityEngine.Object.FindFirstObjectByType<Gmanager>();
        Require(manager != null && GameObject.Find("NightEnnichi") != null, "The authored night scene must be present.");
        var atmosphere = manager.gameObject.AddComponent<CampusAtmosphereController>();
        try
        {
            atmosphere.Configure(manager, manager.course);
            Require(!atmosphere.IsApplied && atmosphere.ReplacedTreeVertices == 0,
                "Generated daytime scenery must not replace the authored night materials or sakura.");
        }
        finally { UnityEngine.Object.DestroyImmediate(atmosphere); }
    }
    private static void ValidateVehicleMixer()
    {
        var root = new GameObject("Integrated vehicle mixer validation");
        root.SetActive(false);
        try
        {
            foreach (RaceVehicleAudioCue cue in Enum.GetValues(typeof(RaceVehicleAudioCue)))
                Require(RaceVehicleSoundBank.Get(cue) != null, "Missing night vehicle cue: " + cue);
            var mixer = root.AddComponent<RaceSoundEffectsMixer>();
            mixer.SetMusicGain(0);
            mixer.Enqueue(new RaceSoundClip(new float[] { .2f, .2f, .2f, .2f }), .25f, .75f);
            var samples = new float[4];
            mixer.Mix(samples, 2);
            Require(Mathf.Abs(samples[0] - .05f) < .00001f && Mathf.Abs(samples[1] - .15f) < .00001f,
                "Night vehicle panning must preserve the imported PCM channels and voice gains.");
            mixer.SetTireSkid(1, 1, 0);
            mixer.SetEffectsGain(0);
            samples = new float[4096];
            mixer.Mix(samples, 2);
            foreach (float sample in samples)
                Require(sample == 0f, "Effects mute must silence both vehicle voices and continuous tire noise.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
