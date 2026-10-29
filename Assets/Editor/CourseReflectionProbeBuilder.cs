using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Three local, baked-only captures. No runtime capture or additional lights.</summary>
public static class CourseReflectionProbeBuilder
{
    private const string RootName = "Course Baked Reflections";
    private const string Folder = "Assets/Art/RenderBudget/Reflections";

    [MenuItem("Racing/Rendering/Place Baked Reflection Probes")]
    public static void Run()
    {
        Scene scene = RenderPreparationScene.Open();
        Place(scene);
        RenderPreparationScene.Save(scene);
        Debug.Log("COURSE_REFLECTIONS_PLACED: 3 local baked-only probes; run Bake with graphics enabled.");
    }

    private static GameObject Place(Scene scene)
    {
        RaceCourse course = RenderPreparationScene.Find<RaceCourse>(scene);
        course.RebuildCache();
        if (!course.HasValidPath) throw new InvalidOperationException("Course path is invalid.");
        var inside = new List<Vector3>();
        for (float d = 0; d < course.TotalLength; d += 5f)
            if (course.TryGetSampleAtProgress(d, out var sample) && course.IsPointInsideTube(sample.position))
                inside.Add(sample.position);
        if (inside.Count == 0) throw new InvalidOperationException("No tube course samples found.");
        GameObject owner = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == RootName)
            {
                if (owner != null) throw new InvalidOperationException("Duplicate reflection roots.");
                owner = root;
            }
        if (owner == null)
        {
            owner = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(owner, scene);
        }
        Vector3 start = RenderPreparationScene.Find<GoalSensor>(scene).transform.position;
        // Authored school-front reference; project to the current course, not the building interior.
        course.TryGetSampleAtProgress(course.GetProgressDistance(new Vector3(496f, 3f, 700f)), out var school);
        Configure(owner.transform, "Start", start + Vector3.up * 2f, new Vector3(100, 30, 100));
        Configure(owner.transform, "SchoolFront", school.position + Vector3.up * 2f, new Vector3(140, 50, 120));
        Configure(owner.transform, "Tube", inside[inside.Count / 2] + Vector3.up * 2f, new Vector3(100, 50, 100));
        return owner;
    }

    private static void Configure(Transform owner, string name, Vector3 position, Vector3 size)
    {
        Transform child = owner.Find(name);
        if (child == null)
        {
            child = new GameObject(name).transform;
            child.SetParent(owner, false);
        }
        child.SetPositionAndRotation(position, Quaternion.identity);
        child.localScale = Vector3.one;
        ReflectionProbe probe = child.GetComponent<ReflectionProbe>();
        if (probe == null) probe = child.gameObject.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Baked;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.resolution = 128;
        probe.hdr = true;
        probe.boxProjection = true;
        probe.size = size;
        probe.center = Vector3.zero;
        probe.blendDistance = 8f;
        probe.nearClipPlane = .3f;
        probe.farClipPlane = 250f;
        probe.intensity = 1f;
        probe.importance = name == "Tube" ? 2 : 1;
        probe.clearFlags = ReflectionProbeClearFlags.Skybox;
        probe.cullingMask = ~LayerMask.GetMask("UI");
        probe.renderDynamicObjects = true; // Includes generated road and neon, only during offline bake.
    }

    [MenuItem("Racing/Rendering/Bake Night Reflection Probes")]
    public static void Bake()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException("Reflection baking needs graphics; omit -nographics.");
        Scene scene = RenderPreparationScene.Open();
        GameObject owner = Place(scene);
        RaceEnvironmentController source = RenderPreparationScene.Find<RaceEnvironmentController>(scene);
        if (source.CurrentTimeOfDay != RaceEnvironmentController.TimeOfDay.Night)
            throw new InvalidOperationException("Save the night environment before baking night reflections.");
        RenderPreparationScene.Folder(Folder);
        // A temporary controller restores the original lights/render settings on disable.
        // Never save the transient runtime sky material or skyline into the scene.
        var temporary = new GameObject("Reflection Bake Environment");
        temporary.SetActive(false);
        var environment = temporary.AddComponent<RaceEnvironmentController>();
        EditorUtility.CopySerialized(source, environment);
        environment.enabled = false;
        try
        {
            temporary.SetActive(true);
            environment.ApplyEnvironment();
            foreach (ReflectionProbe probe in owner.GetComponentsInChildren<ReflectionProbe>())
            {
                string path = Folder + "/" + probe.name + "Night.exr";
                if (!Lightmapping.BakeReflectionProbe(probe, path))
                    throw new InvalidOperationException("Reflection bake failed: " + probe.name);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                Texture texture = AssetDatabase.LoadAssetAtPath<Texture>(path);
                if (texture == null) throw new InvalidOperationException("Missing baked cubemap: " + path);
                probe.bakedTexture = texture;
            }
        }
        finally
        {
            // This controller is not ExecuteAlways. Invoke its existing restoration path explicitly
            // in Edit Mode, without relying on Editor MonoBehaviour lifecycle callbacks.
            environment.SendMessage("OnDisable", SendMessageOptions.RequireReceiver);
            UnityEngine.Object.DestroyImmediate(temporary);
        }
        RenderPreparationScene.Save(scene);
        Debug.Log("COURSE_REFLECTIONS_BAKED: 3 local night probes, 128 HDR, no realtime updates.");
    }
}
