using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TubeSpeedPatternSetup
{
    [MenuItem("Racing/Configure Tube Speed Patterns")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity")
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Open SampleScene first.");
            scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        }
        MeshFilter tube = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.name == "BézierCurve.001" &&
                    AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(filter)) == TubeCoursePreparation.ModelPath) tube = filter;
        RaceCourse course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>();
        if (tube == null || course == null) throw new InvalidOperationException("Tube course is missing.");
        Shader shader = Resources.Load<Shader>("TubeSpeedPatterns");
        if (shader == null) throw new InvalidOperationException("Tube speed pattern shader is missing.");
        string path = TubeCoursePreparation.DataFolder + "/TubeSpeedPatterns.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = "Tube Speed Patterns" };
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        float length = 0f;
        Vector3 previous = Vector3.zero;
        var rings = TubeCoursePreparation.ReadRings(tube.sharedMesh, tube.transform);
        for (int i = 0; i < rings.Count; i++)
        {
            Vector3 center = Vector3.zero;
            foreach (Vector3 vertex in rings[i]) center += vertex;
            center /= rings[i].Count;
            if (i > 0) length += Vector3.Distance(previous, center);
            previous = center;
        }
        material.SetFloat("_TubeLength", length);
        EditorUtility.SetDirty(material);
        TubeSpeedPatternController controller = course.GetComponent<TubeSpeedPatternController>();
        if (controller == null) controller = Undo.AddComponent<TubeSpeedPatternController>(course.gameObject);
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("tube").objectReferenceValue = tube;
        serialized.FindProperty("patternMaterial").objectReferenceValue = material;
        serialized.ApplyModifiedProperties();
        controller.Rebuild();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"TUBE_SPEED_PATTERNS_SETUP_PASS: inner-wall overlay, {length:F1}m tube, original mesh/collision retained.");
    }
}
