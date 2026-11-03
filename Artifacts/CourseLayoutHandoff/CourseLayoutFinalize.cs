using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Verification driver copied only into the isolated project's Editor folder.
public static class CourseLayoutFinalize
{
    public static void ApplyToOriginal()
    {
        SchoolMapCourseSetup.Run();
        NeonRaceMarkersBuilder.PlaceFinishGateBatch();
        NightEnnichiVisualBuilder.RefreshCoursePlacement();
        CourseLayoutSnapshot.Run();
        CompareTubeQueries();
        CourseClearanceValidation.Run();
        CoursePlayabilityProbe.Run();
        CourseBarrierValidation.CaptureScene();
        CoursePlayabilityProbe.CaptureSpots();
        Debug.Log("COURSE_LAYOUT_ORIGINAL_APPLY_PASS");
    }

    static Dictionary<string, string> Hashes()
    {
        var result = new Dictionary<string, string>();
        var files = new List<string>(Directory.GetFiles(TubeCoursePreparation.DataFolder));
        files.Add("Assets/Scenes/SampleScene.unity");
        using (var sha = SHA256.Create()) foreach (string file in files)
            result[file] = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file)));
        return result;
    }

    static void SaveRepeatEvidence(string suffix)
    {
        string directory = "Artifacts/CourseLayoutHandoff/repeat-" + suffix;
        Directory.CreateDirectory(directory);
        foreach (string file in Directory.GetFiles(TubeCoursePreparation.DataFolder)) File.Copy(file, Path.Combine(directory, Path.GetFileName(file)), true);
        File.Copy("Assets/Scenes/SampleScene.unity", Path.Combine(directory, "SampleScene.unity"), true);
        var rows = new List<string>();
        foreach (var source in UnityEngine.Object.FindObjectsByType<CourseClearanceSource>(FindObjectsSortMode.None))
        {
            if (source.name != "Plane.013") continue;
            rows.Add(source.name + " source=" + AssetDatabase.GetAssetPath(source.originalMesh) + " mesh=" + source.originalMesh.name);
            rows.Add("local=" + source.transform.localPosition.ToString("R") + " rotation=" + source.transform.localRotation.ToString("R") + " scale=" + source.transform.localScale.ToString("R"));
            rows.Add("world=" + source.transform.position.ToString("R") + " rotation=" + source.transform.rotation.ToString("R") + " scale=" + source.transform.lossyScale.ToString("R"));
        }
        File.WriteAllLines(Path.Combine(directory, "input-state.txt"), rows);
    }

    static void CompareTubeQueries()
    {
        var course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>();
        var tube = new SerializedObject(course).FindProperty("road.tubeInterior").objectReferenceValue as MeshCollider;
        Type type = typeof(CourseBuildingClearance).GetNestedType("TubeVolumeQuery", System.Reflection.BindingFlags.NonPublic);
        object query = Activator.CreateInstance(type, new object[] { tube });
        var method = type.GetMethod("Contains");
        bool previous = Physics.queriesHitBackfaces;
        int samples = 0;
        try
        {
            Physics.queriesHitBackfaces = true;
            for (float progress = 0; progress < course.TotalLength; progress += .5f)
            {
                course.TryGetSampleAtProgress(progress, out var sample);
                bool actual = (bool)method.Invoke(query, new object[] { sample.position, sample.right });
                if (actual != RaceCourseBarriers.IsInsideTube(sample.position, sample.right, tube))
                    throw new InvalidOperationException("Tube query classification differs at " + progress);
                samples++;
            }
        }
        finally { Physics.queriesHitBackfaces = previous; }
        Debug.Log("COURSE_TUBE_QUERY_COMPARISON_PASS: " + samples + " half-metre course samples.");
    }

    public static void Run()
    {
        SchoolMapCourseSetup.Run();
        var before = Hashes();
        SaveRepeatEvidence("before");
        SchoolMapCourseSetup.Run();
        var after = Hashes();
        SaveRepeatEvidence("after");
        var differences = new List<string>();
        if (before.Count != after.Count) throw new InvalidOperationException("Repeated bake changed generated file count.");
        foreach (var entry in before)
            if (!after.TryGetValue(entry.Key, out string hash) || hash != entry.Value)
                differences.Add(entry.Key);
        File.WriteAllLines("Artifacts/CourseLayoutHandoff/repeat-differences.txt", differences);
        if (differences.Count > 0) throw new InvalidOperationException("Repeated bake changed " + differences.Count + " files: " + differences[0]);
        Debug.Log("COURSE_LAYOUT_REPEAT_PASS: identical scene and " + (before.Count - 1) + " generated files.");

        CourseSceneryLayout.RestoreOriginal();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        int restored = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (Transform target in root.GetComponentsInChildren<Transform>(true))
        {
            Transform source = PrefabUtility.GetCorrespondingObjectFromSource(target);
            if (source == null || source.parent != source.root || AssetDatabase.GetAssetPath(source) != TubeCoursePreparation.ModelPath) continue;
            if (source.name != "Cube.008" && source.name != "Cylinder.002" && source.name != "人間" && !source.name.StartsWith("人間.", StringComparison.Ordinal)) continue;
            if ((target.localPosition - source.localPosition).magnitude > .00002f)
                throw new InvalidOperationException("Restore changed original position: " + source.name);
            restored++;
        }
        if (restored != 10 || CourseSceneryLayout.IsApplied()) throw new InvalidOperationException("Not all ten scenery branches restored.");
        Debug.Log("COURSE_LAYOUT_RESTORE_PASS: ten original branch positions.");
        SchoolMapCourseSetup.Run();
        NeonRaceMarkersBuilder.PlaceFinishGateBatch();
        NightEnnichiVisualBuilder.RefreshCoursePlacement();
        CourseLayoutSnapshot.Run();
        CompareTubeQueries();
        CourseClearanceValidation.Run();
        CoursePlayabilityProbe.Run();
        CourseBarrierValidation.CaptureScene();
        CoursePlayabilityProbe.CaptureSpots();
        Debug.Log("COURSE_LAYOUT_FINALIZE_PASS");
    }
}
