using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CircuitVisualRefinementValidation
{
    public const string Output = "Artifacts/CircuitRefinements";
    public static void RunMeters()
    {
        ArcadeUIValidation.Run(); Directory.CreateDirectory(Output);
        string[] settings = { "ProjectSettings/GraphicsSettings.asset", "ProjectSettings/QualitySettings.asset" };
        byte[][] original = Array.ConvertAll(settings, File.ReadAllBytes);
        try
        {
            var capture = typeof(ArcadeUIValidation).GetMethod("CaptureView", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (int player in new[] { 0, 1 }) foreach (int width in new[] { 1280, 1920 })
            {
                capture.Invoke(null, new object[] { player, "race", width, width * 9 / 16 });
                string suffix = width == 1920 ? "" : "-1280x720";
                File.Copy($"Artifacts/ArcadeSportUI/race-p{player + 1}{suffix}.png", $"{Output}/meters-p{player + 1}-{width}.png", true);
            }
            Debug.Log("CIRCUIT_METERS_PASS: both palettes; 720p/1080p; production dial bindings and dirty-mesh checks.");
        }
        finally { for (int i = 0; i < settings.Length; i++) File.WriteAllBytes(settings[i], original[i]); }
    }
    public static void RunSchool()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity"); Directory.CreateDirectory(Output);
        Gmanager previous = Gmanager.Control;
        var manager = UnityEngine.Object.FindFirstObjectByType<Gmanager>(); Gmanager.Control = manager;
        var features = manager.GetComponent<RaceFeatureController>() ?? manager.gameObject.AddComponent<RaceFeatureController>();
        features.Configure(manager);
        typeof(Gmanager).GetProperty("Features").GetSetMethod(true).Invoke(manager, new object[] { features });
        var course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>();
        var atmosphere = manager.GetComponent<CampusAtmosphereController>() ?? manager.gameObject.AddComponent<CampusAtmosphereController>();
        atmosphere.Configure(manager, course);
        var capture = typeof(CourseBarrierValidation).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic);
        string phase = Environment.GetEnvironmentVariable("RACING_SCHOOL_CAPTURE") ?? "after";
        capture.Invoke(null, new object[] { new Vector3(440, 13, 815), new Vector3(400, 16, 767), Output + "/school-" + phase + ".png" });
        var tube = new SerializedObject(course).FindProperty("road.tubeInterior").objectReferenceValue as MeshCollider;
        var filter = tube.GetComponent<MeshFilter>();
        var rings = TubeCoursePreparation.ReadRings(filter.sharedMesh, filter.transform);
        Vector3 Center(System.Collections.Generic.List<Vector3> ring) { Vector3 c = Vector3.zero; foreach (var v in ring) c += v; return c / ring.Count; }
        Debug.Log($"TUBE_REFINEMENT_AUDIT: verts={filter.sharedMesh.vertexCount}, rings={rings.Count}, profile={rings[0].Count}, first={Center(rings[0]):F3}, second={Center(rings[1]):F3}, last={Center(rings[rings.Count-1]):F3}, previous={Center(rings[rings.Count-2]):F3}");
        Debug.Log("CIRCUIT_SCHOOL_CAPTURE_PASS: " + phase + ", campus palette applied=" + atmosphere.IsApplied);
        Gmanager.Control = previous;
    }
}
