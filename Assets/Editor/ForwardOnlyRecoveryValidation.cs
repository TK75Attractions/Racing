using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Controlled stuck fixtures use the production car, collisions and automatic recovery.</summary>
public static class ForwardOnlyRecoveryValidation
{
    private const string Key = "Racing.ForwardOnlyRecovery";
    private static double began;
    [MenuItem("Racing/Playtest/Validate Forward-Only Recovery")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var input = UnityEngine.Object.FindFirstObjectByType<InputManager>();
        input.isDebugMode = true; input.isP1SerialP2KeyboardDebugMode = false;
        foreach (var bridge in UnityEngine.Object.FindObjectsByType<RaceDashboardBridge>(FindObjectsSortMode.None)) bridge.enabled = false;
        foreach (var publisher in UnityEngine.Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None)) publisher.enabled = false;
        SessionState.SetBool(Key, true); Resume(); EditorApplication.isPlaying = true;
    }
    [InitializeOnLoadMethod] private static void Resume()
    {
        if (!SessionState.GetBool(Key, false)) return;
        began = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false)) { EditorApplication.update -= Tick; return; }
        if (EditorApplication.timeSinceStartup - began > 240) { Finish(false, "Recovery probe timeout"); return; }
        if (!EditorApplication.isPlaying || Gmanager.Control == null || UnityEngine.Object.FindFirstObjectByType<ForwardOnlyRecoveryProbe>() != null) return;
        new GameObject("Forward-only recovery probe").AddComponent<ForwardOnlyRecoveryProbe>();
    }
    public static void Finish(bool passed, string message)
    {
        SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
        if (passed) Debug.Log(message); else Debug.LogError(message);
        if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1); else EditorApplication.isPlaying = false;
    }
}
