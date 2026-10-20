using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Actual one-lap races; finish events and subsequent physical travel are observed.</summary>
public static class FinishRunValidation
{
    private const string Key = "Racing.FinishRunValidation";
    private static double started;
    [MenuItem("Racing/Validation/Finish Run Playthrough")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var input = UnityEngine.Object.FindFirstObjectByType<InputManager>();
        input.isDebugMode = true; input.isP1SerialP2KeyboardDebugMode = false;
        var publisher = UnityEngine.Object.FindFirstObjectByType<RaceRemoteFirebasePublisher>();
        if (publisher != null) publisher.enabled = false;
        SessionState.SetBool(Key, true); Resume(); EditorApplication.isPlaying = true;
    }
    [InitializeOnLoadMethod] private static void Resume()
    {
        if (!SessionState.GetBool(Key, false)) return;
        started = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false)) { EditorApplication.update -= Tick; return; }
        if (EditorApplication.timeSinceStartup - started > 600) { Finish(false, "Finish run validation timed out."); return; }
        if (!EditorApplication.isPlaying || Gmanager.Control == null) return;
        if (UnityEngine.Object.FindFirstObjectByType<FinishRunValidationProbe>() == null)
            Gmanager.Control.gameObject.AddComponent<FinishRunValidationProbe>().Complete = Finish;
    }
    public static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
        if (success) Debug.Log(message); else Debug.LogError(message);
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }
}
