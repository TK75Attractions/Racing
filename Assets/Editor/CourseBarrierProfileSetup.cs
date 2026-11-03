using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Authors SampleScene's race-left home wall without storing brittle progress constants.</summary>
public static class CourseBarrierProfileSetup
{
    [MenuItem("Racing/Configure Home Straight Barrier Profile")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity") throw new InvalidOperationException("Open SampleScene first.");
        RaceCourse course = null;
        MeshFilter wall = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.GetComponent<RaceCourse>() != null) course = root.GetComponent<RaceCourse>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.name == "Plane.019" && filter.transform.parent != null && filter.transform.parent.name == "Cube.047") wall = filter;
        }
        if (course == null || wall == null) throw new InvalidOperationException("RaceCourse or Cube.047/Plane.019 is missing.");
        course.RebuildCache();
        float homeProgress = course.GetProgressDistance(new Vector3(495f, 3f, 700f));
        course.TryGetSampleAtProgress(homeProgress, out var home);
        // Read the imported wall, even after terrain preparation has cut its collider mesh.
        MeshFilter original = PrefabUtility.GetCorrespondingObjectFromSource(wall);
        Mesh mesh = original != null ? original.sharedMesh : wall.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        var planes = new Dictionary<int, Bounds>();
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = wall.transform.TransformPoint(vertices[triangles[i]]);
            Vector3 b = wall.transform.TransformPoint(vertices[triangles[i + 1]]);
            Vector3 c = wall.transform.TransformPoint(vertices[triangles[i + 2]]);
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            if (Mathf.Abs(normal.x) < .99f) continue;
            var bounds = new Bounds(a, Vector3.zero); bounds.Encapsulate(b); bounds.Encapsulate(c);
            if (bounds.size.y < 2f) continue;
            int key = Mathf.RoundToInt(bounds.center.x * 100f);
            if (planes.TryGetValue(key, out Bounds accumulated)) { accumulated.Encapsulate(bounds); planes[key] = accumulated; }
            else planes.Add(key, bounds);
        }
        Bounds face = default;
        float nearest = float.PositiveInfinity;
        foreach (Bounds candidate in planes.Values)
        {
            if (candidate.size.z < 20f || home.position.z < candidate.min.z || home.position.z > candidate.max.z) continue;
            float error = Mathf.Abs(candidate.center.x - home.rightEdge.x);
            if (error >= nearest) continue;
            nearest = error; face = candidate;
        }
        if (nearest > 5f) throw new InvalidOperationException("Cannot locate the east home wall beside the road.");
        RaceBarrierProfiles profiles = course.GetComponent<RaceBarrierProfiles>();
        if (profiles == null) profiles = Undo.AddComponent<RaceBarrierProfiles>(course.gameObject);
        Undo.RecordObject(profiles, "Configure home barrier profile");
        profiles.reverseRaceDirection = true;
        profiles.sections = new List<RaceBarrierProfiles.Section>
        {
            new RaceBarrierProfiles.Section
            {
                side = RaceBarrierProfiles.Side.Left, landmark = wall.transform,
                localStart = wall.transform.InverseTransformPoint(new Vector3(face.center.x, home.position.y, face.max.z)),
                localEnd = wall.transform.InverseTransformPoint(new Vector3(face.center.x, home.position.y, face.min.z)),
                followEntryCorner = true, entryMeters = 30f, exitMeters = 20f
            }
        };
        var centers = new List<Vector3>(); course.CopyCenterPathWorld(centers);
        if (profiles.Resolve(centers, course.ClosedLoop).Count != 1)
            throw new InvalidOperationException("The home wall anchors did not resolve to a local course interval.");
        EditorUtility.SetDirty(profiles);
        EditorSceneManager.MarkSceneDirty(scene);
        course.RebuildRoad();
    }
}
