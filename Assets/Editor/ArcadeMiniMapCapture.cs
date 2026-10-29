using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Validates the actual HUD binding and records both display owners.</summary>
[InitializeOnLoad]
public static class ArcadeMiniMapCapture
{
    private const string Key = "Racing.ArcadeMiniMapCapture";
    private const string Folder = "Artifacts/ArcadeMiniMap";
    private static int stage, captures;
    private static double began, since;
    private static string failure;
    private static PlayerDisplayRig[] rigs;
    private static RenderTexture[] outputs;
    static ArcadeMiniMapCapture() { began = EditorApplication.timeSinceStartup; EditorApplication.update += Tick; }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        MiniMapValidation.Run();
        Directory.CreateDirectory(Folder);
        SessionState.SetBool(Key, true);
        stage = captures = 0; failure = null; began = EditorApplication.timeSinceStartup;
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false)) return;
        try
        {
            if (failure != null) throw new Exception(failure);
            if (EditorApplication.timeSinceStartup - began > 120) throw new Exception("Capture timed out: " + stage);
            var manager = Gmanager.Control;
            if (!EditorApplication.isPlaying || manager == null) return;
            foreach (var publisher in Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None)) publisher.enabled = false;
            if (stage == 0 && Time.frameCount > 20)
            {
                if (manager.IsMaintenanceActive) manager.SetMaintenance(false);
                manager.IManager.SetInputMode("keyboard");
                rigs = (PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
                for (int i = 0; i < 2; i++)
                {
                    Transform map = Map(i);
                    Require(map != null && !map.gameObject.activeInHierarchy, "Map must be hidden at title.");
                    Require(map.Find("Viewport/Content/Track") != null, "Production HUD must initialize its course.");
                }
                manager.StartGame(); stage = 1; since = EditorApplication.timeSinceStartup;
            }
            else if (stage == 1 && EditorApplication.timeSinceStartup - since > 1 && manager.CanUseDashboardControls)
            {
                Require(!Map(0).gameObject.activeInHierarchy && !Map(1).gameObject.activeInHierarchy, "Race map must stay hidden on the practice course.");
                manager.Features.SetValidationFeature(RaceFeature.CourseIntro, false);
                Require(manager.AdvanceDashboardToCountdown(), "Cannot start countdown."); stage = 2;
            }
            else if (stage == 2 && manager.state == Gmanager.State.Game)
            {
                outputs = new RenderTexture[2];
                for (int i = 0; i < 2; i++)
                {
                    outputs[i] = new RenderTexture(1920, 1080, 24); outputs[i].Create();
                    foreach (var camera in new[] { rigs[i].BackCamera, rigs[i].BackImageCamera, rigs[i].MainCamera, rigs[i].FrontCamera, rigs[i].UiCamera })
                        if (camera != null) camera.targetTexture = outputs[i];
                }
                stage = 3; since = EditorApplication.timeSinceStartup;
            }
            else if (stage == 3 && EditorApplication.timeSinceStartup - since > 1.5)
            {
                Canvas.ForceUpdateCanvases();
                for (int i = 0; i < 2; i++)
                {
                    Transform map = Map(i), content = map.Find("Viewport/Content");
                    Require(map.gameObject.activeInHierarchy, "Map must be visible during racing.");
                    var self = content.Find("Marker_P" + (i + 1));
                    Require(self.gameObject.activeSelf, "Own vehicle must be bound to its map.");
                    Require(Vector3.Distance(self.position, content.parent.position) < .1f, "Own vehicle must stay centered.");
                    Require(self.GetComponent<MiniMapMarkerGraphic>().color == ArcadeUI.For(i).Primary, "Map must use display owner's palette.");
                    Require(content.Find("Marker_P3").gameObject.activeSelf && content.Find("Marker_P4").gameObject.activeSelf, "Both CPU markers must be bound.");
                    AssertNoOverlap(map, rigs[i].CanvasRoot.transform.Find("ItemHUD/Toast"));
                    AssertNoOverlap(map, rigs[i].CanvasRoot.transform.Find("ItemHUD/StatusPlate"));
                    int player = i;
                    manager.RequestDashboardPlayerFrame(player, bytes => {
                        if (bytes == null || bytes.Length < 1000) { failure = "Missing live image."; return; }
                        File.WriteAllBytes(Folder + "/p" + (player + 1) + ".jpg", bytes); captures++;
                    });
                }
                stage = 4;
            }
            else if (stage == 4 && captures == 2) Finish(null);
        }
        catch (Exception error) { Finish(error.ToString()); }
    }

    private static Transform Map(int player) => rigs[player].CanvasRoot.transform.Find("OnPlay/ModernHUD/MiniMap");
    private static void AssertNoOverlap(Transform map, Transform other)
    {
        Require(other != null, "Missing item notification layout.");
        Transform root = rigs[0].CanvasRoot.transform;
        var a = RectTransformUtility.CalculateRelativeRectTransformBounds(root, map.Find("MapFrame"));
        var b = RectTransformUtility.CalculateRelativeRectTransformBounds(root, other);
        Require(!(a.min.x < b.max.x && b.min.x < a.max.x && a.min.y < b.max.y && b.min.y < a.max.y), "Map overlaps item notifications.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Finish(string error)
    {
        SessionState.SetBool(Key, false);
        if (error == null) Debug.Log("ARCADE_MINIMAP_CAPTURE_PASS: real P1/P2 HUD; title/practice hidden; race visible; cars and CPU bound; own marker centered; owner colors; item notices separate.");
        else Debug.LogError(error);
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
    }
}
