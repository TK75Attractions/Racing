#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class ResultUIPreviewMenu
{
    [MenuItem("Racing/Preview Result UI", priority = 30)]
    private static void PreviewResult()
    {
        Gmanager manager = Object.FindFirstObjectByType<Gmanager>();
        if (!Application.isPlaying || manager == null)
        {
            Debug.LogWarning("Result preview is available while the game is playing.");
            return;
        }
        manager.DebugPreviewResult();
    }

    [MenuItem("Racing/Preview Result UI", true)]
    private static bool ValidatePreviewResult() => Application.isPlaying && Object.FindFirstObjectByType<Gmanager>() != null;
}
#endif
