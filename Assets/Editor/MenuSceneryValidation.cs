using System;
using System.IO;
using System.Reflection;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>Exercise the real two-player menu flow and capture the rendered course behind its UI.</summary>
public static class MenuSceneryValidation
{
    private const string Key = "Racing.MenuSceneryValidation.Active";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int stage;
    private static double started, changed;
    private static PlayerDisplayRig[] rigs;
    private static CampusAtmospherePlaytestProbe campusProbe;
    private static string runtimeError;
    private static string Folder => Environment.GetEnvironmentVariable("RACING_MENU_SCENERY_OUTPUT") ?? "Logs/MenuScenery";

    [MenuItem("Racing/Validation/Menu Scenery Playthrough")]
    public static void RunBatch()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        // Test the real menus without requiring physical steering hardware. Do not save the scene.
        var input = UnityEngine.Object.FindFirstObjectByType<InputManager>();
        input.isDebugMode = true;
        input.isP1SerialP2KeyboardDebugMode = false;
        SessionState.SetBool(Key, true);
        Resume();
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void Resume()
    {
        if (!SessionState.GetBool(Key, false)) return;
        stage = 0; rigs = null; runtimeError = null;
        started = changed = EditorApplication.timeSinceStartup;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        try
        {
            if (runtimeError != null) throw new InvalidOperationException(runtimeError);
            if (EditorApplication.timeSinceStartup - started > 240) throw new TimeoutException("Menu scenery playthrough timeout.");
            if (!EditorApplication.isPlaying || Gmanager.Control == null) return;
            var manager = Gmanager.Control;
            foreach (var publisher in UnityEngine.Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None)) publisher.enabled = false;
            foreach (var output in UnityEngine.Object.FindObjectsByType<RaceAudioOutput>(FindObjectsSortMode.None)) output.enabled = false;
            var director = manager.GetComponent<MenuSceneryDirector>();
            if (director == null) return;
            if (rigs == null) rigs = (PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs", Private).GetValue(manager);
            double elapsed = EditorApplication.timeSinceStartup - changed;
            switch (stage)
            {
                case 0:
                    if (elapsed < 4 || Time.frameCount < 10) return;
                    Require(director.IsShowing, "The title must use the campus view.");
                    CheckArt(false);
                    Require(Vector3.Distance(MenuSceneryDirector.EvaluatePose(false, 0).position, MenuSceneryDirector.EvaluatePose(false, 12).position) > 20,
                        "The campus view must move slowly over time.");
                    Capture(0, "title");
                    manager.DebugPreviewResult();
                    Next(); break;
                case 1:
                    if (elapsed < 7) return;
                    Require(manager.state == Gmanager.State.Result && director.IsShowing, "Both result screens need the campus shot.");
                    foreach (var rig in rigs) Require(rig.MainCamera.GetComponent<CinemachineBrain>().ActiveVirtualCamera?.Name == "TitleCamera_P" + (rig.PlayerIndex + 1),
                        "The menu camera must drive each result display.");
                    Capture(0, "result"); Capture(1, "result-p2");
                    manager.Features.SetValidationFeature(RaceFeature.MenuScenery, false);
                    Require(!director.IsShowing, "F8 OFF must release scenery cameras immediately.");
                    CheckArt(true);
                    Require(!GameObject.Find("MenuSceneryBackdrop"), "OFF must hide the temporary ground extension.");
                    Next(); break;
                case 2:
                    if (elapsed < 1) return;
                    manager.Features.SetValidationFeature(RaceFeature.MenuScenery, true);
                    Require(director.IsShowing, "F8 ON must restore scenery immediately.");
                    director.enabled = false;
                    Require(!director.IsShowing, "Disabling the component must restore legacy presentation.");
                    CheckArt(true);
                    director.enabled = true;
                    Next(); break;
                case 3:
                    if (elapsed < 1) return;
                    Require(director.IsShowing, "Re-enabling must restore scenery without duplicating cameras.");
                    typeof(Gmanager).GetMethod("ReturnPlayerToTitle", Private).Invoke(manager, new object[] { 0 });
                    Next(); break;
                case 4:
                    if (elapsed < 4) return;
                    Require(manager.GetPlayerAudioState(0) == Gmanager.State.Title && manager.GetPlayerAudioState(1) == Gmanager.State.Result,
                        "The two players must be able to keep title and results independently.");
                    Capture(0, "returned-title");
                    // Verify the shared aerial shadow settings also restore after the course intro.
                    campusProbe = manager.gameObject.AddComponent<CampusAtmospherePlaytestProbe>();
                    manager.StartAutodriveTestRace();
                    Next(); break;
                case 5:
                    if (manager.state != Gmanager.State.Game) return;
                    if (campusProbe == null || !campusProbe.Passed) return;
                    Require(!director.IsShowing && !manager.IsMenuSceneryPlaying, "Scenery must release the camera before driving.");
                    Require(!GameObject.Find("MenuSceneryBackdrop"), "The ground extension must be inactive while racing.");
                    foreach (var rig in rigs) Require(ReferenceEquals(rig.MainCamera.GetComponent<CinemachineBrain>().ActiveVirtualCamera, rig.RaceCamera),
                        "Countdown/intro must hand off to each actual race camera.");
                    Finish(true, "MENU_SCENERY_VALIDATION_PASS: live title/results, moving views, independent title return, live OFF/ON, disable/re-enable, intro/countdown/race camera handoff; real P1/P2 captures.");
                    break;
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Run before Start, so another checkout's dashboard listener is never displaced by this test.
        foreach (var bridge in UnityEngine.Object.FindObjectsByType<RaceDashboardBridge>(FindObjectsSortMode.None)) bridge.enabled = false;
        foreach (var publisher in UnityEngine.Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None)) publisher.enabled = false;
        foreach (var output in UnityEngine.Object.FindObjectsByType<RaceAudioOutput>(FindObjectsSortMode.None)) output.enabled = false;
    }
    private static void Next() { stage++; changed = EditorApplication.timeSinceStartup; }
    private static void OnLog(string message, string stack, LogType type)
    {
        // Unity's editor search index can log an unrelated startup exception in headless runs.
        if ((type == LogType.Error || type == LogType.Exception) && !stack.Contains("UnityEditor.Search"))
            runtimeError = message + "\n" + stack;
    }
    private static void CheckArt(bool legacy)
    {
        foreach (var rig in rigs)
        {
            Transform root = rig.CanvasRoot.transform;
            Require(root.Find("Title/TitleBackground").GetComponent<RawImage>().enabled == legacy, "Title artwork switch must match F8.");
            Require(root.Find("Result/ResultPresentation/ResultBackground").GetComponent<RawImage>().enabled == legacy, "Result artwork switch must match F8.");
            Require(root.Find("Title/SceneryTitleBrand").gameObject.activeSelf != legacy, "The live title must retain its own legible branding.");
        }
    }
    private static void Capture(int player, string name)
    {
        Directory.CreateDirectory(Folder);
        Canvas.ForceUpdateCanvases();
        File.WriteAllBytes(Path.Combine(Folder, name + ".jpg"), rigs[player].CaptureJpeg(1280, 720));
    }
    private static void Finish(bool success, string message)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "validation.txt"), message + "\n");
        SessionState.SetBool(Key, false);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (success) Debug.Log(message); else Debug.LogError(message);
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
