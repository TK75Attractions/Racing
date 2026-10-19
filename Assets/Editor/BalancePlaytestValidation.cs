using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Opt-in balance rehearsal; never saves changes to the scene or gameplay settings.</summary>
public static class BalancePlaytestValidation
{
    private const string Key = "Racing.BalancePlaytest.Active";
    private static double started;

    [MenuItem("Racing/Playtest/Run Balance Agent Suite")]
    public static void RunBatch()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(TubeCoursePreparation.ModelPath);
        if (model == null || model.GetComponentInChildren<MeshFilter>(true) == null)
            throw new InvalidOperationException("The course model is not imported. Materialize Git LFS assets before playtesting.");
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (Environment.GetEnvironmentVariable("RACING_VALIDATE_RESULT_UI") == "1") ResultHighlightValidation.Run();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        SessionState.SetBool(Key, true);
        Resume();
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void Resume()
    {
        if (!SessionState.GetBool(Key, false)) return;
        started = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnState;
        EditorApplication.playModeStateChanged += OnState;
    }

    private static void OnState(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode) DisablePublishing();
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnState;
        }
    }
    private static void DisablePublishing()
    {
        foreach (var publisher in UnityEngine.Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None))
            publisher.enabled = false;
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false)) { EditorApplication.update -= Tick; return; }
        if (EditorApplication.timeSinceStartup - started > 1800) { Finish(false, "Balance playtest wall-clock timeout; inspect partial report."); return; }
        if (!EditorApplication.isPlaying || Gmanager.Control == null) return;
        DisablePublishing();
        if (Environment.GetEnvironmentVariable("RACING_CPU_RACING") == "off" && !Gmanager.Control.Features.Settings.RulesLocked)
            Gmanager.Control.Features.SetFeature(RaceFeature.CpuRacing, false);
        if (Environment.GetEnvironmentVariable("RACING_P2_AUTO") == "1" && !Gmanager.Control.Features.Settings.RulesLocked)
            Gmanager.Control.Features.SetFeature(RaceFeature.PlayerTwoAutodrive, true);
        if (Environment.GetEnvironmentVariable("RACING_FEATURE_PRESET") == "experience" &&
            !Gmanager.Control.Features.Settings.RulesLocked)
            Gmanager.Control.Features.SelectPreset(RacePreset.Experience);
        if (Environment.GetEnvironmentVariable("RACING_FEATURE_PLAYTEST") == "1" &&
            UnityEngine.Object.FindFirstObjectByType<GameplayFeaturePlaytestProbe>() == null)
            Gmanager.Control.gameObject.AddComponent<GameplayFeaturePlaytestProbe>();
        if (UnityEngine.Object.FindFirstObjectByType<BalancePlaytestAgent>() == null)
            new GameObject("Balance playtest agent").AddComponent<BalancePlaytestAgent>();
    }
    public static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key, false);
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnState;
        if (success) Debug.Log(message); else Debug.LogError(message);
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }
}
