using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>実際のシーンと車両で、離陸・着地・転倒中の入力を Play Mode で検証します。</summary>
public static class CarStabilityValidation
{
    internal const string SessionKey = "Racing.CarStabilityValidation";
    private static double started;

    [MenuItem("Racing/Validate Car Stability (Play Mode)")]
    public static void RunBatch()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before starting validation.");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.unity")
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        started = EditorApplication.timeSinceStartup;
        SessionState.SetBool(SessionKey, true);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    public static void RunFullBatch()
    {
        RunRegressionChecks();
        RunBatch();
    }

    public static void RunRegressionChecks()
    {
        SteeringValidation.Run();
        DriftValidation.Run();
        VisualEffectsValidation.Run();
        Debug.Log("CAR_STABILITY_REGRESSION_CHECKS_PASS");
    }

    [InitializeOnLoadMethod]
    private static void Resume()
    {
        if (!SessionState.GetBool(SessionKey, false)) return;
        started = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(SessionKey, false))
        {
            EditorApplication.update -= Tick;
            return;
        }
        if (EditorApplication.timeSinceStartup - started > 240d)
        {
            Finish(false, "Car stability validation timed out.");
            return;
        }
        if (!EditorApplication.isPlaying ||
            UnityEngine.Object.FindFirstObjectByType<Gmanager>() == null ||
            UnityEngine.Object.FindFirstObjectByType<CarStabilityValidationProbe>() != null) return;
        new GameObject("Car stability validation").AddComponent<CarStabilityValidationProbe>();
    }

    public static void Finish(bool passed, string message)
    {
        SessionState.SetBool(SessionKey, false);
        EditorApplication.update -= Tick;
        if (passed) Debug.Log(message); else Debug.LogError(message);
        if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }
}
