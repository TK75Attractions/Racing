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
            new Vector3(496f, 3f, 785.6693f),
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
            new Vector3(536f, 14.5f, 598f),
            new Vector3(528f, 9.5f, 611f),
            new Vector3(514f, 4f, 621f),
            new Vector3(496f, 3f, 619f)
        });
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
            point.FindPropertyRelative("width").floatValue = i >= 2 && i < tubeEnd ? 12f :
                i >= tubeEnd + 2 && i <= tubeEnd + 4 ? 16f : i == tubeEnd + 5 ? 18f : 20f;
            point.FindPropertyRelative("jumpToNext").boolValue = i == tubeEnd + 1;
            point.FindPropertyRelative("preserveAlignment").boolValue = i >= 2 && i < tubeEnd;
        }
        serialized.FindProperty("road").FindPropertyRelative("tubeInterior").objectReferenceValue = tube.GetComponent<MeshCollider>();
        serialized.ApplyModifiedPropertiesWithoutUndo();
        course.RebuildCache();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        TubeCoursePreparation.Run();
        TubeSpeedPatternSetup.Run();
        TubeCourseValidation.Run();
        Debug.Log($"SCHOOL_MAP_COURSE_SETUP_PASS: {points.Count} waypoints, {centers.Count} tube centers.");
    }
}
