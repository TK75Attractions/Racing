using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Checks real SampleScene startup, including exceptions raised before title visibility is applied.</summary>
[InitializeOnLoad]
public static class ScreenStartupValidation
{
    private const string ActiveKey = "Racing.ScreenStartupValidation.Active";
    private const string WithoutManagerKey = "Racing.ScreenStartupValidation.WithoutManager";
    private static readonly List<string> exceptions = new List<string>();
    private static int frames;
    private static double began;

    static ScreenStartupValidation()
    {
        EditorApplication.update += Tick;
        Application.logMessageReceived += (message, trace, type) => {
            if (SessionState.GetBool(ActiveKey, false) && type == LogType.Exception)
                exceptions.Add(message + "\n" + trace);
        };
    }

    public static void RunBatch()
    {
        Start(false);
    }

    // Reproduce a device/asset initialization failure before Gmanager can switch UI.
    public static void RunWithoutManagerBatch()
    {
        Start(true);
    }

    private static void Start(bool withoutManager)
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use Unity -batchmode.");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        InputManager input = UnityEngine.Object.FindFirstObjectByType<InputManager>();
        input.isDebugMode = true;
        input.isP1SerialP2KeyboardDebugMode = false;
        Transform canvas = input.transform.parent.Find("MainCanvas");
        Require(!canvas.Find("OnPlay").gameObject.activeSelf && !canvas.Find("Result").gameObject.activeSelf,
            "The saved scene must not expose gameplay and result screens at startup.");
        if (withoutManager)
        {
            // Even an Inspector preview saved with every screen enabled must be safe.
            canvas.Find("OnPlay").gameObject.SetActive(true);
            canvas.Find("Result").gameObject.SetActive(true);
            input.gameObject.SetActive(false);
        }
        SessionState.SetBool(WithoutManagerKey, withoutManager);
        SessionState.SetBool(ActiveKey, true);
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying) return;
        if (began == 0) began = EditorApplication.timeSinceStartup;
        if (++frames < 15 && EditorApplication.timeSinceStartup - began < 30) return;
        int code = 0;
        try
        {
            if (exceptions.Count > 0) throw new InvalidOperationException(string.Join("\n", exceptions));
            bool withoutManager = SessionState.GetBool(WithoutManagerKey, false);
            var manager = Gmanager.Control;
            if (!withoutManager) Require(manager != null && manager.state == Gmanager.State.Title, "Startup must finish at Title.");
            Transform owner = GameObject.Find("GameManagers").transform;
            foreach (string name in withoutManager ? new[] { "MainCanvas" } : new[] { "MainCanvas", "MainCanvas_P2" })
            {
                Transform canvas = owner.Find(name);
                Require(canvas != null, name + " must exist.");
                Require(canvas.Find("Title").gameObject.activeSelf, name + " title must be visible.");
                Require(!canvas.Find("OnPlay").gameObject.activeSelf, name + " gameplay HUD must be hidden.");
                Require(!canvas.Find("Result").gameObject.activeSelf, name + " result must be hidden.");
                if (!withoutManager)
                    Require(canvas.Find("Goal") != null && !canvas.Find("Goal").gameObject.activeSelf,
                        name + " goal celebration must be hidden.");
                Require(canvas.GetComponent<ScreenTransitionController>() != null, name + " transition must be initialized.");
            }
            if (!withoutManager)
            {
                Transform canvas = owner.Find("MainCanvas");
                var transition = canvas.GetComponent<ScreenTransitionController>();
                transition.ApplyStateImmediate(Gmanager.State.Game);
                Require(canvas.Find("OnPlay").gameObject.activeInHierarchy && !canvas.Find("Result").gameObject.activeInHierarchy,
                    "Gameplay must exclude the legacy result/goal screen.");
                Require(owner.Find("MainCanvas_P2/Title").gameObject.activeInHierarchy,
                    "Changing P1 must not change P2's screen.");
                Require(transition.TryPlayGoal("P1", "1:00.000", 5.5f), "Goal celebration must start.");
                Require(canvas.Find("Goal").gameObject.activeInHierarchy && !canvas.Find("OnPlay").gameObject.activeInHierarchy &&
                    !canvas.Find("Result").gameObject.activeInHierarchy, "Goal must exclude gameplay/result UI.");
                transition.ApplyStateImmediate(Gmanager.State.Result);
                Require(canvas.Find("Result").gameObject.activeInHierarchy && !canvas.Find("Goal").gameObject.activeInHierarchy &&
                    !canvas.Find("OnPlay").gameObject.activeInHierarchy, "Result must exclude gameplay/goal UI.");
                transition.ApplyStateImmediate(Gmanager.State.Title);
                Require(canvas.Find("Title").gameObject.activeInHierarchy && !canvas.Find("Result").gameObject.activeInHierarchy,
                    "Returning to title must hide the result.");
            }
            Debug.Log("SCREEN_STARTUP_VALIDATION_PASS: " + (withoutManager
                ? "Preview gameplay/result screens are hidden even when Gmanager never initializes."
                : "SampleScene initializes both players at Title without startup exceptions or overlapping screens."));
        }
        catch (Exception error) { Debug.LogError("SCREEN_STARTUP_VALIDATION_FAIL: " + error); code = 1; }
        SessionState.SetBool(ActiveKey, false);
        SessionState.SetBool(WithoutManagerKey, false);
        EditorApplication.Exit(code);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
