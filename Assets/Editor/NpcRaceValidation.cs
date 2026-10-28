using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class NpcRaceValidation
{
    private const string Key = "Racing.NpcValidation";
    private static double start;
    [MenuItem("Racing/Validate Four Car NPC Race (Play Mode)")]
    public static void RunBatch()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        SessionState.SetBool(Key, true); start = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= DisablePublisher; EditorApplication.playModeStateChanged += DisablePublisher;
        EditorApplication.isPlaying = true;
    }
    [MenuItem("Racing/Validate NPC Classification Rules")]
    public static void RunRules()
    {
        RaceCourseValidation.Run();
        ModernUIValidation.Run();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        MiniMapValidation.Run();
        ValidateItemTargets();
        GameObject fixture = new GameObject("Four car result validation", typeof(RectTransform));
        try
        {
            RaceSessionResult result = new RaceSessionResult(4);
            for (int i = 0; i < 4; i++) result.SetPlayerResult(i, new RaceResultRecord
                { playerNumber = i + 1, isCpu = i >= 2, finishPosition = 4 - i, totalRaceTime = 100 + 4 - i });
            ResultUIManager ui = new ResultUIManager(); ui.Init(fixture.transform, 1); ui.ShowResults(result);
            Transform card = fixture.transform.Find("ResultPresentation/ResultCard");
            if (card.Find("Classification/ResultRow1/Driver").GetComponent<TMPro.TMP_Text>().text != "CPU 2" ||
                !card.Find("Finish/Rank").GetComponent<TMPro.TMP_Text>().text.StartsWith("4"))
                throw new InvalidOperationException("Four-car CPU results or fourth-place presentation failed");
            RaceCourse course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>();
            UIMiniMap map = new UIMiniMap(); map.Init(fixture.transform, course, 0);
            Transform[] cars = new Transform[4];
            for (int i = 0; i < 4; i++)
            { cars[i] = new GameObject("Marker test " + i).transform; cars[i].SetParent(fixture.transform); course.TryGetPointAtProgress(i*20, out Vector3 point); cars[i].position = point; }
            map.SetCars(cars[0],cars[1]); map.SetCpuCars(cars[2],cars[3]);
            if (!fixture.transform.Find("MiniMap/Viewport/Content/Marker_P3").gameObject.activeSelf ||
                !fixture.transform.Find("MiniMap/Viewport/Content/Marker_P4").gameObject.activeSelf)
                throw new InvalidOperationException("CPU minimap markers are missing");
            Debug.Log("NPC_CLASSIFICATION_RULES_PASS: course/UI/minimap regression and four-car CPU winner/fourth-place results");
        }
        finally { UnityEngine.Object.DestroyImmediate(fixture); }
    }

    private static void ValidateItemTargets()
    {
        Gmanager manager = UnityEngine.Object.FindFirstObjectByType<Gmanager>();
        var entries = (System.Collections.Generic.List<RaceParticipant>)typeof(Gmanager).GetField("participants", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(manager);
        GameObject root = new GameObject("Four car item targets");
        try
        {
            for (int i = 0; i < 4; i++)
            {
                GameObject vehicle = new GameObject("Item target " + i); vehicle.transform.SetParent(root.transform);
                Rigidbody body = vehicle.AddComponent<Rigidbody>();
                CarItemEffects effects = vehicle.AddComponent<CarItemEffects>();
                entries.Add(new RaceParticipant { Index = i, IsCpu = i >= 2, Car = vehicle, Body = body, Effects = effects, Position = i == 0 ? 4 : i == 1 ? 1 : i == 2 ? 3 : 2,
                    Progress = i == 0 ? 100 : i == 1 ? 300 : i == 2 ? 125 : 140 });
            }
            manager.TryGetItemContext(entries[0].Body, out int position, out CarItemEffects target);
            if (position != 4 || target != entries[2].Effects) throw new InvalidOperationException("Closest ahead CPU was not an item target");
            entries[2].Result = new RaceResultRecord();
            manager.TryGetItemContext(entries[0].Body, out position, out target);
            if (target != entries[3].Effects) throw new InvalidOperationException("Finished CPU remained an item target");
            entries[3].Result = new RaceResultRecord { didFinish = false }; entries[0].Progress = 400;
            manager.TryGetItemContext(entries[0].Body, out position, out target);
            if (target != entries[1].Effects) throw new InvalidOperationException("Remaining human ahead was not an item target");
            entries[0].Position = 1; entries[1].Position = 2;
            manager.TryGetItemContext(entries[0].Body, out position, out target);
            if (target != null) throw new InvalidOperationException("Leader must not target a chasing human");
            Debug.Log("NPC_ITEM_TARGETS_PASS: four entries, nearest ranked CPU ahead, finished/withdrawn exclusion, no backwards attack");
        }
        finally { entries.Clear(); UnityEngine.Object.DestroyImmediate(root); }
    }

    [InitializeOnLoadMethod] private static void Resume()
    {
        if (!SessionState.GetBool(Key, false)) return;
        EditorApplication.playModeStateChanged -= DisablePublisher; EditorApplication.playModeStateChanged += DisablePublisher;
        start = EditorApplication.timeSinceStartup; EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }
    private static void DisablePublisher(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key,false)) return;
        foreach (RaceRemoteFirebasePublisher publisher in UnityEngine.Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None)) publisher.enabled=false;
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false)) { EditorApplication.update -= Tick; return; }
        if (EditorApplication.timeSinceStartup - start > 360) { Finish(false, "NPC validation wall-clock timeout"); return; }
        if (!EditorApplication.isPlaying || UnityEngine.Object.FindFirstObjectByType<Gmanager>() == null) return;
        foreach (RaceRemoteFirebasePublisher publisher in UnityEngine.Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None)) publisher.enabled = false;
        if (UnityEngine.Object.FindFirstObjectByType<NpcRaceValidationProbe>() == null)
            new GameObject("NPC race validation").AddComponent<NpcRaceValidationProbe>();
    }
    public static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key, false); EditorApplication.update -= Tick; EditorApplication.playModeStateChanged -= DisablePublisher;
        if (success) Debug.Log(message); else Debug.LogError(message);
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1); else EditorApplication.isPlaying = false;
    }
}
