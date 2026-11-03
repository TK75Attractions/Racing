using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Aligns the race to the supplied 1016 school model, then rebakes its collision surfaces.</summary>
public static class SchoolMapCourseSetup
{
    [MenuItem("Racing/Align Course To 1016 School Map")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode first.");
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        RaceCourse course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>();
        MeshFilter tube = null;
        GameObject map = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(filter)) !=
                    TubeCoursePreparation.ModelPath) continue;
                map = root;
                if (filter.name == "BézierCurve.001") tube = filter;
                // New model meshes need collision too. Preserve existing per-object overrides.
                if (filter.GetComponent<MeshCollider>() == null)
                    filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            }
        if (course == null || tube == null || map == null)
            throw new InvalidOperationException("The school map, tube and course must be present.");
        map.name = "1016こうしゃ";
        PrefabUtility.RecordPrefabInstancePropertyModifications(map);
        CourseSceneryLayout.Run();

        var centers = new List<Vector3>();
        foreach (var ring in TubeCoursePreparation.ReadRings(
            PrefabUtility.GetCorrespondingObjectFromSource(tube).sharedMesh, tube.transform))
        {
            Vector3 center = Vector3.zero;
            foreach (Vector3 vertex in ring) center += vertex;
            center = center / ring.Count - Vector3.up * 10f;
            // The supplied tube doubles back over itself at the west end. The road
            // crosses the coincident section once; the visual tube remains untouched.
            int coincident = centers.FindIndex(p => (p - center).sqrMagnitude < .0001f);
            if (coincident >= 0) centers.RemoveRange(coincident, centers.Count - coincident);
            centers.Add(center);
        }
        var points = new List<Vector3>
        {
            // Bevel both ends of the north-shifted straight. Retain the old west
            // endpoint so the complete original tube lead-in remains authored unchanged.
            new Vector3(485.2f, 3f, 807f),
            new Vector3(267.5f, 3f, 807f),
            new Vector3(258.2f, 3f, 796.7f)
        };
        for (int i = centers.Count - 1; i >= 0; i--) points.Add(centers[i]);
        int tubeEnd = points.Count;
        // Land earlier to leave a gradual descent into the relocated, east-facing portal.
        points.AddRange(new[]
        {
            // Keep both sides straight at the landing edge, including its shrinking
            // smoothing window, so the barrier's edge tangent cannot reverse there.
            new Vector3(619f, 8f, 598f),
            new Vector3(596f, 16f, 598f),
            new Vector3(584f, 18f, 598f),
            // The shrinking smoothing window roughly halves this 12.5 m raw
            // alignment. Leave >6 m aligned with the airborne X-axis chord so
            // the neighbouring-sample edge tangent cannot fold the takeoff quad.
            new Vector3(571.5f, 17.7f, 598f),
            // Ease in along z = 598 + .067 * (571.5 - x). The gentler diagonal
            // keeps total heading change over the last 40 m below 6 degrees.
            // Raise the entrance locally to clear its low southern wall while
            // retaining the 12.4 m opening width and gradual width recovery.
            new Vector3(549f, 17.7f, 599.508f),
            new Vector3(536f, 15.5f, 600.379f),
            // Move the lower connection east to clear Plane.019; extend the
            // entrance taper north to keep the full-width rate below .2 m/m.
            new Vector3(513.5f, 3f, 601.886f),
            new Vector3(513.9f, 4f, 621.2f),
            new Vector3(496f, 3f, 619f),
            // Shift only the long home straight west. Keep the original corner
            // east of Plane.019's northern return wall instead of narrowing it.
            new Vector3(494.7f, 3f, 650f),
            new Vector3(494.7f, 3f, 720f),
            new Vector3(494.7f, 3f, 797.5f)
        });
        // Full widths, in the same order as the ground points above. The 12 m
        // jump needs an 18 m takeoff to reach the 20 m landing within 0.2 m/m.
        // Narrow locally at the entrance; recover 20 m along the entire home.
        var groundWidths = new[] { 15.95f, 20f, 18f, 15.9f, 12.4f, 14.4f, 16.1f, 18.3f, 20f, 20f, 20f, 20f };
        var serialized = new SerializedObject(course);
        var waypoints = serialized.FindProperty("waypoints");
        waypoints.arraySize = points.Count;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 local = course.transform.InverseTransformPoint(points[i]);
            var point = waypoints.GetArrayElementAtIndex(i);
            point.FindPropertyRelative("position").vector2Value = new Vector2(local.x, local.z);
            point.FindPropertyRelative("height").floatValue = local.y;
            point.FindPropertyRelative("curve").floatValue = 0f;
            point.FindPropertyRelative("width").floatValue = i < 3 ? 20f :
                i < tubeEnd ? 12f : groundWidths[i - tubeEnd];
            point.FindPropertyRelative("jumpToNext").boolValue = i == tubeEnd + 1;
            point.FindPropertyRelative("preserveAlignment").boolValue = i >= 3 && i < tubeEnd;
        }
        serialized.FindProperty("road").FindPropertyRelative("tubeInterior").objectReferenceValue = tube.GetComponent<MeshCollider>();
        serialized.ApplyModifiedPropertiesWithoutUndo();
        course.RebuildCache();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        TubeCoursePreparation.Run();
        TubeSpeedPatternSetup.Run();
        TubeCourseValidation.Run();
        // Terrain preparation already aligns start/checkpoints, goal and items.
        // Persistent lanterns/landmarks, finish gate and hand-placed props need
        // the follow-up steps documented in RaceCourse.md after this operation.
        Debug.Log($"SCHOOL_MAP_COURSE_SETUP_PASS: {points.Count} waypoints, {centers.Count} tube centers.");
    }
}
