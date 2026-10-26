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
    [Serializable] private sealed class TubeReport
    {
        public int sourceVertices, smoothVertices, portalProfiles, comparedColliderVertices, portalShoulders, supportedShoulderSamples;
        public float maximumSeamGap, maximumSeamTangentAngle;
    }
    public static void RunTube()
    {
        TubeCourseValidation.Run();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity"); Directory.CreateDirectory(Output);
        var course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>(); course.RebuildCache(); course.RebuildRoad(); Physics.SyncTransforms();
        var tube = new SerializedObject(course).FindProperty("road.tubeInterior").objectReferenceValue as MeshCollider;
        var filter = tube.GetComponent<MeshFilter>();
        var report = new TubeReport { smoothVertices = filter.sharedMesh.vertexCount,
            sourceVertices = PrefabUtility.GetCorrespondingObjectFromSource(filter).sharedMesh.vertexCount };
        foreach (var wall in course.GetComponentsInChildren<RaceBarrierPath>())
        {
            if (!wall.HasPortalBlend) continue;
            var visual = wall.GetComponentInChildren<MeshFilter>(); var collision = wall.GetComponentInChildren<MeshCollider>();
            var vertices = visual.sharedMesh.vertices; var solid = collision.sharedMesh.vertices;
            int rows = vertices.Length / 33;
            for (int row = 0; row < rows; row++) for (int k = 0; k <= 32; k++)
            {
                if (Vector3.Distance(vertices[row * 33 + k], solid[row * 66 + k]) > .0001f)
                    throw new InvalidOperationException("Portal collision must equal the rendered surface.");
                report.comparedColliderVertices++;
            }
            foreach (bool start in new[] { true, false })
            {
                var profile = typeof(RaceBarrierPath).GetField(start ? "portalStart" : "portalEnd", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(wall) as TubePortalProfile;
                if (profile == null) continue; report.portalProfiles++;
                int edge = start ? 0 : rows - 1, adjacent = start ? 1 : rows - 2;
                for (int k = 0; k <= 32; k++)
                {
                    Vector3 a = visual.transform.TransformPoint(vertices[edge * 33 + k]);
                    Vector3 b = visual.transform.TransformPoint(vertices[adjacent * 33 + k]);
                    report.maximumSeamGap = Mathf.Max(report.maximumSeamGap, Vector3.Distance(a, profile.points[k]));
                    report.maximumSeamTangentAngle = Mathf.Max(report.maximumSeamTangentAngle,
                        Vector3.Angle(b - a, profile.outwardTangents[k]));
                }
            }
        }
        foreach (var shoulder in course.GetComponentsInChildren<MeshCollider>())
        {
            if (!shoulder.name.StartsWith("Portal Road Shoulder ")) continue; report.portalShoulders++;
            var v = shoulder.sharedMesh.vertices;
            for (int row = 1; row < v.Length / 2; row++)
            {
                Vector3 a = shoulder.transform.TransformPoint(v[row * 2]), b = shoulder.transform.TransformPoint(v[row * 2 + 1]);
                Vector3 beforeA = shoulder.transform.TransformPoint(v[(row - 1) * 2]), beforeB = shoulder.transform.TransformPoint(v[(row - 1) * 2 + 1]);
                if (Vector3.Distance(a, b) < .2f || Vector3.Distance(beforeA, beforeB) < .2f) continue;
                Vector3 midpoint = (a + b + beforeA + beforeB) * .25f;
                if (!shoulder.Raycast(new Ray(midpoint + Vector3.up * 2, Vector3.down), out _, 3))
                    throw new InvalidOperationException("Portal shoulder support missing at " + shoulder.name + " row=" + row + " point=" + midpoint + " width=" + Vector3.Distance(a,b));
                report.supportedShoulderSamples++;
            }
        }
        if (report.portalShoulders != 2 || report.supportedShoulderSamples < 100) throw new InvalidOperationException("Portal shoulder coverage missing.");
        File.WriteAllText(Output + "/tube-seams.json", JsonUtility.ToJson(report, true));
        if (report.portalProfiles != 4 || report.maximumSeamGap > .02f || report.maximumSeamTangentAngle > 2f)
            throw new InvalidOperationException("Portal continuity failed: " + JsonUtility.ToJson(report));
        var capture = typeof(CourseBarrierValidation).GetMethod("CaptureViews", BindingFlags.Static | BindingFlags.NonPublic);
        capture.Invoke(null, new object[] { course, tube, Output });
        typeof(CourseBarrierValidation).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
            new object[] { new Vector3(282, 8, 573), new Vector3(282, 7, 535), Output + "/tube-west-portal.png" });
        Debug.Log("CIRCUIT_TUBE_PASS: " + JsonUtility.ToJson(report));
    }
    public static void AuditPortal()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var campus = GameObject.Find("1016こうしゃ");
        var region = new Bounds(new Vector3(635, 6, 602), new Vector3(80, 20, 60));
        foreach (var renderer in campus.GetComponentsInChildren<MeshRenderer>())
            if (renderer.bounds.Intersects(region) || renderer.bounds.Intersects(new Bounds(new Vector3(282, 6, 557), new Vector3(80, 20, 60)))) Debug.Log("PORTAL_OBJECT: " + renderer.name + " center=" + renderer.bounds.center + " size=" + renderer.bounds.size);
    }
}
