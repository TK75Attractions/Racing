using UnityEditor;
using UnityEngine;

public static class OperatorUpdateMenu
{
    private static RaceOperatorUpdate pending;

    [MenuItem("Racing/Update main, Build and Run %&u")]
    public static void Run()
    {
        if (pending != null && pending.Busy) return;
        var race = Object.FindFirstObjectByType<Gmanager>();
        if (race == null) { Debug.LogWarning("SampleSceneを開いてから更新してください。"); return; }
        if (EditorApplication.isPlaying)
        {
            var service = race.GetComponent<RaceOperatorUpdate>();
            if (service != null && !service.Request()) Debug.LogWarning(service.Status);
            return;
        }
        // Edit-mode requests need an Editor tick while waiting for Git preflight.
        var root = EditorUtility.CreateGameObjectWithHideFlags("Operator update", HideFlags.HideAndDontSave);
        root.SetActive(false);
        pending = root.AddComponent<RaceOperatorUpdate>(); pending.Configure(race);
        if (!pending.Request()) { Debug.LogWarning(pending.Status); Object.DestroyImmediate(root); pending = null; return; }
        EditorApplication.update += Poll;
    }

    private static void Poll()
    {
        if (pending != null) pending.Poll();
        if (pending != null && pending.Busy) return;
        EditorApplication.update -= Poll;
        if (pending != null) { Debug.LogWarning(pending.Status); Object.DestroyImmediate(pending.gameObject); }
        pending = null;
    }
}
