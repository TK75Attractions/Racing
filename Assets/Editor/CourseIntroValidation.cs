using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class CourseIntroValidation
{
    public static void InspectScene()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        string folder = "Artifacts/CourseIntro/Probe"; Directory.CreateDirectory(folder);
        var log = new System.Text.StringBuilder();
        foreach (var root in scene.GetRootGameObjects())
        {
            if (!root.name.Contains("1016")) continue;
            foreach (Transform child in root.transform)
            {
                var renderers = child.GetComponentsInChildren<Renderer>(); if (renderers.Length == 0) continue;
                Bounds bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                log.AppendLine(child.name + " center=" + bounds.center + " size=" + bounds.size);
            }
        }
        File.WriteAllText(Path.Combine(folder, "bounds.txt"), log.ToString());
        var go = new GameObject("Course intro probe camera"); var camera = go.AddComponent<Camera>();
        camera.fieldOfView = 55; camera.farClipPlane = 1800;
        Capture(camera, new Vector3(360, 280, 1050), new Vector3(510, 10, 670), folder + "/aerial.jpg");
        Capture(camera, new Vector3(500, 55, 850), new Vector3(540, 15, 680), folder + "/front.jpg");
        UnityEngine.Object.DestroyImmediate(go);
        Debug.Log("COURSE_INTRO_SCENE_PROBE_PASS");
    }
    private static void Capture(Camera camera, Vector3 position, Vector3 look, string path)
    {
        camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(look-position));
        var target = RenderTexture.GetTemporary(1280, 720, 24);
        var old = RenderTexture.active;
        var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToJPG(85));
        }
        finally { camera.targetTexture = null; RenderTexture.active = old; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(pixels); }
    }
}
