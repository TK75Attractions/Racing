using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Repeatable steering checks, real HUD captures and production-car Play Mode trials.</summary>
public static class SteeringComfortValidation
{
    private const string CourseKey = "Racing.SteeringComfort.Course";
    [MenuItem("Racing/Validate Steering Comfort (Full Suite)")]
    public static void RunFullBatch()
    {
        RunChecks();
        RacingHUDDesignPreview.CaptureSteeringComfort();
        CarStabilityValidation.RunBatch();
    }

    public static void RunChecks()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        RacingHUDFontBuilder.Build();
        SteeringValidation.Run();
        DriftValidation.Run();
        ModernUIValidation.Run();
    }

    public static void RunCourseBatch()
    {
        // UI is captured separately. Course rehearsal needs the original collision geometry only.
        SessionState.SetBool(CourseKey, true);
        ResumeCourseRenderingOverride();
        BalancePlaytestValidation.RunBatch();
    }

    [InitializeOnLoadMethod]
    private static void ResumeCourseRenderingOverride()
    {
        if (!SessionState.GetBool(CourseKey, false)) return;
        EditorApplication.update -= DisableCourseRendering;
        EditorApplication.update += DisableCourseRendering;
    }

    private static void DisableCourseRendering()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            SessionState.SetBool(CourseKey, false);
            EditorApplication.update -= DisableCourseRendering;
            return;
        }
        if (!EditorApplication.isPlaying) return;
        foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) camera.enabled = false;
    }
}
