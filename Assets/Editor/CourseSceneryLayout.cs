using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Absolute, reversible school placement and a generated north-only ground apron.</summary>
public static class CourseSceneryLayout
{
    public const float NorthGroundExtension = 8f;
    // World-space translations from each imported prefab transform, never from the last run.
    private static readonly Dictionary<string, Vector3> Offsets = new Dictionary<string, Vector3>
    {
        { "Cube.008", new Vector3(0f, 0f, 23f) },
        { "Cylinder.002", new Vector3(3f, 0f, 0f) },
        { "人間", new Vector3(0f, 0f, 20f) },
        { "人間.001", new Vector3(0f, 0f, 20f) },
        { "人間.002", new Vector3(0f, 0f, 20f) },
        { "人間.003", new Vector3(0f, 0f, 20f) },
        { "人間.004", new Vector3(0f, 0f, 20f) },
        { "人間.005", new Vector3(0f, 0f, 20f) },
        { "人間.006", new Vector3(0f, 0f, 20f) },
        { "人間.007", new Vector3(0f, 0f, 20f) }
    };

    [MenuItem("Racing/Apply Course Scenery Layout")]
    public static void Run() => Apply(true);

    [MenuItem("Racing/Restore Original Course Scenery")]
    public static void RestoreOriginal()
    {
        Apply(false);
        // Rebuild from the original mesh, removing the apron as well as old apertures.
        TubeCoursePreparation.Run();
    }

    private static Transform FindBranch(string sourceName)
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity")
            throw new InvalidOperationException("Open SampleScene first.");
        Transform found = null;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(candidate);
                if (source == null || source.name != sourceName || source.parent != source.root ||
                    AssetDatabase.GetAssetPath(source) != TubeCoursePreparation.ModelPath) continue;
                if (found != null) throw new InvalidOperationException("Multiple school branches found: " + sourceName);
                found = candidate;
            }
        if (found == null) throw new InvalidOperationException("School source branch is missing: " + sourceName);
        return found;
    }

    private static Vector3 OriginalPosition(Transform target)
    {
        var source = PrefabUtility.GetCorrespondingObjectFromSource(target);
        return target.parent != null ? target.parent.TransformPoint(source.localPosition) : source.localPosition;
    }

    private static void Apply(bool enabled)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode first.");
        // Resolve every branch before changing any placement, so missing source
        // objects fail without leaving a partially applied layout.
        var targets = new Dictionary<string, Transform>();
        foreach (var entry in Offsets) targets.Add(entry.Key, FindBranch(entry.Key));
        foreach (var entry in Offsets)
        {
            Transform target = targets[entry.Key];
            target.position = OriginalPosition(target) + (enabled ? entry.Value : Vector3.zero);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            EditorUtility.SetDirty(target);
        }
        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"COURSE_SCENERY_LAYOUT_PASS: {Offsets.Count} absolute placements {(enabled ? "applied" : "restored")}; children retain local transforms.");
    }

    public static bool IsApplied()
    {
        Transform wing = FindBranch("Cube.008");
        Vector3 offset = wing.position - OriginalPosition(wing);
        if (offset.sqrMagnitude < .000001f) return false;
        if ((offset - Offsets["Cube.008"]).sqrMagnitude > .000001f)
            throw new InvalidOperationException("Unexpected north-wing override; apply or restore Course Scenery Layout first.");
        return true;
    }

    /// <summary>Append strips to source north boundary edges; all existing triangles stay fixed.</summary>
    public static Mesh ExtendNorthGround(Mesh source, Transform transform)
    {
        var vertices = new List<Vector3>(source.vertices);
        var uv = new List<Vector2>();
        Vector2[] originalUV = source.uv;
        for (int i = 0; i < vertices.Count; i++)
            uv.Add(originalUV.Length == vertices.Count ? originalUV[i] : Vector2.zero);
        float north = float.NegativeInfinity;
        foreach (Vector3 vertex in vertices) north = Mathf.Max(north, transform.TransformPoint(vertex).z);
        var triangles = new List<int>[source.subMeshCount];
        int edges = 0;
        for (int submesh = 0; submesh < triangles.Length; submesh++)
        {
            int[] input = source.GetTriangles(submesh);
            triangles[submesh] = new List<int>(input);
            for (int i = 0; i < input.Length; i += 3)
                for (int j = 0; j < 3; j++)
                {
                    int a = input[i + j], b = input[i + (j + 1) % 3], c = input[i + (j + 2) % 3];
                    Vector3 wa = transform.TransformPoint(vertices[a]), wb = transform.TransformPoint(vertices[b]);
                    if (Mathf.Abs(wa.z - north) > .001f || Mathf.Abs(wb.z - north) > .001f) continue;
                    Vector3 wc = transform.TransformPoint(vertices[c]);
                    Vector3 ab = wb - wa, ac = wc - wa, normal = Vector3.Cross(ab, ac);
                    if (Mathf.Abs(normal.y) < .0001f) throw new InvalidOperationException("North ground is not a support surface.");
                    Vector3 delta = new Vector3(0f, -normal.z / normal.y * NorthGroundExtension, NorthGroundExtension);
                    float aa = Vector3.Dot(ab, ab), bb = Vector3.Dot(ac, ac), cross = Vector3.Dot(ab, ac);
                    float denominator = aa * bb - cross * cross;
                    if (Mathf.Abs(denominator) < .000001f) throw new InvalidOperationException("Degenerate north ground edge.");
                    float u = (Vector3.Dot(delta, ab) * bb - Vector3.Dot(delta, ac) * cross) / denominator;
                    float v = (Vector3.Dot(delta, ac) * aa - Vector3.Dot(delta, ab) * cross) / denominator;
                    Vector2 uvDelta = (uv[b] - uv[a]) * u + (uv[c] - uv[a]) * v;
                    int na = vertices.Count, nb = na + 1;
                    vertices.Add(transform.InverseTransformPoint(wa + delta)); uv.Add(uv[a] + uvDelta);
                    vertices.Add(transform.InverseTransformPoint(wb + delta)); uv.Add(uv[b] + uvDelta);
                    // Reverse the existing boundary edge for the adjacent strip.
                    triangles[submesh].AddRange(new[] { b, a, na, b, na, nb });
                    edges++;
                }
        }
        if (edges == 0) throw new InvalidOperationException("No original north ground boundary found.");
        var mesh = new Mesh { name = "Original ground with north support extension", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.subMeshCount = triangles.Length;
        for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }
}
