using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Exercise the entire no-input visitor flow against the real scene.</summary>
[InitializeOnLoad]
public static class ShortVisitorFlowValidation
{
    private const string Key = "Racing.ShortVisitorFlowValidation", Folder = "Artifacts/ShortVisitorFlow";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int stage, captures, page = -1;
    private static double began, started, resultBegan;
    private static string failure;
    private static PlayerDisplayRig[] rigs;
    private static RenderTexture[] outputs;
    static ShortVisitorFlowValidation() { began = EditorApplication.timeSinceStartup; EditorApplication.update += Tick; }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Directory.CreateDirectory(Folder); File.Delete(Folder + "/failure.txt");
        SessionState.SetBool(Key, true); stage = captures = 0; page = -1; failure = null;
        began = EditorApplication.timeSinceStartup; EditorApplication.isPlaying = true;
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false)) return;
        try
        {
            if (failure != null) throw new Exception(failure);
            if (EditorApplication.timeSinceStartup - began > 240) throw new Exception("Visitor flow timeout at " + stage);
            var manager = Gmanager.Control;
            if (!EditorApplication.isPlaying || manager == null) return;
            foreach (var publisher in Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None)) publisher.enabled = false;
            if (stage == 0 && Time.frameCount > 20)
            {
                if (manager.IsMaintenanceActive) manager.SetMaintenance(false);
                manager.IManager.SetInputMode("keyboard");
                rigs = (PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs", Private).GetValue(manager);
                outputs = new RenderTexture[2];
                for (int i = 0; i < 2; i++)
                {
                    outputs[i] = new RenderTexture(1920,1080,24); outputs[i].Create();
                    foreach (var camera in new[] {rigs[i].BackCamera,rigs[i].BackImageCamera,rigs[i].MainCamera,rigs[i].FrontCamera,rigs[i].UiCamera})
                        if (camera != null) camera.targetTexture = outputs[i];
                }
                manager.ConfirmTitleStart(0); // P2 provides no start input at all.
                started = EditorApplication.timeSinceStartup; stage = 1;
            }
            else if (stage == 1)
            {
                if (manager.state == Gmanager.State.Tutorial)
                {
                    Require(manager.GetPlayerPreparationState(1) == Gmanager.State.Tutorial, "P2 must join the shared group start.");
                    var controller = manager.GetComponent<DrivingTutorialController>();
                    var guide = controller.GetPlayerBriefing(0);
                    if (controller.GetPlayerProgress(0)?.IsFinished == true && guide != null && !guide.IsComplete && guide.Page != page)
                    {
                        page = guide.Page; Capture(manager, 0, "briefing-" + page + ".jpg");
                    }
                }
                if (manager.state != Gmanager.State.Game) return;
                double seconds = EditorApplication.timeSinceStartup - started;
                Require(seconds <= 60.5, "Preparation exceeded 60 seconds: " + seconds);
                Require(page == 1 && captures >= 2, "Both concise pages must have been presented.");
                Require(manager.GoalLap == 2, "The sole normal race must retain two laps.");
                Debug.Log("SHORT_FLOW_PREPARATION_PASS: " + seconds.ToString("F2") + " seconds, P2 did not press start.");
                File.WriteAllText(Folder + "/verification.txt", "Preparation: " + seconds.ToString("F2") + " seconds without player input, including intro and countdown.\n");
                Time.timeScale = 20; stage = 2;
            }
            else if (stage == 2 && manager.state == Gmanager.State.Result)
            {
                Require(manager.time >= 180 && manager.time < 182, "Normal race must timeout at 180 seconds: " + manager.time);
                File.AppendAllText(Folder + "/verification.txt", "Normal race: automatic timeout at " + manager.time.ToString("F2") + " simulation seconds, both humans DNF.\n");
                Time.timeScale = 1; stage = 3;
            }
            else if (stage == 3 && manager.CanUseDashboardControls)
            {
                var results = rigs[0].CanvasRoot.transform.Find("Result");
                if (!results.gameObject.activeInHierarchy) return;
                resultBegan = EditorApplication.timeSinceStartup;
                Capture(manager, 0, "result.jpg"); stage = 4;
            }
            else if (stage == 4)
            {
                bool first = rigs[0].CanvasRoot.GetComponent<ArcadeEndingUI>()?.IsVisible == true;
                bool second = rigs[1].CanvasRoot.GetComponent<ArcadeEndingUI>()?.IsVisible == true;
                if (!first && !second)
                {
                    Require(EditorApplication.timeSinceStartup - resultBegan < 16.5, "Result did not automatically close after 15 seconds."); return;
                }
                Require(first && second, "Both independent results must switch to exit guidance.");
                double seconds = EditorApplication.timeSinceStartup - resultBegan;
                Require(seconds >= 14.5 && seconds <= 16.5, "Result display duration: " + seconds);
                Require(!rigs[0].CanvasRoot.transform.Find("Result").gameObject.activeSelf, "Result must be hidden after exit guidance.");
                var exit = rigs[0].CanvasRoot.transform.Find("Ending/Thanks/Again").GetComponent<TMPro.TMP_Text>();
                Require(exit.text.Contains("おりてね"), "Explicit exit guidance is required.");
                exit.ForceMeshUpdate(); Require(!exit.isTextOverflowing && exit.font.HasCharacters(exit.text), "Exit text must fit and have all glyphs.");
                File.AppendAllText(Folder + "/verification.txt", "Results: both screens automatically switched to exit guidance after " + seconds.ToString("F2") + " real seconds, no pedal input.\n");
                resultBegan = EditorApplication.timeSinceStartup; stage = 5;
            }
            else if (stage == 5 && EditorApplication.timeSinceStartup - resultBegan > 1)
            {
                Capture(manager, 0, "exit-p1.jpg"); Capture(manager, 1, "exit-p2.jpg"); stage = 6;
            }
            else if (stage == 6 && captures == 5) Finish(null);
        }
        catch (Exception error) { Finish(error.ToString()); }
    }
    private static void Capture(Gmanager manager, int player, string name)
    {
        manager.RequestDashboardPlayerFrame(player, bytes => {
            if (bytes == null || bytes.Length < 1000) { failure = "Missing frame: " + name; return; }
            File.WriteAllBytes(Folder + "/" + name, bytes); captures++;
        });
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Finish(string error)
    {
        SessionState.SetBool(Key, false); Time.timeScale = 1;
        if (error == null) Debug.Log("SHORT_VISITOR_FLOW_VALIDATION_PASS");
        else { File.WriteAllText(Folder + "/failure.txt", error); Debug.LogError(error); }
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
    }
}
