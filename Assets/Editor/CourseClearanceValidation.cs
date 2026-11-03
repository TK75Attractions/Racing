using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Checks saved school collision meshes against the same volume used by the bake.</summary>
public static class CourseClearanceValidation
{
    private const float InteriorMargin = .05f;

    [MenuItem("Racing/Validate Course Building Clearance")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/SampleScene.unity");
        try
        {
            RaceCourse course = null;
            var sources = new List<CourseClearanceSource>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (course == null) course = root.GetComponentInChildren<RaceCourse>(true);
                sources.AddRange(root.GetComponentsInChildren<CourseClearanceSource>(true));
            }
            if (course == null || sources.Count == 0)
                throw new InvalidOperationException("SampleScene has no course or clearance sources; bake and save it first.");
            course.RebuildCache();
            MeshCollider tube = new SerializedObject(course).FindProperty("road.tubeInterior")
                .objectReferenceValue as MeshCollider;
            var volumes = CourseBuildingClearance.CreateVolumes(course, tube);
            if (volumes.prisms.Count == 0) throw new InvalidOperationException("The clearance volume is empty.");
            var checkedTargets = new HashSet<MeshCollider>();
            var seen = new HashSet<int>();
            var candidates = new List<int>();
            long total = 0, checkedPoints = 0;
            int objects = 0, invalid = 0;
            foreach (CourseClearanceSource source in sources)
            {
                MeshCollider target = source.target;
                if (target == null || target.sharedMesh == null || target.gameObject.scene != scene)
                {
                    invalid++;
                    Debug.LogError("COURSE_CLEARANCE_VALIDATION_INVALID: " + HierarchyPath(source.transform) +
                        " has no target mesh in SampleScene.", source);
                    continue;
                }
                if (!checkedTargets.Add(target)) continue;
                objects++;
                // Include disabled/inactive targets too: the geometry must remain safe if re-enabled.
                Mesh mesh = target.sharedMesh;
                Matrix4x4 toWorld = CourseBuildingClearance.PhysicsMatrix(target.transform);
                Vector3[] vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = toWorld.MultiplyPoint3x4(vertices[i]);
                int[] triangles = mesh.triangles;
                long count = 0;
                Vector3 representative = default;
                int representativeTriangle = -1;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                    Check(a, i / 3); Check(b, i / 3); Check(c, i / 3);
                    Check((a + b) * .5f, i / 3); Check((b + c) * .5f, i / 3); Check((c + a) * .5f, i / 3);
                    Check((a + b + c) / 3f, i / 3);
                }
                if (count != 0)
                {
                    total += count;
                    Debug.LogError(string.Format(CultureInfo.InvariantCulture,
                        "COURSE_CLEARANCE_VALIDATION_RESIDUAL: {0}: {1} sampled points; representative=({2:F6}, {3:F6}, {4:F6}), triangle={5}, enabled={6}",
                        HierarchyPath(target.transform), count, representative.x, representative.y, representative.z,
                        representativeTriangle, target.enabled), target);
                }

                void Check(Vector3 point, int triangle)
                {
                    checkedPoints++;
                    if (!volumes.ContainsInterior(point, InteriorMargin, seen, candidates)) return;
                    if (count == 0) { representative = point; representativeTriangle = triangle; }
                    count++;
                }
            }
            if (total != 0 || invalid != 0)
                throw new InvalidOperationException($"Course clearance validation failed: {total} interior sampled points, {invalid} invalid sources, {objects} objects.");
            Debug.Log($"COURSE_CLEARANCE_VALIDATION_PASS: {objects} objects, {checkedPoints} sampled points, 0 interior points (margin={InteriorMargin:F2}m).");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static string HierarchyPath(Transform transform) => transform.parent == null ? transform.name :
        HierarchyPath(transform.parent) + "/" + transform.name;
}
