using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CourseJumpPlaythroughValidation
{
    private const string SessionKey = "Racing.CourseJumpPlaythrough";
    private static double started;

    [MenuItem("Racing/Validate Start And Jump Route (Play Mode)")]
    public static void RunBatch()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.unity")
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Open SampleScene before starting validation.");
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        }
        SessionState.SetBool(SessionKey, true);
        started = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void Resume()
    {
        if (!SessionState.GetBool(SessionKey, false)) return;
        started = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(SessionKey, false)) return;
        // Gmanager creates publishers in Awake. Disable them before their Start coroutine;
        // editing publishers before Play Mode would miss these newly created components.
        foreach (RaceRemoteFirebasePublisher publisher in UnityEngine.Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None))
            publisher.enabled = false;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(SessionKey, false)) { EditorApplication.update -= Tick; return; }
        if (EditorApplication.timeSinceStartup - started > 240d) { Finish(false, "Course playthrough timed out."); return; }
        if (!EditorApplication.isPlaying || UnityEngine.Object.FindFirstObjectByType<Gmanager>() == null ||
            UnityEngine.Object.FindFirstObjectByType<CourseJumpPlaythroughProbe>() != null) return;
        new GameObject("Course playthrough validation").AddComponent<CourseJumpPlaythroughProbe>();
    }

    public static void Finish(bool success, string message)
    {
        SessionState.SetBool(SessionKey, false);
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        if (success) Debug.Log(message); else Debug.LogError(message);
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }
}
